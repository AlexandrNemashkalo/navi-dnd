using NaviDnD.Data.Models;

namespace NaviDnD.Helpers;

// Что звучит сейчас: раз в полсекунды смотрит на игру (экран, место героя, бой, время суток, путешествие) и выбирает
// настроение музыки и звук окружения. Музыка — папка sound/music/<настроение> (любые wav/mp3/ogg: подложить свою
// музыку — положить файлы в папку), окружение — sound/ambience/<вид> по кругу.
//  - треки места играют с паузами (45–120 с одного окружения между ними) и не повторяют только что сыгранный;
//    меню и бой — без пауз;
//  - смена настроения — перекрёстное затухание; ждёт, пока новое продержится пару секунд (шаг через границу блока
//    туда-обратно не дёргает музыку); бой — сразу.
internal static class MusicDirector
{
    // Мастер описывает путешествие (MapScreenLoop.StartTravel) — музыка дороги.
    public static volatile bool Traveling;

    private const float AmbienceVolume = 0.7f;
    private static readonly string[] AudioExt = [".mp3", ".ogg", ".wav"];
    private static readonly Random Rng = new();

    private static Func<(string Mood, string? Ambience)>? _decide;
    private static string? _mood, _ambience, _trackId, _lastTrack, _pendingMood;
    private static DateTime _pendingSince, _nextTrackAt;
    private static int _counter;

    // Запуск (не в UI-тестах): первая музыка разгоняется с тишины.
    public static void Start(GameState state, WorldState ws)
    {
        _decide = () => Decide(state, ws);
        new Thread(Loop) { IsBackground = true, Name = "MusicDirector" }.Start();
    }

    private static void Loop()
    {
        bool first = true;
        while (true)
        {
            try { Tick(first); first = false; }
            catch { /* состояние игры меняется в другом потоке — следующий такт */ }
            Thread.Sleep(500);
        }
    }

    private static void Tick(bool first)
    {
        var (mood, ambience) = _decide!();
        int fade = first ? 8000 : 3000;

        if (ambience != _ambience)
        {
            if (_ambience != null) Music.Stop("amb:" + _ambience, 3000);
            if (ambience != null && FilesOf("ambience", ambience).FirstOrDefault() is { } file)
                Music.Play("amb:" + ambience, file, AmbienceVolume, fade, loop: true, ambience: true);
            _ambience = ambience;
        }

        if (mood != _mood)
        {
            if (mood != _pendingMood) { _pendingMood = mood; _pendingSince = DateTime.Now; }
            if (_mood == null || mood == "combat" || _mood == "combat" || DateTime.Now - _pendingSince > TimeSpan.FromSeconds(2.5))
            {
                _mood = mood;
                StartTrack(fade);
            }
            return;
        }
        _pendingMood = null;

        if (_trackId != null && !Music.IsPlaying(_trackId))
        {
            _trackId = null;
            _nextTrackAt = Continuous(_mood) ? DateTime.Now : DateTime.Now.AddSeconds(Rng.Next(45, 121));
        }
        if (_trackId == null && DateTime.Now >= _nextTrackAt) StartTrack(Continuous(_mood) ? 1000 : 4000);
    }

    private static bool Continuous(string? mood) => mood is "menu" or "combat";

    // Новый трек настроения (старый — затухает поверх).
    private static void StartTrack(int fadeMs)
    {
        if (_trackId != null) Music.Stop(_trackId, 3000);
        _trackId = null;
        var files = FilesOf("music", _mood!);
        if (files.Count == 0) return;
        var pool = files.Count > 1 ? files.Where(f => f != _lastTrack).ToList() : files;
        string file = pool[Rng.Next(pool.Count)];
        _lastTrack = file;
        _trackId = "music:" + ++_counter;
        Music.Play(_trackId, file, 1f, fadeMs, loop: false);
    }

    private static List<string> FilesOf(string kind, string name)
    {
        string dir = Path.Combine(Music.SoundDir, kind, name);
        return Directory.Exists(dir)
            ? Directory.GetFiles(dir).Where(f => AudioExt.Contains(Path.GetExtension(f).ToLowerInvariant())).Order().ToList()
            : [];
    }

    // Настроение и окружение по состоянию игры. Меню, создание игры, настройки — главная тема.
    private static (string Mood, string? Ambience) Decide(GameState state, WorldState ws)
    {
        bool inGame = state.CurrentScreen is Screen.Map or Screen.World or Screen.Journal or Screen.Character or Screen.Abilities;
        if (!inGame) return ("menu", null);

        bool night = ws.Time.PartOfDay == "Ночь";
        bool combat = ws.Combat?.Active == true;
        (string mood, string? ambience) place;
        if (Traveling || !GameWorld.HasLocation(ws))
            place = (night ? "night" : "wild", night ? "night" : "wind");   // в пути, сцена без тактической карты
        else
        {
            MapChunk? chunk = ws.Hero?.Position is { Count: >= 2 } p ? ws.Map.ChunkAt(p[0], p[1]) : null;
            bool indoor = ws.Hero?.Position is { Count: >= 2 } hp && ws.Map.TerrainAt(hp[0], hp[1]) is { Indoor: true };
            string? theme = chunk?.Theme;
            place = theme switch
            {
                _ when chunk is { Z: < 0 } => ("dungeon", "dungeon"),                         // подвал
                MapChunk.Building when chunk?.Style == "tavern" => ("tavern", "hearth"),
                MapChunk.Building or MapChunk.Village => (night ? "night" : "town", indoor ? null : night ? "night" : "day"),
                "desert" or "snow" or "mountain" => (night ? "night" : "wild", "wind"),
                _ when MapChunk.IsOutdoorTheme(theme) => (night ? "night" : "wild", night ? "night" : "day"),
                _ => ("dungeon", "dungeon"),                                                   // подземелье, пещера, старые карты
            };
        }
        return combat ? ("combat", place.ambience) : place;
    }
}
