using System.Runtime.InteropServices;
using NAudio.Wave;

namespace NaviDnD.Helpers;

// macOS output: an IWavePlayer over AudioToolbox's AudioQueue, standing in for WaveOutEvent. A few buffers circulate:
// the queue hands a played buffer back to the callback (on its own thread), it is refilled from the provider and enqueued again.
// Same semantics as WaveOutEvent: when the provider runs dry the queued buffers play out, then the state becomes Stopped and
// PlaybackStopped fires; Stop() cuts off at once and also fires it.
internal sealed class AudioQueuePlayer(int latencyMs) : IWavePlayer
{
    private const string Toolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    private const int BufferCount = 3;
    private const uint LinearPcm = 0x6C70636D; // 'lpcm'
    private const uint FlagIsFloat = 1, FlagIsSignedInteger = 4, FlagIsPacked = 8;
    private const uint ParamVolume = 1;

    // Kept in a static field: the queue holds only a raw pointer to it.
    private static readonly OutputCallback Callback = OnBufferDone;

    private readonly object _lock = new();
    private readonly IntPtr[] _buffers = new IntPtr[BufferCount];
    private IWaveProvider? _source;
    private IntPtr _queue;
    private GCHandle _self;
    private byte[] _scratch = [];
    private int _queued;              // buffers currently held by the queue
    private int _generation;          // a new one on every start from Stopped: late callbacks of an old run are ignored
    private volatile bool _draining;  // the provider ran dry, the rest is playing out
    private volatile PlaybackState _state = PlaybackState.Stopped;
    private float _volume = 1f;

    public PlaybackState PlaybackState => _state;
    public WaveFormat? OutputWaveFormat => _source?.WaveFormat;
    public event EventHandler<StoppedEventArgs>? PlaybackStopped;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            lock (_lock) if (_queue != IntPtr.Zero) AudioQueueSetParameter(_queue, ParamVolume, _volume);
        }
    }

    public void Init(IWaveProvider waveProvider)
    {
        lock (_lock)
        {
            if (_queue != IntPtr.Zero) throw new InvalidOperationException("Already initialised");
            try
            {
                var format = waveProvider.WaveFormat;
                uint flags = (format.Encoding, format.BitsPerSample) switch
                {
                    (WaveFormatEncoding.IeeeFloat, 32) => FlagIsFloat | FlagIsPacked,
                    (WaveFormatEncoding.Pcm, 8) => FlagIsPacked, // 8-bit wav is unsigned
                    (WaveFormatEncoding.Pcm, 16 or 24 or 32) => FlagIsSignedInteger | FlagIsPacked,
                    _ => throw new NotSupportedException($"Audio format {format} is not supported")
                };
                var description = new StreamDescription
                {
                    SampleRate = format.SampleRate, FormatId = LinearPcm, FormatFlags = flags,
                    BytesPerPacket = (uint)format.BlockAlign, FramesPerPacket = 1, BytesPerFrame = (uint)format.BlockAlign,
                    ChannelsPerFrame = (uint)format.Channels, BitsPerChannel = (uint)format.BitsPerSample
                };
                _source = waveProvider;
                _self = GCHandle.Alloc(this);
                Check(AudioQueueNewOutput(ref description, Callback, GCHandle.ToIntPtr(_self), IntPtr.Zero, IntPtr.Zero, 0, out _queue), "AudioQueueNewOutput");

                // Like WaveOutEvent, the latency is split between the buffers; too short buffers crackle on underrun.
                int bufferMs = Math.Max(latencyMs / 2, 20);
                int bytes = format.AverageBytesPerSecond * bufferMs / 1000;
                bytes -= bytes % format.BlockAlign;
                _scratch = new byte[bytes];
                for (int i = 0; i < BufferCount; i++)
                    Check(AudioQueueAllocateBuffer(_queue, (uint)bytes, out _buffers[i]), "AudioQueueAllocateBuffer");
                AudioQueueSetParameter(_queue, ParamVolume, _volume);
            }
            catch
            {
                Release(); // the caller logs and goes silent
                throw;
            }
        }
    }

    public void Play()
    {
        bool empty = false;
        lock (_lock)
        {
            if (_queue == IntPtr.Zero) throw new InvalidOperationException("Init must be called first");
            if (_state == PlaybackState.Playing) return;
            if (_state == PlaybackState.Stopped)
            {
                _generation++;
                _queued = 0;
                _draining = false;
                foreach (var buffer in _buffers)
                    if (!Fill(buffer) || !Enqueue(buffer)) { _draining = true; break; }
                empty = _queued == 0;
            }
            if (!empty)
            {
                _state = PlaybackState.Playing;
                Check(AudioQueueStart(_queue, IntPtr.Zero), "AudioQueueStart");
            }
        }
        if (empty) RaiseStopped(null);
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (_state != PlaybackState.Playing) return;
            _state = PlaybackState.Paused;
            AudioQueuePause(_queue);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_queue == IntPtr.Zero || _state == PlaybackState.Stopped) return;
            _state = PlaybackState.Stopped;
            AudioQueueStop(_queue, true); // the callback takes no lock, so waiting for it here cannot deadlock
        }
        RaiseStopped(null);
    }

    public void Dispose()
    {
        Stop();
        lock (_lock) Release();
    }

    private void Release()
    {
        if (_queue != IntPtr.Zero) AudioQueueDispose(_queue, true); // also frees the buffers; no callbacks after it returns
        _queue = IntPtr.Zero;
        if (_self.IsAllocated) _self.Free();
    }

    private static void OnBufferDone(IntPtr user, IntPtr queue, IntPtr buffer)
    {
        // An exception must not escape onto the native audio thread — it would take the game down.
        AudioQueuePlayer? player = null;
        try
        {
            player = GCHandle.FromIntPtr(user).Target as AudioQueuePlayer;
            player?.Refill(buffer);
        }
        catch (Exception ex)
        {
            AudioOutput.Log(ex);
            if (player != null) ThreadPool.QueueUserWorkItem(_ => player.Finish(Volatile.Read(ref player._generation), ex));
        }
    }

    private void Refill(IntPtr buffer)
    {
        if (_state == PlaybackState.Stopped) return;
        int generation = Volatile.Read(ref _generation);
        int left = Interlocked.Decrement(ref _queued);
        if (!_draining)
        {
            if (Fill(buffer) && Enqueue(buffer)) return;
            _draining = true;
        }
        if (left == 0) ThreadPool.QueueUserWorkItem(_ => Finish(generation, null));
    }

    // The provider ran dry and every buffer has been played: stop like WaveOutEvent does at the end of a stream.
    private void Finish(int generation, Exception? error)
    {
        lock (_lock)
        {
            if (_generation != generation || _state == PlaybackState.Stopped || _queue == IntPtr.Zero) return;
            _state = PlaybackState.Stopped;
            AudioQueueStop(_queue, true);   // everything has played; a synchronous stop leaves nothing pending for a later Play
        }
        RaiseStopped(error);
    }

    private bool Fill(IntPtr buffer)
    {
        int blockAlign = _source!.WaveFormat.BlockAlign;
        int read = _source.Read(_scratch, 0, _scratch.Length);
        read -= read % blockAlign;
        if (read <= 0) return false;
        Marshal.Copy(_scratch, 0, Marshal.ReadIntPtr(buffer, BufferDataOffset), read);
        Marshal.WriteInt32(buffer, BufferByteSizeOffset, read);
        return true;
    }

    private bool Enqueue(IntPtr buffer)
    {
        Interlocked.Increment(ref _queued);
        if (AudioQueueEnqueueBuffer(_queue, buffer, 0, IntPtr.Zero) == 0) return true;
        Interlocked.Decrement(ref _queued);
        return false;
    }

    private void RaiseStopped(Exception? error) => PlaybackStopped?.Invoke(this, new StoppedEventArgs(error));

    private static void Check(int status, string call)
    {
        if (status != 0) throw new InvalidOperationException($"{call} failed: OSStatus {status}");
    }

    // AudioQueueBuffer: UInt32 mAudioDataBytesCapacity; void* mAudioData; UInt32 mAudioDataByteSize; ... (64-bit layout).
    private const int BufferDataOffset = 8, BufferByteSizeOffset = 16;

    [StructLayout(LayoutKind.Sequential)]
    private struct StreamDescription // AudioStreamBasicDescription
    {
        public double SampleRate;
        public uint FormatId, FormatFlags, BytesPerPacket, FramesPerPacket, BytesPerFrame, ChannelsPerFrame, BitsPerChannel, Reserved;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void OutputCallback(IntPtr userData, IntPtr queue, IntPtr buffer);

    [DllImport(Toolbox)]
    private static extern int AudioQueueNewOutput(ref StreamDescription format, OutputCallback callback, IntPtr userData,
        IntPtr runLoop, IntPtr runLoopMode, uint flags, out IntPtr queue);
    [DllImport(Toolbox)] private static extern int AudioQueueAllocateBuffer(IntPtr queue, uint size, out IntPtr buffer);
    [DllImport(Toolbox)] private static extern int AudioQueueEnqueueBuffer(IntPtr queue, IntPtr buffer, uint packetDescriptions, IntPtr descriptions);
    [DllImport(Toolbox)] private static extern int AudioQueueStart(IntPtr queue, IntPtr startTime);
    [DllImport(Toolbox)] private static extern int AudioQueuePause(IntPtr queue);
    [DllImport(Toolbox)] private static extern int AudioQueueStop(IntPtr queue, [MarshalAs(UnmanagedType.U1)] bool immediate);
    [DllImport(Toolbox)] private static extern int AudioQueueDispose(IntPtr queue, [MarshalAs(UnmanagedType.U1)] bool immediate);
    [DllImport(Toolbox)] private static extern int AudioQueueSetParameter(IntPtr queue, uint parameter, float value);
}
