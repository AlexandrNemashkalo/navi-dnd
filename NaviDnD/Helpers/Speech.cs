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
    private static string status = "Выключена";
    private static int started;
    public static string Status => Volatile.Read(ref status);

    public static void Initialize(AppConfig settings)
    {
        config = settings;
        if (Interlocked.Exchange(ref started, 1) == 0) _ = Task.Run(ConsumeAsync);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { Lifetime.Cancel(); Stop(); SileroSpeech.Close(); };
        status = settings.SpeechEnabled ? "Готова к запуску" : "Выключена";
        WarmUp();
    }

    public static void WarmUp()
    {
        if (config?.SpeechEnabled == true)
        {
            Queue.Writer.TryWrite(("Добро пожаловать.", null, config.SileroVoice, true, Lifetime.Token));
            Queue.Writer.TryWrite(("Добро пожаловать.", null, "baya", true, Lifetime.Token));
        }
    }

    public static void Stop()
    {
        lock (Gate)
        {
            session.Cancel();
            session.Dispose();
            session = new CancellationTokenSource();
        }
    }

    public static void Speak(string text, string? author = null, string? speechText = null)
    {
        if (config?.SpeechEnabled != true || author is "Hero" or "Герой" or "System" or "system") return;
        text = PrepareText(text);
        if (text.Length == 0) return;
        string? ssml = ValidateSsml(speechText);
        string voice = SelectVoice(author, ssml, config.SileroVoice);
        ssml = ApplyVoiceStyle(text, ssml, author);
        lock (Gate) Queue.Writer.TryWrite((text, ssml, voice, false, session.Token));
    }

    private static bool IsNarrator(string? author) => string.IsNullOrWhiteSpace(author)
        || author.Equals("DM", StringComparison.OrdinalIgnoreCase) || author is "Мастер" or "Рассказчик";

    internal static string SelectVoice(string? author, string? ssml, string narratorVoice)
    {
        if (IsNarrator(author)) return narratorVoice;
        string? kind = ssml == null ? null : XElement.Parse(ssml).Attribute("voice")?.Value;
        return kind == "female" ? "baya" : "eugene";
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
            if (item.Token.IsCancellationRequested || config?.SpeechEnabled != true) continue;
            string wav = Path.Combine(Path.GetTempPath(), $"NaviDnD-speech-{Guid.NewGuid():N}.wav");
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(item.Token);
                timeout.CancelAfter(TimeSpan.FromMinutes(15)); // первый запуск Silero устанавливает CPU PyTorch
                var token = timeout.Token;
                await SileroSpeech.SynthesizeAsync(item.Text, item.Ssml, item.Voice, wav, config!.SileroPythonPath,
                    value => status = value, token);
                if (item.Warmup) { status = "Готова"; continue; }
                using var reader = new WaveFileReader(wav);
                using var output = new WaveOutEvent();
                // Громкость — в самом потоке: WaveOutEvent.Volume (waveOutSetVolume) меняет громкость всего приложения,
                // и музыка оставалась на громкости речи.
                var volume = new VolumeSampleProvider(reader.ToSampleProvider()) { Volume = Math.Clamp(config!.SpeechVolume / 100f, 0, 1) };
                output.Init(volume);
                Music.Ducked = true;
                try
                {
                    output.Play();
                    status = "Озвучивание";
                    while (output.PlaybackState == PlaybackState.Playing)
                    {
                        await Task.Delay(30, token);
                        volume.Volume = Math.Clamp(config.SpeechVolume / 100f, 0, 1);
                    }
                }
                finally { Music.Ducked = false; }
                status = "Готова";
            }
            catch (OperationCanceledException) { status = "Остановлена"; }
            catch (Exception ex)
            {
                status = "Ошибка: " + ex.Message;
                // Текст реплики и секреты в диагностический лог не записываются.
                try { File.AppendAllText(Path.Combine(AppConfig.ProjectRoot, "logs", "speech.log"), $"{DateTime.Now:s} {ex.GetType().Name}: {ex.Message}\n"); } catch { }
            }
            finally { try { File.Delete(wav); } catch { } }
        }
    }

}
