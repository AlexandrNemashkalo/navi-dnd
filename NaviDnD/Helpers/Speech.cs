using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Xml;
using System.Xml.Linq;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NaviDnD.Helpers;

// Единственный фоновый потребитель: реплики не перекрываются и не задерживают игровой поток.
internal static class Speech
{
    private static readonly Channel<(string Text, string? Ssml, string Voice, bool Warmup, CancellationToken Token)> Queue =
        Channel.CreateBounded<(string, string?, string, bool, CancellationToken)>(new BoundedChannelOptions(32)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private static readonly object Gate = new();
    private static CancellationTokenSource session = new();
    private static readonly CancellationTokenSource Lifetime = new();
    private static AppConfig? config;
    private static string status = L.T("Выключена");
    private static int started;
    private static volatile bool _windowMinimized;
    private static IWavePlayer? _activeOutput;
    private static long _messageId;
    private static (long Id, string Text, string? Author, string? Ssml)? _printingMessage;

    public static IDisposable BeginMessage(string text, string? author, string? speechText)
    {
        lock (Gate)
        {
            long id = ++_messageId;
            _printingMessage = (id, text, author, speechText);
            Speak(text, author, speechText);
            return new MessageScope(id);
        }
    }

    private sealed class MessageScope(long id) : IDisposable
    {
        public void Dispose()
        {
            lock (Gate)
                if (_printingMessage?.Id == id) _printingMessage = null;
        }
    }

    public static void SetWindowMinimized(bool minimized)
    {
        lock (Gate)
        {
            if (_windowMinimized == minimized) return;
            _windowMinimized = minimized;
            if (minimized) CancelSession();
            else if (_printingMessage is { } message)
                Speak(message.Text, message.Author, message.Ssml);
        }
    }
    public static string Status => Volatile.Read(ref status);

    public static void Initialize(AppConfig settings)
    {
        config = settings;
        if (Interlocked.Exchange(ref started, 1) == 0) _ = Task.Run(ConsumeAsync);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { Lifetime.Cancel(); Stop(); SileroSpeech.Close(); };
        status = settings.SpeechEnabled ? L.T("Готова к запуску") : L.T("Выключена");
        WarmUp();
    }

    // Модели — в память заранее, даже при выключенной озвучке: включение и пробные фразы в настройках — без
    // задержки. Язык игры (и интерфейса); both — оба языка (экран настроек). Ничего не устанавливает и не скачивает.
    public static void WarmUp(bool both = false)
    {
        // В UI-тестах (своё состояние, озвучка выключена) модели не грузим — тесты быстрее и без лишних процессов.
        if (config == null || !SileroSpeech.Installed || Environment.GetEnvironmentVariable("NAVIDND_TEST_WORLDSTATE") != null) return;
        bool english = both || L.WorldIsEnglish || L.IsEnglish, russian = both || !L.WorldIsEnglish || !L.IsEnglish;
        if (russian)
        {
            Queue.Writer.TryWrite(("Добро пожаловать.", null, config.SileroVoice, true, Lifetime.Token));
            if (config.SileroVoice != "baya")
                Queue.Writer.TryWrite(("Добро пожаловать.", null, "baya", true, Lifetime.Token));
        }
        if (english)
            Queue.Writer.TryWrite(("Welcome.", null, config.SileroVoiceEn, true, Lifetime.Token));
    }

    public static void Stop()
    {
        lock (Gate)
        {
            _printingMessage = null;
            CancelSession();
        }
    }

    private static void CancelSession()
    {
        session.Cancel();
        _activeOutput?.Stop();
        session.Dispose();
        session = new CancellationTokenSource();
    }

    // Английские голоса модели v3_en (по высоте тона: en_57/en_31/en_20 — мужские, en_21/en_11 — женские).
    public static readonly string[] EnglishVoices = ["en_57", "en_31", "en_20", "en_21", "en_11"];
    private const string EnglishNpcMale = "en_23", EnglishNpcFemale = "en_6";

    public static void Speak(string text, string? author = null, string? speechText = null)
    {
        if (config?.SpeechEnabled != true || author is "Hero" or "Герой" or "System" or "system") return;
        text = PrepareText(text);
        if (text.Length == 0) return;
        string? ssml = ValidateSsml(speechText);
        // Голос — языка игры (рассказ мастера на нём): русская или английская модель.
        string voice = SelectVoice(author, ssml, L.WorldIsEnglish ? config.SileroVoiceEn : config.SileroVoice, L.WorldIsEnglish);
        ssml = ApplyVoiceStyle(text, ssml, author);
        lock (Gate)
            if (!_windowMinimized) Queue.Writer.TryWrite((text, ssml, voice, false, session.Token));
    }

    // Пробная фраза выбранным голосом (настройки) — независимо от языка игры.
    public static void Sample(string text, string voice)
    {
        if (config?.SpeechEnabled != true) return;
        lock (Gate)
            if (!_windowMinimized) Queue.Writer.TryWrite((text, ApplyVoiceStyle(text, null, null), voice, false, session.Token));
    }

    private static bool IsNarrator(string? author) => string.IsNullOrWhiteSpace(author)
        || author.Equals("DM", StringComparison.OrdinalIgnoreCase) || author is "Мастер" or "Рассказчик" or "Narrator";

    internal static string SelectVoice(string? author, string? ssml, string narratorVoice, bool english = false)
    {
        if (IsNarrator(author)) return narratorVoice;
        string? kind = ssml == null ? null : XElement.Parse(ssml).Attribute("voice")?.Value;
        return english ? kind == "female" ? EnglishNpcFemale : EnglishNpcMale : kind == "female" ? "baya" : "eugene";
    }

    internal static string ApplyVoiceStyle(string text, string? ssml, string? author)
    {
        var root = ssml == null ? new XElement("speak", text) : XElement.Parse(ssml);
        root.Attribute("voice")?.Remove(); // служебный выбор голоса обрабатывается приложением
        if (IsNarrator(author))
        {
            foreach (var prosody in root.Descendants("prosody")) prosody.Attribute("pitch")?.Remove();
            var nodes = root.Nodes().ToList();
            root.RemoveNodes();
            root.Add(new XElement("prosody", new XAttribute("pitch", "low"), nodes));
        }
        return root.ToString(SaveOptions.DisableFormatting);
    }

    internal static string? ValidateSsml(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 16000) return null;
        try
        {
            using var reader = XmlReader.Create(new StringReader(value), new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16000 });
            var root = XElement.Load(reader);
            if (root.Name != "speak") return null;
            foreach (var element in root.DescendantsAndSelf())
            {
                if (element.Name.Namespace != XNamespace.None || element.Name.LocalName is not ("speak" or "p" or "s" or "break" or "prosody")) return null;
                foreach (var attribute in element.Attributes())
                {
                    bool allowed = (element.Name.LocalName, attribute.Name.LocalName) switch
                    {
                        ("speak", "voice") => attribute.Value is "male" or "female",
                        ("prosody", "rate") => attribute.Value is "x-slow" or "slow" or "medium" or "fast" or "x-fast",
                        ("prosody", "pitch") => attribute.Value is "x-low" or "low" or "medium" or "high" or "x-high",
                        ("break", "strength") => attribute.Value is "x-weak" or "weak" or "medium" or "strong" or "x-strong",
                        ("break", "time") => Regex.IsMatch(attribute.Value, @"^(?:[0-9]{1,3}|1[0-9]{3}|2000)ms$"),
                        _ => false
                    };
                    if (attribute.Name.Namespace != XNamespace.None || !allowed) return null;
                }
            }
            foreach (var node in root.DescendantNodes().OfType<XText>().ToList())
                node.Value = Regex.Replace(node.Value, @"\[[^\]]*\]", " ");
            return root.ToString(SaveOptions.DisableFormatting);
        }
        catch (XmlException) { return null; }
    }

    internal static string DisplayText(string text)
    {
        if (!text.TrimStart().StartsWith("<speak", StringComparison.OrdinalIgnoreCase)) return text;
        // Сохраняем механические пометки на экране, но убираем теги и маркеры произношения.
        string plain = Regex.Replace(text, @"<(?:break\b[^>]*|/(?:p|s))>", " ", RegexOptions.IgnoreCase);
        plain = Regex.Replace(plain, "<[^>]*>", "");
        plain = System.Net.WebUtility.HtmlDecode(plain);
        plain = Regex.Replace(plain, @"\+(?=[аеёиоуыэюяАЕЁИОУЫЭЮЯ])", "");
        return plain.Replace("*", "");
    }

    internal static string PrepareText(string text)
    {
        text = Regex.Replace(text, @"\[[^\]]*\]", " "); // механические пометки не читаются
        text = Regex.Replace(text, @"[*#`_]", "");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static async Task ConsumeAsync()
    {
        await foreach (var item in Queue.Reader.ReadAllAsync())
        {
            // Заранее загрузить модель (Warmup) — и при выключенной озвучке; реплики — только при включённой.
            if (item.Token.IsCancellationRequested || !item.Warmup && config?.SpeechEnabled != true) continue;
            if (item.Warmup && !SileroSpeech.Installed) continue;
            string wav = Path.Combine(Path.GetTempPath(), $"NaviDnD-speech-{Guid.NewGuid():N}.wav");
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(item.Token);
                timeout.CancelAfter(TimeSpan.FromMinutes(15)); // первый запуск Silero устанавливает CPU PyTorch
                var token = timeout.Token;
                await SileroSpeech.SynthesizeAsync(item.Text, item.Ssml, item.Voice, wav, config!.SileroPythonPath,
                    value => { if (!item.Warmup || config.SpeechEnabled) status = value; }, token);
                if (item.Warmup) { if (config!.SpeechEnabled) status = L.T("Готова"); continue; }
                using var reader = new WaveFileReader(wav);
                using var output = new WaveOutEvent();
                // Громкость — в самом потоке: WaveOutEvent.Volume (waveOutSetVolume) меняет громкость всего приложения,
                // и музыка оставалась на громкости речи.
                var volume = new VolumeSampleProvider(reader.ToSampleProvider())
                    { Volume = _windowMinimized ? 0 : Math.Clamp(config!.SpeechVolume / 100f, 0, 1) };
                output.Init(volume);
                Music.Ducked = true;
                try
                {
                    lock (Gate)
                    {
                        token.ThrowIfCancellationRequested();
                        _activeOutput = output;
                        output.Play();
                    }
                    status = L.T("Озвучивание");
                    while (output.PlaybackState == PlaybackState.Playing)
                    {
                        await Task.Delay(30, token);
                        volume.Volume = _windowMinimized ? 0 : Math.Clamp(config.SpeechVolume / 100f, 0, 1);
                    }
                }
                finally
                {
                    lock (Gate)
                        if (ReferenceEquals(_activeOutput, output)) _activeOutput = null;
                    Music.Ducked = false;
                }
                status = L.T("Готова");
            }
            catch (OperationCanceledException) { status = L.T("Остановлена"); }
            catch (Exception ex)
            {
                status = L.T("Ошибка: ") + ex.Message;
                // Текст реплики и секреты в диагностический лог не записываются.
                try { File.AppendAllText(Path.Combine(AppConfig.ProjectRoot, "logs", "speech.log"), $"{DateTime.Now:s} {ex.GetType().Name}: {ex.Message}\n"); } catch { }
            }
            finally { try { File.Delete(wav); } catch { } }
        }
    }

}
