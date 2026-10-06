namespace NaviDnD;

public class AppConfig
{
    public static string ProjectRoot { get; } = FindProjectRoot();

    private static string FindProjectRoot()
    {
        if (Directory.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Prompts")))
            return AppDomain.CurrentDomain.BaseDirectory;
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.csproj").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        return AppDomain.CurrentDomain.BaseDirectory;
    }

    public static string AssetDirectory(string name)
    {
        string bundled = Path.Combine(ProjectRoot, name);
        return Directory.Exists(bundled) ? bundled : Path.GetFullPath(Path.Combine(ProjectRoot, "..", name));
    }

    public string RuleSet { get; init; } = "Dnd5e";

    // Claude CLI
    public string ClaudeOAuthToken { get; set; } = "";
    public string ClaudeCliPath { get; set; } = "claude.exe";
    public string ClaudeModel { get; set; } = "claude-opus-5-5"; // "claude-sonnet-5"; //"claude-haiku-4-5-20251001"; //"claude-sonnet-4-6";
    public string ClaudeCreateNewGameModel { get; set; } = "claude-opus-5-5";
    public string ClaudeStartNewGameModel { get; set; } = "claude-opus-5-5";

    // Нейронка: "claude" — Claude CLI (по умолчанию), "codex" — OpenAI Codex CLI (CodexCliAiProvider).
    public string AiProvider { get; set; } = "claude";
    // Путь к Codex CLI: «codex» — найти в PATH (из npm-пакета берётся сам codex.exe), иначе — полный путь.
    public string CodexCliPath { get; set; } = "codex";
    // Модель Codex для всех запросов игры; пусто — модель Codex по умолчанию.
    public string CodexModel { get; set; } = "gpt-6.1-sol";
    public string CodexCreateNewGameModel { get; set; } = "gpt-6.1-sol";
    public string CodexStartNewGameModel { get; set; } = "gpt-6.1-sol";
    // Отдельно от настроек Codex для разработки: medium — баланс скорости и правил игры.
    public string CodexReasoningEffort { get; set; } = "medium";

    public string McpServerPath { get; init; } = FindMcpServer();

    private static string FindMcpServer()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string bundled = Path.Combine(baseDir, "NaviDnD.McpServer.exe");
        if (File.Exists(bundled)) return bundled;
        string configuration = new DirectoryInfo(baseDir).Parent?.Name ?? "Debug";
        return Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..",
            "NaviDnD.McpServer", "bin", configuration, "net9.0", "NaviDnD.McpServer.exe"));
    }

    public void InitializeStorage()
    {
        foreach (string folder in new[] { "Storage", "Storage/Worlds", "Storage/Heroes", "Storage/Portraits", "logs" })
            Directory.CreateDirectory(Path.Combine(ProjectRoot, folder));
        if (!File.Exists(UserSettingsPath))
        {
            InitializeAiPaths();
            SaveUserSettings();
        }
    }

    public void InitializeAiPaths()
    {
        string? claude = Helpers.AiCliDiscovery.Find("claude");
        string? codex = Helpers.AiCliDiscovery.Find("codex");
        if (claude != null) ClaudeCliPath = claude;
        if (codex != null) CodexCliPath = codex;
        if (claude == null && codex != null) AiProvider = "codex";
    }

    // Debug
    public bool DisableTriggers { get; set; } = false;
    public bool RevealMap { get; set; } = false;
    // Ошибки ИИ (кривой ответ, сбой вызова) подробно в диалоге; выключено — короткое «повтори действие».
    public bool ShowAiErrors { get; set; } = false;
    // Модель для починки сломанного JSON ответа мастера (короткий вызов без инструментов).
    public string RepairJsonModel { get; set; } = "claude-haiku-4-5-20251001";

    // Сохранять ли исследованные клетки (туман войны) между запусками игры. По умолчанию — сохранять.
    public bool SaveExploredCells { get; set; } = true;
    // Подсказки клавиш «[F1]», «[Esc]» в заголовках и подвкладках; выключено — на их месте пусто.
    public bool ShowKeyHints { get; set; } = true;
    // Весь экран: окно игры того же размера по центру монитора, вокруг — фон игры (Helpers/FullscreenBackdrop).
    public bool Fullscreen { get; set; } = true;
    // Шрифт консоли (настройка «ТИП ШРИФТА»).
    public string FontFace { get; set; } = "Consolas";
    // Размер шрифта во весь экран: 0 — авто (самый крупный, при котором игра влезает в монитор), иначе размер.
    public int FullscreenFontSize { get; set; } = 0;
    // Звук клика по кнопкам/стрелкам (Helpers/Sound.cs).
    public bool SoundEnabled { get; set; } = true;
    // Звук печати текста ИИ в диалоге (синтезируется в Sound.PlayTyping, работает только при SoundEnabled).
    public bool TypingSoundEnabled { get; set; } = true;
    // Фоновая музыка (Helpers/Music) и её громкость 0–100 % (умножается на общую громкость).
    public bool MusicEnabled { get; set; } = true;
    public int MusicVolume { get; set; } = 30;
    public bool AmbienceEnabled { get; set; } = true;   // звуки окружения (птицы, камин, капли)
    // Общая громкость звуков, 0–100 % (Sound.Volume).
    public int SoundVolume { get; set; } = 20;
    // [] = off, ["*"] = all actions, or any subset of:
    // "CreateNewGame", "FixHeroForNewGame", "StartNewGame", "SendAction"
    // Через env var NAVIDND_TEST_MOCKED_ACTIONS (запятая-разделённый список) — для E2E-тестов,
    // чтобы не бить в реальную нейронку (см. NaviDnD.UiTests/GameSession.cs).
    public string[] MockedActions { get; init; } = ParseMockedActions();

    // Переопределяет базовую папку, откуда SelectiveMockProvider берёт response.json —
    // по умолчанию это Prompts/{RuleSet}/{Action}/, что затронуло бы боевые файлы в репозитории.
    public string? MockResponsesDir { get; init; } = Environment.GetEnvironmentVariable("NAVIDND_TEST_MOCK_RESPONSES_DIR")
        ?? (TestMocksSet ? null : DevMocksDir);

    // Отладка при обычном запуске (без env var тестов): эти действия не зовут нейронку, а отвечают из
    // Storage/DevMocks/{Action}/response.json. CreateNewGame — готовый герой, чтобы тестировать генерацию
    // карты/старт игры без создания персонажа каждый раз. [] — выключить.
    public static readonly string[] DevMockedActions = [];
    public static string DevMocksDir => Path.Combine(ProjectRoot, "Storage", "DevMocks");

    // ── Настройки игрока (экран «НАСТРОЙКИ») ──────────────────────────────────
    // Сохраняются в Storage/settings.json (в .gitignore — там токен) поверх значений по умолчанию выше.
    public static string UserSettingsPath => Path.Combine(ProjectRoot, "Storage", "settings.json");

    private sealed class UserSettings
    {
        public bool? SoundEnabled { get; set; }
        public bool? TypingSoundEnabled { get; set; }
        public int? SoundVolume { get; set; }
        public bool? MusicEnabled { get; set; }
        public int? MusicVolume { get; set; }
        public bool? AmbienceEnabled { get; set; }
        public string? ClaudeModel { get; set; }
        public string? ClaudeCreateNewGameModel { get; set; }
        public string? ClaudeStartNewGameModel { get; set; }
        public string? ClaudeOAuthToken { get; set; }
        public string? ClaudeCliPath { get; set; }
        public string? AiProvider { get; set; }
        public string? CodexCliPath { get; set; }
        public string? CodexModel { get; set; }
        public string? CodexCreateNewGameModel { get; set; }
        public string? CodexStartNewGameModel { get; set; }
        public string? CodexReasoningEffort { get; set; }
        public bool? SaveExploredCells { get; set; }
        public bool? RevealMap { get; set; }
        public bool? ShowAiErrors { get; set; }
        public bool? DisableTriggers { get; set; }
        public bool? ShowKeyHints { get; set; }
        public bool? Fullscreen { get; set; }
        public int? FullscreenFontSize { get; set; }
        public string? FontFace { get; set; }
    }

    public void LoadUserSettings()
    {
        try
        {
            if (!File.Exists(UserSettingsPath)) return;
            var s = System.Text.Json.JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(UserSettingsPath));
            if (s == null) return;
            SoundEnabled = s.SoundEnabled ?? SoundEnabled;
            TypingSoundEnabled = s.TypingSoundEnabled ?? TypingSoundEnabled;
            SoundVolume = Math.Clamp(s.SoundVolume ?? SoundVolume, 0, 100);
            MusicEnabled = s.MusicEnabled ?? MusicEnabled;
            MusicVolume = Math.Clamp(s.MusicVolume ?? MusicVolume, 0, 100);
            AmbienceEnabled = s.AmbienceEnabled ?? AmbienceEnabled;
            ClaudeModel = s.ClaudeModel ?? ClaudeModel;
            ClaudeCreateNewGameModel = s.ClaudeCreateNewGameModel ?? ClaudeCreateNewGameModel;
            ClaudeStartNewGameModel = s.ClaudeStartNewGameModel ?? ClaudeStartNewGameModel;
            ClaudeOAuthToken = s.ClaudeOAuthToken ?? ClaudeOAuthToken;
            ClaudeCliPath = s.ClaudeCliPath ?? ClaudeCliPath;
            AiProvider = s.AiProvider ?? AiProvider;
            CodexCliPath = s.CodexCliPath ?? CodexCliPath;
            CodexModel = s.CodexModel ?? CodexModel;
            CodexCreateNewGameModel = s.CodexCreateNewGameModel ?? CodexModel;
            CodexStartNewGameModel = s.CodexStartNewGameModel ?? CodexModel;
            CodexReasoningEffort = s.CodexReasoningEffort is "low" or "medium" or "high"
                ? s.CodexReasoningEffort : CodexReasoningEffort;
            SaveExploredCells = s.SaveExploredCells ?? SaveExploredCells;
            RevealMap = s.RevealMap ?? RevealMap;
            ShowAiErrors = s.ShowAiErrors ?? ShowAiErrors;
            DisableTriggers = s.DisableTriggers ?? DisableTriggers;
            ShowKeyHints = s.ShowKeyHints ?? ShowKeyHints;
            Fullscreen = s.Fullscreen ?? Fullscreen;
            FullscreenFontSize = s.FullscreenFontSize ?? FullscreenFontSize;
            FontFace = s.FontFace ?? FontFace;
        }
        catch { /* битый файл настроек — остаются значения по умолчанию */ }
    }

    public void SaveUserSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UserSettingsPath)!);
            var s = new UserSettings
            {
                SoundEnabled = SoundEnabled, TypingSoundEnabled = TypingSoundEnabled, SoundVolume = SoundVolume,
                MusicEnabled = MusicEnabled, MusicVolume = MusicVolume, AmbienceEnabled = AmbienceEnabled,
                ClaudeModel = ClaudeModel, ClaudeCreateNewGameModel = ClaudeCreateNewGameModel,
                ClaudeStartNewGameModel = ClaudeStartNewGameModel, ClaudeOAuthToken = ClaudeOAuthToken,
                ClaudeCliPath = ClaudeCliPath, SaveExploredCells = SaveExploredCells, RevealMap = RevealMap, ShowAiErrors = ShowAiErrors,
                AiProvider = AiProvider, CodexCliPath = CodexCliPath, CodexModel = CodexModel, Fullscreen = Fullscreen,
                CodexReasoningEffort = CodexReasoningEffort,
                CodexCreateNewGameModel = CodexCreateNewGameModel, CodexStartNewGameModel = CodexStartNewGameModel,
                FullscreenFontSize = FullscreenFontSize, FontFace = FontFace,
                DisableTriggers = DisableTriggers, ShowKeyHints = ShowKeyHints,
            };
            File.WriteAllText(UserSettingsPath, System.Text.Json.JsonSerializer.Serialize(s,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* не сохранилось — настройки действуют до конца сеанса */ }
    }

    private static bool TestMocksSet => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NAVIDND_TEST_MOCKED_ACTIONS"));

    private static string[] ParseMockedActions()
    {
        string? v = Environment.GetEnvironmentVariable("NAVIDND_TEST_MOCKED_ACTIONS");
        return string.IsNullOrEmpty(v) ? DevMockedActions : v.Split(',');
    }
}
