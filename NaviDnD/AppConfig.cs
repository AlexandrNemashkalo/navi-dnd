namespace NaviDnD;

public class AppConfig
{
    // The game was launched from NaviDnD.app on the macOS: program - in Contents/MacOS, the rest of the game
    // files - in Contents/Resources. (Declared before ProjectRoot:, static properties are initialized in
    // order.)
    public static bool InMacApp { get; } = OperatingSystem.IsMacOS()
        && Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory).EndsWith(".app/Contents/MacOS", StringComparison.Ordinal);

    public static string ProjectRoot { get; } = FindProjectRoot();

    // Saves, settings and logs (Storage/, logs/) - next to the game; for NaviDnD.app - in ~/Library/Application
    // Support/NaviDnD: the application is replaced entirely and signed, there is no place for your player files
    // inside it.
    public static string DataRoot { get; } = InMacApp
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "NaviDnD")
        : ProjectRoot;

    private static string FindProjectRoot()
    {
        if (InMacApp) return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources")) + Path.DirectorySeparatorChar;
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

    // Interface language: "ru" or "en" (L.SetLanguage). World language - separately, in the world when created.
    // New installation (no settings file yet) - English; settings of the previous version without a field -
    // Russian (LoadUserSettings).
    public string Language { get; set; } = L.English;

    // Claude CLI
    public string ClaudeOAuthToken { get; set; } = "";
    public string ClaudeCliPath { get; set; } = OperatingSystem.IsWindows() ? "claude.exe" : "claude";
    public string ClaudeModel { get; set; } = "claude-opus-5-5"; // "claude-sonnet-5"; //"claude-haiku-4-5-20251001"; //"claude-sonnet-4-6";
    public string ClaudeCreateNewGameModel { get; set; } = "claude-opus-5-5";
    public string ClaudeStartNewGameModel { get; set; } = "claude-opus-5-5";

    // AI: "claude" - Claude CLI (default), "codex" - OpenAI Codex CLI (CodexCliAiProvider).
    public string AiProvider { get; set; } = "claude";
    // The path to Codex CLI: “codex” is found in PATH (codex.exe) itself is taken from the npm- package, otherwise the full path.
    public string CodexCliPath { get; set; } = "codex";
    // Model Codex for all game requests; empty - default model Codex.
    public string CodexModel { get; set; } = "gpt-6.1-sol";
    public string CodexCreateNewGameModel { get; set; } = "gpt-6.1-sol";
    public string CodexStartNewGameModel { get; set; } = "gpt-6.1-sol";
    // Separate from the Codex settings for development: medium - balance of speed and game rules.
    public string CodexReasoningEffort { get; set; } = "medium";

    public string McpServerPath { get; init; } = FindMcpServer();

    // Launcher: NaviDnD.McpServer.exe on Windows, NaviDnD.McpServer (without extension) on macOS/Linux.
    private static string FindMcpServer()
    {
        string executable = OperatingSystem.IsWindows() ? "NaviDnD.McpServer.exe" : "NaviDnD.McpServer";
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string bundled = Path.Combine(baseDir, executable);
        if (File.Exists(bundled)) return bundled;
        string configuration = new DirectoryInfo(baseDir).Parent?.Name ?? "Debug";
        return Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..",
            "NaviDnD.McpServer", "bin", configuration, "net9.0", executable));
    }

    public void InitializeStorage()
    {
        foreach (string folder in new[] { "Storage", "Storage/Worlds", "Storage/Heroes", "Storage/Portraits", "logs" })
            Directory.CreateDirectory(Path.Combine(DataRoot, folder));
        if (!File.Exists(UserSettingsPath))
        {
            SaveUserSettings();
        }
    }

    public Task<(string? claude, string? codex)> DiscoverAiPathsAsync()
    {
        string claude = ClaudeCliPath, codex = CodexCliPath;
        return Task.Run(() => (Helpers.AiCliDiscovery.Resolve("claude", claude), Helpers.AiCliDiscovery.Resolve("codex", codex)));
    }

    public bool ApplyAiPaths(string? claude, string? codex, bool chooseProvider)
    {
        bool changed = false;
        if (claude != null && ClaudeCliPath != claude) { ClaudeCliPath = claude; changed = true; }
        if (codex != null && CodexCliPath != codex) { CodexCliPath = codex; changed = true; }
        if (chooseProvider && claude == null && codex != null && AiProvider != "codex") { AiProvider = "codex"; changed = true; }
        return changed;
    }

    // Debug
    public bool DisableTriggers { get; set; } = false;
    public bool RevealMap { get; set; } = false;
    // AI errors (crooked response, call failure) are detailed in the dialogue; off - short “repeat action”.
    public bool ShowAiErrors { get; set; } = false;
    // Model for fixing broken JSON GM response (short call without tools).
    public string RepairJsonModel { get; set; } = "claude-haiku-4-5-20251001";

    // Whether to save explored cells (fog of war) between starts of the game. The default is to save.
    public bool SaveExploredCells { get; set; } = true;
    // Key hints "[F1]", "[Esc]" in headers and subtabs; turned off - their place is empty.
    public bool ShowKeyHints { get; set; } = true;
    // Whole screen: game window of the same size in the center of the monitor, around the game background (Helpers/FullscreenBackdrop).
    public bool Fullscreen { get; set; } = true;
    // Picture pack of the last new game ("game-icons" / "dcss") - the default of the questionnaire field "PICTURES";
    // the pack of a game itself is in its save (WorldState.IconPack).
    public string IconPack { get; set; } = Helpers.CatalogSearch.GameIconsPack;
    // Console font ("FONT TYPE" setting).
    public string FontFace { get; set; } = "Consolas";
    // Full screen font size: 0 - auto (the largest at which the game fits into the monitor), otherwise size.
    public int FullscreenFontSize { get; set; } = 0;
    // Click sound on buttons/arrows (Helpers/Sound.cs).
    public bool SoundEnabled { get; set; } = true;
    // AI text typing sound in dialogue (synthesized in Sound.PlayTyping, only works with SoundEnabled).
    public bool TypingSoundEnabled { get; set; } = true;
    // Background music (Helpers/Music) and its volume 0–100% (multiplied by the total volume).
    public bool MusicEnabled { get; set; } = true;
    public int MusicVolume { get; set; } = 30;
    public bool AmbienceEnabled { get; set; } = true;   // ambient sounds (birds, fireplace, drops) Overall sound volume, 0–100% (Sound.Volume).
    public int SoundVolume { get; set; } = 20;
    public bool SpeechEnabled { get; set; } = false;
    public int SpeechVolume { get; set; } = 70;
    public string SileroVoice { get; set; } = "baya";
    // Voice of the narrator of the English game (Speech.EnglishVoices; model - Speech/models/v3_en.pt).
    public string SileroVoiceEn { get; set; } = "en_57";
    public string SileroPythonPath { get; set; } = "python";
    // [] = off, ["*"] = all actions, or any subset of: "CreateNewGame", "FixHeroForNewGame", "StartNewGame",
    // "SendAction" Via env var NAVIDND_TEST_MOCKED_ACTIONS (comma-separated list) - for E2E- tests, so as not
    // to hit a real AI (see NaviDnD.UiTests/GameSession.cs).
    public string[] MockedActions { get; init; } = ParseMockedActions();

    // Overrides the base folder where SelectiveMockProvider gets response.json from - the default is
    // Prompts/{RuleSet}/{Action}/, which would affect the battle files in the repository.
    public string? MockResponsesDir { get; init; } = Environment.GetEnvironmentVariable("NAVIDND_TEST_MOCK_RESPONSES_DIR")
        ?? (TestMocksSet ? null : DevMocksDir);

    // Debugging during normal startup (without env var tests): these actions do not call the AI, but respond
    // from Storage/DevMocks/{Action}/response.json. CreateNewGame - a ready-made hero to test map
    // generation/game start without creating a character every time. [] - turn off.
    public static readonly string[] DevMockedActions = [];
    public static string DevMocksDir => Path.Combine(DataRoot, "Storage", "DevMocks");

    // ── Player settings (SETTINGS screen) ────────────────────────────────── Are saved in
    // Storage/settings.json (in .gitignore - there is a token) on top of the default values above.
    public static string UserSettingsPath => Path.Combine(DataRoot, "Storage", "settings.json");

    private sealed class UserSettings
    {
        public bool? SoundEnabled { get; set; }
        public bool? TypingSoundEnabled { get; set; }
        public int? SoundVolume { get; set; }
        public bool? SpeechEnabled { get; set; }
        public int? SpeechVolume { get; set; }
        public string? SileroVoice { get; set; }
        public string? SileroVoiceEn { get; set; }
        public string? SileroPythonPath { get; set; }
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
        public string? IconPack { get; set; }
        public string? Language { get; set; }
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
            SpeechEnabled = s.SpeechEnabled ?? SpeechEnabled;
            SpeechVolume = Math.Clamp(s.SpeechVolume ?? SpeechVolume, 0, 100);
            SileroVoice = s.SileroVoice is "eugene" or "aidar" or "baya" or "kseniya" or "xenia" ? s.SileroVoice : SileroVoice;
            SileroVoiceEn = s.SileroVoiceEn is { } en && Helpers.Speech.EnglishVoices.Contains(en) ? en : SileroVoiceEn;
            SileroPythonPath = s.SileroPythonPath ?? SileroPythonPath;
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
            IconPack = s.IconPack is Helpers.CatalogSearch.DcssPack ? s.IconPack : IconPack;
            // There is no field - settings from the version before choosing the language: the player played in Russian, after the update - too.
            Language = L.Normalize(s.Language ?? L.Russian);
        }
        catch { /*broken settings file - default values remain*/ }
    }

    public void SaveUserSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UserSettingsPath)!);
            var s = new UserSettings
            {
                SoundEnabled = SoundEnabled, TypingSoundEnabled = TypingSoundEnabled, SoundVolume = SoundVolume,
                SpeechEnabled = SpeechEnabled, SpeechVolume = SpeechVolume,
                SileroVoice = SileroVoice, SileroVoiceEn = SileroVoiceEn, SileroPythonPath = SileroPythonPath,
                MusicEnabled = MusicEnabled, MusicVolume = MusicVolume, AmbienceEnabled = AmbienceEnabled,
                ClaudeModel = ClaudeModel, ClaudeCreateNewGameModel = ClaudeCreateNewGameModel,
                ClaudeStartNewGameModel = ClaudeStartNewGameModel, ClaudeOAuthToken = ClaudeOAuthToken,
                ClaudeCliPath = ClaudeCliPath, SaveExploredCells = SaveExploredCells, RevealMap = RevealMap, ShowAiErrors = ShowAiErrors,
                AiProvider = AiProvider, CodexCliPath = CodexCliPath, CodexModel = CodexModel, Fullscreen = Fullscreen,
                CodexReasoningEffort = CodexReasoningEffort,
                CodexCreateNewGameModel = CodexCreateNewGameModel, CodexStartNewGameModel = CodexStartNewGameModel,
                FullscreenFontSize = FullscreenFontSize, FontFace = FontFace, IconPack = IconPack,
                DisableTriggers = DisableTriggers, ShowKeyHints = ShowKeyHints, Language = Language,
            };
            File.WriteAllText(UserSettingsPath, System.Text.Json.JsonSerializer.Serialize(s,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /*not saved - settings are valid until the end of the session*/ }
    }

    private static bool TestMocksSet => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NAVIDND_TEST_MOCKED_ACTIONS"));

    private static string[] ParseMockedActions()
    {
        string? v = Environment.GetEnvironmentVariable("NAVIDND_TEST_MOCKED_ACTIONS");
        return string.IsNullOrEmpty(v) ? DevMockedActions : v.Split(',');
    }
}
