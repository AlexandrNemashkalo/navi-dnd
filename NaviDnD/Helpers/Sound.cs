using NAudio.Wave;

namespace NaviDnD.Helpers;

internal static class Sound
{
    // Выставляется один раз из AppConfig.SoundEnabled при старте игры (Program.cs).
    public static bool Enabled { get; set; } = true;

    // Общая громкость 0..1 (AppConfig.SoundVolume, экран «НАСТРОЙКИ») — множитель для всех звуков.
    public static float Volume { get; set; } = 1f;

    private static readonly OneShotSound Click = new("click.wav");

    public static void PlayClick()
    {
        if (Enabled) Click.Trigger();
    }

    // Выставляется из AppConfig.TypingSoundEnabled (Program.cs).
    public static bool TypingEnabled { get; set; } = true;

    private static readonly TypingBlipProvider? Typing = TypingBlipProvider.Create();
    private const int TypingMinIntervalMs = 55;
    private static DateTime _lastTypingBlip = DateTime.MinValue;

    // Звук печати текста в диалоге (как в RPG): блип только на буквах/цифрах и не чаще раза в
    // TypingMinIntervalMs — на каждый символ при ~40 симв/с получается трещотка, а не «голос».
    public static void PlayTyping(char c)
    {
        if (!Enabled || !TypingEnabled || !char.IsLetterOrDigit(c)) return;
        var now = DateTime.UtcNow;
        if ((now - _lastTypingBlip).TotalMilliseconds < TypingMinIntervalMs) return;
        _lastTypingBlip = now;
        Typing?.Trigger();
    }

    private static readonly string DiceRollPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sound", "dice-roll.mp3");

    // Без предзагрузки в память — играем mp3 напрямую с диска через свежий Mp3FileReader на каждый
    // бросок. Предзагрузка (чтение всего файла в плоский массив заранее) стабильно портила именно
    // этот сжатый клип; бросок редкий и не такой чувствительный к задержке, как клик, так что
    // открытие устройства каждый раз — приемлемая цена за корректный звук.
    public static Task PlayDiceRoll()
    {
        if (!Enabled || !File.Exists(DiceRollPath)) return Task.CompletedTask;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task.Run(() =>
        {
            try
            {
                using var reader = new Mp3FileReader(DiceRollPath);
                using var output = new WaveOutEvent();
                output.Init(new NAudio.Wave.SampleProviders.VolumeSampleProvider(reader.ToSampleProvider()) { Volume = Volume });
                output.Play();
                started.TrySetResult();
                while (output.PlaybackState == PlaybackState.Playing)
                    Thread.Sleep(50);
            }
            catch { /* звук — не критичная функция */ }
            finally { started.TrySetResult(); }
        });
        return started.Task;
    }

    // Устройство открывается ОДИН раз на старте игры и играет непрерывно (тишину, когда звук не
    // запущен) — иначе каждый Play() заново открывает аудио-устройство, что даёт заметную задержку.
    // Клип декодируется в плоский массив семплов заранее, целиком, один раз — для клика (честный PCM
    // wav) это надёжно работает.
    private sealed class OneShotSound
    {
        private readonly OneShotSampleProvider? _provider;
        private readonly IWavePlayer? _output;

        public OneShotSound(string fileName)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sound", fileName);
                if (!File.Exists(path)) return;

                using var reader = new AudioFileReader(path);
                var samples = new List<float>();
                var buffer = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels];
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                    samples.AddRange(buffer.Take(read));

                _provider = new OneShotSampleProvider(samples.ToArray(), reader.WaveFormat);
                _output = new WaveOutEvent { DesiredLatency = 40 };
                _output.Init(_provider);
                _output.Play();
            }
            catch { /* звук — не критичная функция */ }
        }

        public void Trigger() => _provider?.Trigger();
    }

    // Синтезированный звук печати (без файла): тихий затухающий чистый синус — ноутбучные динамики
    // хрипят на низах и обертонах, поэтому без них. Нота каждый раз случайная из ре-минора (D4–C5),
    // поэтому текст звучит как тихий минорный перебор, а не метроном.
    // Устройство, как у клика, открыто постоянно и играет тишину между звуками.
    private sealed class TypingBlipProvider : ISampleProvider
    {
        private const int SampleRate = 44100;
        private const double DurationSec = 0.08;
        private const double DecaySec = 0.025;
        private const float Volume = 0.04f;
        private static readonly double[] Notes = [293.66, 349.23, 392.00, 440.00, 523.25]; // D4 F4 G4 A4 C5

        private readonly object _lock = new();
        private readonly Random _rnd = new();
        private int _pos = -1; // -1 = тишина
        private double _freq = Notes[0];

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);

        public static TypingBlipProvider? Create()
        {
            try
            {
                var provider = new TypingBlipProvider();
                var output = new WaveOutEvent { DesiredLatency = 40 };
                output.Init(provider);
                output.Play();
                return provider;
            }
            catch { return null; /* звук — не критичная функция */ }
        }

        public void Trigger()
        {
            lock (_lock)
            {
                _freq = Notes[_rnd.Next(Notes.Length)];
                _pos = 0;
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int length = (int)(SampleRate * DurationSec);
            lock (_lock)
            {
                for (int i = 0; i < count; i++)
                {
                    if (_pos < 0 || _pos >= length)
                    {
                        buffer[offset + i] = 0f;
                        _pos = -1;
                        continue;
                    }
                    double t = (double)_pos / SampleRate;
                    double attack = Math.Min(1.0, t / 0.005);
                    double fadeOut = Math.Min(1.0, (length - _pos) / (SampleRate * 0.01)); // без щелчка в конце
                    buffer[offset + i] = (float)(Math.Sin(2 * Math.PI * _freq * t) * attack * Math.Exp(-t / DecaySec) * fadeOut * Volume * Sound.Volume);
                    _pos++;
                }
            }
            return count;
        }
    }

    private sealed class OneShotSampleProvider(float[] clip, WaveFormat format) : ISampleProvider
    {
        private int _pos = -1; // -1 = тишина
        private readonly object _lock = new();

        public WaveFormat WaveFormat { get; } = format;

        public void Trigger() { lock (_lock) _pos = 0; }

        public int Read(float[] buffer, int offset, int count)
        {
            lock (_lock)
            {
                for (int i = 0; i < count; i++)
                {
                    if (_pos >= 0 && _pos < clip.Length)
                        buffer[offset + i] = clip[_pos++] * Volume;
                    else
                    {
                        buffer[offset + i] = 0f;
                        _pos = -1;
                    }
                }
            }
            return count;
        }
    }
}
