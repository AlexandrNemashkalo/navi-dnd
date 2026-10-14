using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NaviDnD.Helpers;

// Music and environmental sounds of the game. One output device and mixer - the tracks sound in layers: each
// has its own volume and smooth appearance/fading (fade). What plays is decided by MusicDirector (by the place
// of the hero, battle, time of day): - change of music: Play(new) + Stop(old) - cross-fading;  - several tracks
// together (music + ambient sounds, later - combat layers): Play with different id, volume - SetLayerVolume.
// Volume: layer × “MUSIC VOLUME” × total “VOLUME” (Sound.Volume); “MUSIC: OFF” / “AMBIOROUS SOUNDS: OFF” -
// silence without stopping tracks. Formats: wav, mp3, ogg.
internal static class Music
{
    public static string SoundDir => Path.Combine(AppConfig.ProjectRoot, "sound");

    public static bool Enabled { get; set; } = true;
    public static bool AmbienceEnabled { get; set; } = true;
    public static float Volume { get; set; } = 0.4f;   // “MUSIC VOLUME”, 0..1 The GM’s speech sounds (Speech): the music and surroundings gradually fade out until
    // DuckLevel, after which they return.
    public static volatile bool Ducked;
    private const float DuckLevel = 0.3f, DuckDownSec = 0.4f, DuckUpSec = 1.5f;

    private static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
    private static readonly object _lock = new();
    private static IWavePlayer? _output;
    private static bool _paused;

    public static void SetPaused(bool paused)
    {
        lock (_lock)
        {
            if (_paused == paused) return;
            _paused = paused;
            if (_output == null) return;
            if (paused) _output.Pause();
            else _output.Play();
        }
    }
    private static MixingSampleProvider? _mixer;
    private static readonly Dictionary<string, Layer> _layers = [];

    // Turn on the track id (path from the folder sound) with a smooth appearance; already playing - only
    // volume. loop - in a circle (ambient sounds, battle); otherwise the track finishes playing and fades out
    // by itself (IsPlaying → false). ambience - ambient sound: muffled by its setting, not "MUSIC".
    public static void Play(string id, string file, float volume = 1f, int fadeMs = 2000, bool loop = true, bool ambience = false)
    {
        lock (_lock)
        {
            if (_layers.TryGetValue(id, out var playing))
            {
                if (!playing.Ended) { playing.FadeTo(volume, fadeMs); return; }
                _layers.Remove(id);
                playing.Dispose();
            }
            string path = Path.IsPathRooted(file) ? file : Path.Combine(SoundDir, file);
            if (!File.Exists(path) || !EnsureOutput()) return;
            try
            {
                var layer = new Layer(path, loop, ambience);
                layer.FadeTo(volume, fadeMs);
                _layers[id] = layer;
                _mixer!.AddMixerInput(layer);
            }
            catch (Exception ex) { AudioOutput.Log(ex); /*file cannot be read - without it*/ }
        }
    }

    // The track is still playing (it has not finished playing and has not stopped).
    public static bool IsPlaying(string id)
    {
        lock (_lock) return _layers.TryGetValue(id, out var layer) && !layer.Ended;
    }

    // Smoothly mute the track and remove it from the mixer.
    public static void Stop(string id, int fadeMs = 2000)
    {
        lock (_lock)
        {
            if (!_layers.Remove(id, out var layer)) return;
            if (layer.Ended) { layer.Dispose(); return; }   // finished playing - the mixer has already removed it
            layer.FadeTo(0, fadeMs, then: () => { lock (_lock) _mixer?.RemoveMixerInput(layer); layer.Dispose(); });
        }
    }

    // Layer volume (0..1) with fade - for blending multiple tracks.
    public static void SetLayerVolume(string id, float volume, int fadeMs = 1000)
    {
        lock (_lock) if (_layers.TryGetValue(id, out var layer)) layer.FadeTo(volume, fadeMs);
    }

    private static bool EnsureOutput()
    {
        if (_output != null) return true;
        try
        {
            _mixer = new MixingSampleProvider(Format) { ReadFully = true };   // without tracks - silence, the device does not close
            _output = AudioOutput.Create(200);
            _output.Init(new Ducking(_mixer));
            if (!_paused) _output.Play();
            return true;
        }
        catch (Exception ex) { AudioOutput.Log(ex); _output = null; _mixer = null; return false; }
    }

    // Quiet the entire mixer to the speech: down quickly, back down slowly.
    private sealed class Ducking(ISampleProvider source) : ISampleProvider
    {
        private float _gain = 1f;
        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = source.Read(buffer, offset, count);
            bool ducked = Ducked;
            float target = ducked ? DuckLevel : 1f;
            float step = (1f - DuckLevel) / ((ducked ? DuckDownSec : DuckUpSec) * Format.SampleRate * Format.Channels);
            for (int i = 0; i < read; i++)
            {
                if (_gain != target) _gain = _gain > target ? MathF.Max(target, _gain - step) : MathF.Min(target, _gain + step);
                buffer[offset + i] *= _gain;
            }
            return read;
        }
    }

    // Track: file (wav/mp3/ogg), reduced to mixer format, × layer volume (with attenuation) × total.
    private sealed class Layer : ISampleProvider, IDisposable
    {
        private readonly WaveStream _reader;
        private readonly ISampleProvider _source;
        private readonly bool _loop, _ambience;
        private float _gain, _target, _step;
        private Action? _then;
        private volatile bool _ended;

        public WaveFormat WaveFormat => Format;
        public bool Ended => _ended;

        public Layer(string path, bool loop, bool ambience)
        {
            _loop = loop;
            _ambience = ambience;
            _reader = AudioOutput.Open(path, out var s);
            if (s.WaveFormat.Channels == 1) s = new MonoToStereoSampleProvider(s);
            if (s.WaveFormat.SampleRate != Format.SampleRate) s = new WdlResamplingSampleProvider(s, Format.SampleRate);
            _source = s;
        }

        public void FadeTo(float volume, int ms, Action? then = null)
        {
            _target = MathF.Sqrt(Math.Clamp(volume, 0f, 1f));   // the transition is based on “loudness by ear”, the amplitude is its square
            int samples = Math.Max(1, ms * Format.SampleRate / 1000 * Format.Channels);
            _step = (_target - _gain) / samples;
            _then = then;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            if (_ended) return 0;
            int read = 0;
            bool rewound = false;
            while (read < count)
            {
                int n = _source.Read(buffer, offset + read, count - read);
                if (n == 0)
                {
                    if (!_loop || rewound) break;   // finished playing / empty file - do not spin
                    _reader.Position = 0;           // in a circle
                    rewound = true;
                    continue;
                }
                rewound = false;
                read += n;
            }
            float master = (_ambience ? AmbienceEnabled : Enabled) ? Volume * Sound.Volume : 0f;
            for (int i = 0; i < read; i++)
            {
                if (_step != 0)
                {
                    _gain += _step;
                    if ((_step > 0 && _gain >= _target) || (_step < 0 && _gain <= _target)) { _gain = _target; _step = 0; Finish(); }
                }
                buffer[offset + i] *= _gain * _gain * master;   // square - the rise from silence is smooth, without a sharp start
            }
            if (read < count)
            {
                _ended = true;   // finished playing: return less count - the mixer removes the track itself
                Finish();
            }
            return read;
        }

        private void Finish()
        {
            var then = _then;
            _then = null;
            if (then != null) Task.Run(then);   // not from the audio stream: RemoveMixerInput takes lock of the mixer
        }

        public void Dispose() => _reader.Dispose();
    }
}
