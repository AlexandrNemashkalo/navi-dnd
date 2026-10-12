using NAudio.Wave;
using NAudio.Vorbis;

namespace NaviDnD.Helpers;

// The one place that knows which audio backend and decoders this OS has. Windows: WaveOutEvent and NAudio's own readers,
// exactly as before. macOS: AudioQueue (AudioQueuePlayer) and the managed NLayer for mp3. Anything else: silence.
// Audio is not a critical feature: failures are written to logs/audio.log and the callers go silent.
internal static class AudioOutput
{
    // Same as WaveOutEvent's default DesiredLatency.
    public const int DefaultLatencyMs = 300;

    public static IWavePlayer Create(int latencyMs = DefaultLatencyMs)
    {
        if (OperatingSystem.IsWindows()) return new WaveOutEvent { DesiredLatency = latencyMs };
        if (OperatingSystem.IsMacOS()) return new AudioQueuePlayer(latencyMs);
        return new SilentWavePlayer();
    }

    // A wav/mp3/ogg file: the stream (for rewinding and Dispose) and its samples.
    public static WaveStream Open(string path, out ISampleProvider samples)
    {
        if (path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)) { var v = new VorbisWaveReader(path); samples = v; return v; }
        if (OperatingSystem.IsWindows()) { var a = new AudioFileReader(path); samples = a; return a; }
        WaveStream reader = path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ? OpenMp3(path) : new WaveFileReader(path);
        samples = reader.ToSampleProvider();
        return reader;
    }

    // Windows decodes mp3 through ACM (Mp3FileReader); elsewhere there is no ACM, so NLayer decodes the frames.
    public static WaveStream OpenMp3(string path) => OperatingSystem.IsWindows()
        ? new Mp3FileReader(path)
        : new Mp3FileReaderBase(path, format => new NLayer.NAudioSupport.Mp3FrameDecompressor(format));

    // macOS/Linux only: on Windows audio failures stay silent, as they always were.
    public static void Log(Exception ex)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            string dir = Path.Combine(AppConfig.ProjectRoot, "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "audio.log"), $"{DateTime.Now:s} {ex.GetType().Name}: {ex.Message}\n");
        }
        catch { }
    }

    // No audio device on this OS: accepts everything, plays nothing and reports the playback as finished at once
    // (whoever waits for the end of a clip does not wait forever).
    private sealed class SilentWavePlayer : IWavePlayer
    {
        public float Volume { get; set; } = 1f;
        public PlaybackState PlaybackState => PlaybackState.Stopped;
        public WaveFormat? OutputWaveFormat { get; private set; }
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;

        public void Init(IWaveProvider waveProvider) => OutputWaveFormat = waveProvider.WaveFormat;
        public void Play() => PlaybackStopped?.Invoke(this, new StoppedEventArgs());
        public void Stop() { }
        public void Pause() { }
        public void Dispose() { }
    }
}
