using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NaviDnD.Helpers;

// Музыка и звуки окружения игры. Одно устройство вывода и микшер — дорожки звучат слоями: у каждой своя громкость и
// плавное появление/затухание (fade). Что играет — решает MusicDirector (по месту героя, бою, времени суток):
//  - смена музыки: Play(новая) + Stop(старая) — перекрёстное затухание;
//  - несколько дорожек вместе (музыка + звуки окружения, позже — слои боя): Play с разными id, громкость — SetLayerVolume.
// Громкость: слой × «ГРОМКОСТЬ МУЗЫКИ» × общая «ГРОМКОСТЬ» (Sound.Volume); «МУЗЫКА: ВЫКЛ» / «ЗВУКИ ОКРУЖЕНИЯ: ВЫКЛ» —
// тишина без остановки дорожек. Форматы: wav, mp3, ogg.
internal static class Music
{
    public static string SoundDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sound");

    public static bool Enabled { get; set; } = true;
    public static bool AmbienceEnabled { get; set; } = true;
    public static float Volume { get; set; } = 0.4f;   // «ГРОМКОСТЬ МУЗЫКИ», 0..1
    // Звучит речь мастера (Speech): музыка и окружение плавно притихают до DuckLevel, после — возвращаются.
    public static volatile bool Ducked;
    private const float DuckLevel = 0.3f, DuckDownSec = 0.4f, DuckUpSec = 1.5f;

    private static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
    private static readonly object _lock = new();
    private static WaveOutEvent? _output;
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

    // Включить дорожку id (путь от папки sound) с плавным появлением; уже играет — только громкость.
    // loop — по кругу (звуки окружения, бой); иначе дорожка доигрывает и затихает сама (IsPlaying → false).
    // ambience — звук окружения: глушится своей настройкой, а не «МУЗЫКА».
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
            catch { /* файл не читается — без него */ }
        }
    }

    // Дорожка ещё звучит (не доиграла и не остановлена).
    public static bool IsPlaying(string id)
    {
        lock (_lock) return _layers.TryGetValue(id, out var layer) && !layer.Ended;
    }

    // Плавно заглушить дорожку и убрать её из микшера.
    public static void Stop(string id, int fadeMs = 2000)
    {
        lock (_lock)
        {
            if (!_layers.Remove(id, out var layer)) return;
            if (layer.Ended) { layer.Dispose(); return; }   // доиграла — микшер её уже убрал
            layer.FadeTo(0, fadeMs, then: () => { lock (_lock) _mixer?.RemoveMixerInput(layer); layer.Dispose(); });
        }
    }

    // Громкость слоя (0..1) с плавным переходом — для смешивания нескольких дорожек.
    public static void SetLayerVolume(string id, float volume, int fadeMs = 1000)
    {
        lock (_lock) if (_layers.TryGetValue(id, out var layer)) layer.FadeTo(volume, fadeMs);
    }

    private static bool EnsureOutput()
    {
        if (_output != null) return true;
        try
        {
            _mixer = new MixingSampleProvider(Format) { ReadFully = true };   // без дорожек — тишина, устройство не закрывается
            _output = new WaveOutEvent { DesiredLatency = 200 };
            _output.Init(new Ducking(_mixer));
            if (!_paused) _output.Play();
            return true;
        }
        catch { _output = null; _mixer = null; return false; }
    }

    // Притихание всего микшера под речь: вниз быстро, обратно — медленно.
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

    // Дорожка: файл (wav/mp3/ogg), приведённый к формату микшера, × громкость слоя (с затуханием) × общая.
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
            ISampleProvider s;
            if (path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)) { var v = new VorbisWaveReader(path); _reader = v; s = v; }
            else { var a = new AudioFileReader(path); _reader = a; s = a; }
            if (s.WaveFormat.Channels == 1) s = new MonoToStereoSampleProvider(s);
            if (s.WaveFormat.SampleRate != Format.SampleRate) s = new WdlResamplingSampleProvider(s, Format.SampleRate);
            _source = s;
        }

        public void FadeTo(float volume, int ms, Action? then = null)
        {
            _target = MathF.Sqrt(Math.Clamp(volume, 0f, 1f));   // переход идёт по «громкости на слух», амплитуда — её квадрат
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
                    if (!_loop || rewound) break;   // доиграла / пустой файл — не крутиться
                    _reader.Position = 0;           // по кругу
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
                buffer[offset + i] *= _gain * _gain * master;   // квадрат — нарастание с тишины плавное, без резкого начала
            }
            if (read < count)
            {
                _ended = true;   // доиграла: вернуть меньше count — микшер убирает дорожку сам
                Finish();
            }
            return read;
        }

        private void Finish()
        {
            var then = _then;
            _then = null;
            if (then != null) Task.Run(then);   // не из потока звука: RemoveMixerInput берёт lock микшера
        }

        public void Dispose() => _reader.Dispose();
    }
}
