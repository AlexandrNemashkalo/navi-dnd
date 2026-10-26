using NaviDnD.Clients;
using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.Helpers;

namespace NaviDnD.Display;

// Questionnaire for a new game in menu style: on the left are fields (↑↓ - to any field, Enter - change, ←→ -
// scroll through color and portrait), on the right is a live preview of the hero (portrait of him in color,
// name, symbol as on the map). Text fields are filled in successively: Enter saves the field and immediately
// opens the next one (they are also filled in by UI tests). Race and class - leaflets from the list of rules
// (Prompts/{RuleSet}/heroOptions.json): change - a portrait for them, if there is a suitable one. Portraits - a
// ready-made set; under it "SELECT FILE..." (window Windows; PNG/JPG/BMP/GIF/SVG is copied to
// Storage/Portraits).
public class NewGameDisplay
{
    private enum Kind { Text, Color, Portrait, Actions, Button, Choice, AiButton, RandomButton }

    private sealed class Field(string label, Kind kind, int maxLength = 0, string? fixedValue = null)
    {
        public string Label = label;
        public Kind Kind = kind;
        public int MaxLength = maxLength;
        public string Value = fixedValue ?? "";
        public bool ReadOnly = fixedValue != null; // fixed value - focus and input are skipped
        public List<string> Options = [];           // Choice: options (←→), Index selected
        public int Index;
    }



    // Gender: "male"/"female" - only for a hero of this gender (GENDER field), null - for anyone.
    private sealed record Portrait(string Image, string Title, string? Gender = null);
    private const string Male = "male", Female = "female";

    private static (string Name, List<int> Rgb)[] Palette =>
    [
        (L.T("AMBER"), [220, 170, 60]), (L.T("SCARLET"), [210, 70, 60]), (L.T("EMERALD"), [70, 190, 110]),
        (L.T("AZURE"), [80, 150, 230]), (L.T("AMETHYST"), [160, 105, 220]), (L.T("TURQUOISE"), [70, 200, 200]),
        (L.T("COPPER"), [205, 125, 75]), (L.T("SILVER"), [190, 195, 205]), (L.T("GOLD"), [240, 210, 90]),
        (L.T("PINK"), [230, 120, 170]), (L.T("FOREST"), [120, 175, 70]), (L.T("ASHEN"), [155, 145, 135]),
    ];

    // Ready set of portraits (game-icons): faces and figures, not a helmet or a hat instead of a person.
    private static Portrait[] Gallery =>
    [
        new("delapouite/wizard-face", L.T("MAGE"), Male), new("delapouite/woman-elf-face", L.T("ELF WOMAN"), Female),
        new("delapouite/dwarf-face", L.T("DWARF"), Male), new("delapouite/barbarian", L.T("BARBARIAN"), Male),
        new("delapouite/monk-face", L.T("MONK"), Male), new("delapouite/bandit", L.T("BANDIT"), Male),
        new("cathelineau/witch-face", L.T("WITCH"), Female), new("cathelineau/nun-face", L.T("PRIESTESS"), Female),
        new("darkzaitzev/ninja-head", L.T("NINJA")), new("darkzaitzev/hooded-assassin", L.T("ASSASSIN")),
        new("darkzaitzev/hooded-figure", L.T("WANDERER")), new("lorc/cowled", L.T("HERMIT")),
        new("skoll/mounted-knight", L.T("KNIGHT")), new("cathelineau/swordwoman", L.T("SWORDSWOMAN"), Female),
        new("delapouite/viking-head", L.T("VIKING"), Male), new("delapouite/pikeman", L.T("PIKEMAN")),
        new("lorc/bowman", L.T("BOWMAN")), new("delapouite/sun-priest", L.T("SUN PRIEST")),
        new("caro-asercion/philosopher-bust", L.T("SAGE"), Male), new("cathelineau/bad-gnome", L.T("GNOME"), Male),
        new("kier-heyl/dwarf-king", L.T("DWARF KING"), Male),
        new("delapouite/orc-head", L.T("ORC")), new("delapouite/goblin-head", L.T("GOBLIN")),
        new("delapouite/kenku-head", L.T("KENKU")), new("lorc/lizardman", L.T("LIZARDFOLK")),
        new("faithtoken/dragon-head", L.T("DRAGONBORN")), new("delapouite/vampire-dracula", L.T("VAMPIRE"), Male),
        new("lorc/werewolf", L.T("WEREWOLF")), new("lorc/cultist", L.T("CULTIST")),
        new("delapouite/plague-doctor-profile", L.T("PLAGUE DOCTOR")), new("delapouite/fairy", L.T("FAIRY"), Female),
        new("delapouite/archer", L.T("ARCHER")), new("cathelineau/swordman", L.T("SWORDSMAN"), Male),
    ];

    // Dungeon Crawl: ready characters of the pack (people and humanoids dressed as heroes) - after the hero assembled
    // for the race and class, not tied to them.
    private static Portrait[] DcssGallery =>
    [
        new("dcss/monster/human_new", L.T("WANDERER"), Male), new("dcss/monster/human", L.T("ADVENTURER"), Male),
        new("dcss/monster/wizard", L.T("MAGE"), Male), new("dcss/monster/necromancer_new", L.T("NECROMANCER"), Male),
        new("dcss/monster/enchantress_human", L.T("ENCHANTRESS"), Female), new("dcss/monster/hell_knight_new", L.T("BLACK KNIGHT"), Male),
        new("dcss/monster/death_knight", L.T("BLACK KNIGHT")), new("dcss/monster/dwarf_new", L.T("DWARF"), Male),
        new("dcss/monster/deep_dwarf_artificer", L.T("DWARF"), Male), new("dcss/monster/halfling_new", L.T("HALFLING"), Male),
        new("dcss/monster/elf_new", L.T("ELF")), new("dcss/monster/deep_elf_knight_new", L.T("ELF WARRIOR")),
        new("dcss/monster/deep_elf_blademaster", L.T("ELF WARRIOR")), new("dcss/monster/deep_elf_master_archer", L.T("ARCHER")),
        new("dcss/monster/deep_elf_sorcerer", L.T("MAGE")), new("dcss/monster/deep_elf_annihilator", L.T("MAGE")),
        new("dcss/monster/deep_elf_priest", L.T("PRIEST")), new("dcss/monster/deep_elf_high_priest", L.T("PRIEST")),
        new("dcss/monster/orc_knight_new", L.T("ORC"), Male), new("dcss/monster/orc_sorcerer_new", L.T("ORC SHAMAN"), Male),
        new("dcss/monster/unique/donald_new", L.T("ADVENTURER"), Male), new("dcss/monster/unique/duane", L.T("WARRIOR"), Male),
        new("dcss/monster/unique/edmund_new", L.T("WARRIOR"), Male), new("dcss/monster/unique/joseph_new", L.T("WARRIOR"), Male),
        new("dcss/monster/unique/terence_new", L.T("WARRIOR"), Male), new("dcss/monster/unique/frances_male", L.T("KNIGHT"), Male),
        new("dcss/monster/unique/eustachio_new", L.T("NOBLE"), Male), new("dcss/monster/unique/frederick_new", L.T("NOBLE"), Male),
        new("dcss/monster/unique/fannar", L.T("MAGE"), Male), new("dcss/monster/unique/francis", L.T("SAGE"), Male),
        new("dcss/monster/unique/sigmund_new", L.T("NECROMANCER"), Male), new("dcss/monster/unique/harold", L.T("HUNTER"), Male),
        new("dcss/monster/unique/maurice_new", L.T("ROGUE"), Male), new("dcss/monster/unique/michael", L.T("HERMIT"), Male),
        new("dcss/monster/unique/norris", L.T("BARBARIAN"), Male), new("dcss/monster/unique/rupert_new", L.T("BARBARIAN"), Male),
        new("dcss/monster/unique/jory", L.T("VAMPIRE"), Male), new("dcss/monster/unique/wiglaf_new", L.T("DWARF WARRIOR"), Male),
        new("dcss/monster/unique/frances", L.T("SWORDSWOMAN"), Female), new("dcss/monster/unique/margery_new", L.T("SWORDSWOMAN"), Female),
        new("dcss/monster/unique/maud_new", L.T("SWORDSWOMAN"), Female), new("dcss/monster/unique/sonja_new", L.T("ASSASSIN"), Female),
        new("dcss/monster/unique/erica_new", L.T("SORCERESS"), Female), new("dcss/monster/unique/jessica_new", L.T("SORCERESS"), Female),
        new("dcss/monster/unique/louise", L.T("SORCERESS"), Female), new("dcss/monster/unique/psyche_new", L.T("ENCHANTRESS"), Female),
        new("dcss/monster/unique/kirke_new", L.T("PRIESTESS"), Female), new("dcss/monster/unique/josephine_new", L.T("WITCH"), Female),
        new("dcss/monster/unique/agnes_new", L.T("WANDERER"), Female),
    ];

    private static string[] PortraitActions => [L.T("CHOOSE FILE…")];

    // Races and classes of rules with a suitable portrait (gallery slug) - Prompts/{RuleSet}/heroOptions.json.
    private sealed record HeroOption(string Name, string? Portrait);
    private readonly Dictionary<string, string?> _optionPortraits = [];
    // Dungeon Crawl pack: the body of the race ("doll", hair - "dollHair") and gear variants of the class ("dolls") - HeroDoll.
    private readonly Dictionary<string, (string Body, bool Hair)> _raceDolls = [];
    private readonly Dictionary<string, List<HeroDoll.Gear>> _classDolls = [];
    private readonly List<string> _names = [];   // names for "RANDOM"
    private static readonly Random _rnd = new();

    private void LoadHeroOptions(string ruleSet)
    {
        try
        {
            string path = Path.Combine(AppConfig.ProjectRoot, "Prompts", ruleSet, "heroOptions.json");
            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path));
            foreach (var (key, field) in new[] { ("races", RaceField), ("classes", ClassField), ("levels", LevelField), ("alignments", AlignmentField),
                         ("backgrounds", BackgroundField) })
                foreach (var o in root?[key]?.AsArray() ?? [])
                {
                    // Option - a string (“1”) or an object with a portrait ({"name","portrait"}).
                    bool plain = o is System.Text.Json.Nodes.JsonValue;
                    // Options - Russian (translation keys), in the questionnaire and to the GM - in the language of the world.
                    string name = L.W(plain ? o!.ToString() : (string?)o?["name"] ?? "");
                    if (name.Length == 0) continue;
                    _fields[field].Options.Add(name);
                    if (plain) continue;
                    _optionPortraits[name] = (string?)o?["portrait"];
                    if (key == "races" && (string?)o?["doll"] is { Length: > 0 } body)
                        _raceDolls[name] = (body, (bool?)o?["dollHair"] ?? true);
                    if (key == "classes")
                        _classDolls[name] = [.. (o?["dolls"]?.AsArray() ?? []).Select(g => new HeroDoll.Gear(
                            (string?)g?["gear"] ?? "", (string?)g?["hair"], (string?)g?["hairFemale"], (string?)g?["beard"]))];
                }
            foreach (var n in root?[L.WorldIsEnglish ? "namesEn" : "names"]?.AsArray() ?? [])
                if (n?.ToString() is { Length: > 0 } s) _names.Add(s);
        }
        catch { /*no list - leaflets are empty*/ }
        _fields[GenderField].Options.AddRange(GenderNames);
        foreach (int f in ChoiceFields)
        {
            if (_fields[f].Options.Count == 0) _fields[f].Options.Add("—");
            _fields[f].Value = _fields[f].Options[0];
        }
    }

    // “RANDOM”: random name (symbol is its first letters), race, class, level, alignment, background and color;
    // portrait - under them.
    private void Randomize()
    {
        Sound.PlayClick();
        if (_fields[RandomField].ReadOnly) return;
        if (_editing) StopEditing();
        ArchiveNewHero();
        if (_names.Count > 0)
        {
            string name = _names[_rnd.Next(_names.Count)];
            _fields[NameField].Value = name;
            _fields[SymbolField].Value = name.ToUpperInvariant().PadRight(3, name.ToUpperInvariant()[^1])[..3];
        }
        foreach (int f in ChoiceFields)
        {
            var field = _fields[f];
            field.Index = _rnd.Next(field.Options.Count);
            field.Value = field.Options[field.Index];
        }
        _color = _rnd.Next(Palette.Length);
        PortraitFor(ClassField);
    }

    // Select a scrolling option by value (not in the list - add: race/class of a hero from the library).
    private void SetChoice(int field, string value)
    {
        var f = _fields[field];
        if (value.Length == 0) value = f.Options[0];
        int i = f.Options.IndexOf(value);
        if (i < 0) { f.Options.Add(value); i = f.Options.Count - 1; }
        f.Index = i;
        f.Value = value;
    }

    // Picture pack of the adventure (field "PICTURES"): game-icons - ready gallery, Dungeon Crawl - the hero of the
    // selected race and class assembled from layers.
    private static readonly string[] Packs = [CatalogSearch.GameIconsPack, CatalogSearch.DcssPack];
    private static readonly string[] PackNames = ["GAME-ICONS", "DUNGEON CRAWL"];
    private string Pack => Packs[Math.Clamp(_fields[PackField].Index, 0, Packs.Length - 1)];

    // Gender of the hero (field "GENDER"): filters the portraits, goes to the AI with the questionnaire.
    private static readonly string[] Genders = [Male, Female];
    private static string[] GenderNames => [L.T("male"), L.T("female")];
    private string Gender => Genders[Math.Clamp(_fields[GenderField].Index, 0, Genders.Length - 1)];

    private void SetPack(int index)
    {
        _fields[PackField].Index = Math.Clamp(index, 0, Packs.Length - 1);
        _fields[PackField].Value = PackNames[_fields[PackField].Index];
        RebuildPortraits();
    }

    // Pack of a ready-made portrait: a Dungeon Crawl slug - its pack, anything else (game-icons, own file) - game-icons.
    private static int PackOfImage(string? image) =>
        image?.StartsWith(CatalogSearch.DcssPrefix, StringComparison.Ordinal) == true ? Array.IndexOf(Packs, CatalogSearch.DcssPack) : 0;

    // Portraits not from the gallery - own file, hero from the library: always first, with any pack.
    private readonly List<Portrait> _customPortraits = [];
    // Portrait of the hero from the library - only while he is selected (a new hero does not get it).
    private Portrait? _libraryPortrait;
    private int FixedPortraits => _customPortraits.Count + (_libraryPortrait != null ? 1 : 0);

    private void AddCustomPortrait(Portrait portrait)
    {
        _customPortraits.RemoveAll(p => p.Image == portrait.Image);
        _customPortraits.Insert(0, portrait);
        RebuildPortraits();
        _portrait = 0;
    }

    // Gallery anew (pack, race or class changed): the selected portrait remains selected, if it is in the new one.
    private void RebuildPortraits()
    {
        string? selected = _portraits.Count > 0 ? _portraits[_portrait].Image : null;
        _portraits.Clear();
        if (_libraryPortrait != null) _portraits.Add(_libraryPortrait);
        _portraits.AddRange(_customPortraits);
        var gallery = Pack == CatalogSearch.DcssPack ? [.. DollGallery(), .. DcssGallery] : Gallery;
        _portraits.AddRange(gallery.Where(p => p.Gender == null || p.Gender == Gender));
        int index = _portraits.FindIndex(p => p.Image == selected);
        _portrait = index >= 0 ? index : Math.Min(FixedPortraits, _portraits.Count - 1);
    }

    // The race and gender of the hero in all variants of the selected class, then - in the first variant of each
    // other class. Race or class without a recipe - the first one.
    private List<Portrait> DollGallery()
    {
        (string Body, bool Hair) race = _raceDolls.TryGetValue(_fields[RaceField].Value, out var r) ? r : _raceDolls.Values.FirstOrDefault(("human", true));
        string selectedClass = _classDolls.ContainsKey(_fields[ClassField].Value) ? _fields[ClassField].Value : _classDolls.Keys.FirstOrDefault() ?? "";
        var result = new List<Portrait>();
        foreach (var (cls, variants) in _classDolls.OrderBy(c => c.Key != selectedClass))
            for (int v = 0; v < (cls == selectedClass ? variants.Count : Math.Min(1, variants.Count)); v++)
                result.Add(new Portrait(HeroDoll.Slug(race.Body, Gender == Female, variants[v], race.Hair),
                    cls.ToUpperInvariant() + (v > 0 ? $" {v + 1}" : "")));
        return result;
    }

    // Changed race/class - a portrait for them from the gallery (their own picture from the file is not
    // touched). The selected one does not have a suitable one - in another field. Dungeon Crawl - the gallery is
    // assembled for them anew, the first one (a man of the selected class) is selected.
    private void PortraitFor(int changed)
    {
        bool custom = _portrait < FixedPortraits;
        RebuildPortraits();
        if (custom) return;
        if (Pack == CatalogSearch.DcssPack)
        {
            _portrait = Math.Min(FixedPortraits, _portraits.Count - 1);
            return;
        }
        int other = changed == RaceField ? ClassField : RaceField;
        foreach (int f in new[] { changed, other })
            if (_optionPortraits.GetValueOrDefault(_fields[f].Value) is { } slug && _portraits.FindIndex(p => p.Image == slug) is int pi and >= 0)
            {
                _portrait = pi;
                return;
            }
    }
    private const int PortraitWidth = 36; // as in game (SvgToBrailleConverter default): 36 × 18

    private readonly WorldState _settings;
    private readonly DisplayConfig _display;
    private readonly ScreenConfig _screen;
    private readonly GameAiClient _aiClient;
    private readonly BorderDrawer _borderDrawer;
    private int _startTop;

    // Steps of a new game: which one you can go to (F1–F4 / click in the title). The questionnaire lives between steps.
    public Func<int, bool> CanGo = _ => false;
    public NewGameData? Data { get; private set; }
    // The hero was created by the AI during the last “NEXT” (Program saves his card to the hero library).
    public bool CreatedNow { get; set; }
    // Race/class/description/choice of the hero with which the hero was created: changed - create again (with
    // warning); no - the name, symbol, color and portrait are simply transferred to the hero.
    private string? _createdKey;
    private bool _recreateWarned;
    private int? _gotoStep;
    private string? _tabHovered;

    private readonly List<Field> _fields =
    [
        new(L.T("NAME"), Kind.Text, 30), new(L.T("SYMBOL"), Kind.Text, 3),
        new(L.T("RACE"), Kind.Choice), new(L.T("CLASS"), Kind.Choice),
        new(L.T("DESCRIPTION"), Kind.Text, 1500),
        new(L.T("COLOR"), Kind.Color), new(L.T("PORTRAIT"), Kind.Portrait), new("", Kind.Actions),
        new(L.T("HERO"), Kind.Choice),
        new(L.T("REQUEST"), Kind.Text, 300), new(L.T("INVENT"), Kind.AiButton),
        new(L.T("LEVEL"), Kind.Choice), new(L.T("ALIGNMENT"), Kind.Choice),
        new(L.T("RANDOM"), Kind.RandomButton),
        new(L.T("BACKGROUND"), Kind.Choice),
        new(L.T("PICTURES"), Kind.Choice),
        new(L.T("GENDER"), Kind.Choice),
        new(L.T("NEXT"), Kind.Button),   // the button is the last one
    ];
    private const int NameField = 0, SymbolField = 1, RaceField = 2, ClassField = 3, DescriptionField = 4,
        ColorField = 5, HeroField = 8, PromptField = 9, AiField = 10, LevelField = 11, AlignmentField = 12, RandomField = 13,
        BackgroundField = 14, PackField = 15, GenderField = 16;
    private const string Setting = "DnD 5e";   // rules - only one for now (not shown in the questionnaire)

    // The order of the fields on the screen (↑↓): HERO is at the top, the button is at the bottom (in the list
    // _fields the button is the last one). Description of the hero - in the bottom block (like a description of
    // the world): text, request to the AI, INVENT / CHANGE.
    private static readonly int[] Order = [HeroField, 0, 1, 2, 3, GenderField, LevelField, AlignmentField, BackgroundField, 5, PackField, 6, 7, RandomField,
        DescriptionField, PromptField, AiField, 17];

    // HERO (as WORLD at the “World” step): “new hero” (first), deferred options for this game creation (Draft -
    // ready, only read; to the library - only the one with which you moved on) and library heroes (Card).
    private sealed record HeroDraft(string[] Values, int Color, string Image, int Pack);
    private sealed record HeroChoice(HeroLibrary.Card? Card = null, HeroDraft? Draft = null);
    private readonly List<HeroChoice> _heroes = [new()];

    private int _focus;
    private bool _editing;
    private int _color;
    private readonly List<Portrait> _portraits = [];
    private int _portrait;
    private int _action;             // selected button in the line below the portrait
    private string _message = "";
    private readonly List<(int row, int x, int w)> _actionPos = []; // buttons under the portrait (for mouse)

    // Layout
    private int _width, _height, _x0, _bodyTop;
    private int _buttonRow, _buttonX, _buttonWidth, _formLeft;
    private readonly List<(int row, int x)> _swatches = [];      // palette samples (clickable)
    private char[,] _chars = new char[0, 0];
    private List<int>?[,] _fg = new List<int>?[0, 0];
    private List<int>?[,] _bg = new List<int>?[0, 0];
    private readonly List<(int row, int field)> _fieldRows = []; // body lines occupied by field (for mouse)
    private (int row, int left, int right)? _arrows;              // ◄ ► near field in focus (color/portrait)

    // Center layout: “QUESTIONNAIRE” frame on the left, “HERO” frame on the right of the same height.
    private const int FormWidth = 56, PreviewWidth = 40, Gap = 3, BoxInner = 22;
    private const int DescHeight = 13;                        // block “HERO DESCRIPTION”
    private const int LabelCol = 3, ValueCol = 18;           // from the left edge of the questionnaire frame
    private const int MultiLines = 3;                         // lines for description/wishes

    private List<int> Fg => _display.MainForeground;
    private List<int> Bright => ColorHelper.Pale(_display.MainForeground, 0.75);
    private List<int> Dim => ColorHelper.Darker(_display.MainForeground, 0.5);

    // Language of the world in which the questionnaire was collected (options, names): the world has changed to another language - the questionnaire is created anew.
    public string Language { get; } = L.World;

    // iconPack - picture pack of the previous new game (AppConfig.IconPack): the default value of "PICTURES".
    public NewGameDisplay(WorldState settings, DisplayConfig display, ScreenConfig screen, GameAiClient aiClient, string? iconPack = null)
    {
        _aiClient = aiClient;
        _screen = screen;
        _settings = settings;
        _display = display;
        _borderDrawer = new BorderDrawer(settings, display);
        _startTop = Term.CursorTop;
        LoadHeroOptions(aiClient.RuleSet);
        _fields[PackField].Options.AddRange(PackNames);
        _fields[PackField].Index = Math.Max(0, Array.IndexOf(Packs, CatalogSearch.NormalizePack(iconPack)));
        _fields[PackField].Value = PackNames[_fields[PackField].Index];
        RebuildPortraits();
        PortraitFor(RaceField);
        _fields[HeroField].Options.Add(L.T("new hero"));
        foreach (var h in HeroLibrary.All())
        {
            _heroes.Add(new(h));
            _fields[HeroField].Options.Add(h.Name);
        }
    }

    // The HERO option is selected: a person from the library - to the fields; “new” - fields from scratch. A
    // hero from the library - his race and class are fixed (level and skills transfer as is). What was entered
    // for a new hero is stored while you are flipping through the heroes of the library (returned to “new” -
    // everything is as it was).
    private static readonly int[] PersonalFields = [NameField, SymbolField, RaceField, ClassField, LevelField, AlignmentField, BackgroundField, DescriptionField, GenderField];
    private static readonly int[] ChoiceFields = [RaceField, ClassField, LevelField, AlignmentField, BackgroundField, GenderField];
    private HeroDraft? _newHeroDraft;
    private string _newHeroPrompt = "";

    private HeroDraft Snapshot() => new([.. PersonalFields.Select(f => _fields[f].Value)], _color, _portraits[_portrait].Image, _fields[PackField].Index);

    private void ApplyHeroChoice(int previous)
    {
        if (previous == 0) { _newHeroDraft = Snapshot(); _newHeroPrompt = _fields[PromptField].Value; }
        _descScroll = 0;
        var h = _heroes[_fields[HeroField].Index];
        bool isNew = h.Card == null && h.Draft == null;
        // A ready-made hero (library or deferred version) is like a ready-made world: everything is read-only.
        foreach (int f in PersonalFields.Concat([ColorField, ColorField + 1, ColorField + 2, PackField, PromptField, AiField, RandomField])) _fields[f].ReadOnly = !isNew;
        _fields[PromptField].Value = isNew ? _newHeroPrompt : "";
        // A ready-made hero keeps the picture pack of his portrait (old heroes - game-icons), it is not changed.
        if (h.Card != null)
        {
            SetPack(PackOfImage(h.Card.Image));
            ApplyPrefill(h.Card);
            bool? female = h.Card.Image?.StartsWith(HeroDoll.Prefix, StringComparison.Ordinal) == true ? h.Card.Image.Contains("_female") : null;
            if (female != null) SetChoice(GenderField, GenderNames[female.Value ? 1 : 0]);
            else _fields[GenderField].Value = "—";
            return;
        }
        var d = h.Draft ?? _newHeroDraft;
        if (d != null) SetPack(d.Pack);
        for (int i = 0; i < PersonalFields.Length; i++)
        {
            string v = d?.Values[i] ?? "";
            if (ChoiceFields.Contains(PersonalFields[i])) SetChoice(PersonalFields[i], v);
            else _fields[PersonalFields[i]].Value = v;
        }
        // The gallery - by the restored pack, race, class and gender; the library portrait is gone.
        _libraryPortrait = null;
        RebuildPortraits();
        if (d != null) _color = d.Color;
        int pi = d != null ? _portraits.FindIndex(p => p.Image == d.Image) : -1;
        _portrait = pi >= 0 ? pi : Math.Min(FixedPortraits, _portraits.Count - 1);
    }

    // The new hero already has a description - before “RAND” he becomes a ready-made option in the HERO page
    // (only for the duration of this game creation), and the description of the new one is from scratch (like a
    // world with a description before a new map).
    private void ArchiveNewHero()
    {
        if (_fields[HeroField].Index != 0 || _fields[DescriptionField].Value.Trim().Length == 0) return;
        _heroes.Insert(1, new(Draft: Snapshot()));
        _fields[HeroField].Options.Insert(1, _fields[NameField].Value.Trim() is { Length: > 0 } n ? n : L.T("draft"));
        _fields[DescriptionField].Value = _fields[PromptField].Value = "";
        _message = L.T("The previous version is in the HERO list (◄ ►) while this game is being created");
    }

    // Hero from the library (step “Hero” of the new game GM) - the fields of the questionnaire are filled in with his personality, they can be corrected.
    private void ApplyPrefill(HeroLibrary.Card h)
    {
        _fields[NameField].Value = h.Name;
        _fields[SymbolField].Value = h.Symbol;
        if (h.Race.Length > 0) SetChoice(RaceField, h.Race);
        if (h.Class.Length > 0) SetChoice(ClassField, h.Class);
        SetChoice(LevelField, System.Text.RegularExpressions.Regex.Match(h.Level, @"\d+").Value);
        SetChoice(AlignmentField, HeroStat(h.HeroJson, StatKeys.Alignment));
        SetChoice(BackgroundField, HeroStat(h.HeroJson, StatKeys.Background));
        _fields[DescriptionField].Value = h.Description;   // no description - empty (not the text of the previous hero)
        if (h.Color != null && Array.FindIndex(Palette, p => p.Rgb.SequenceEqual(h.Color)) is int ci and >= 0) _color = ci;
        _libraryPortrait = null;   // the portrait of the previously viewed library hero
        RebuildPortraits();
        if (h.Image != null)
        {
            int pi = _portraits.FindIndex(p => p.Image == h.Image);
            if (pi < 0)
            {
                _libraryPortrait = new Portrait(h.Image, h.Name.ToUpperInvariant());
                RebuildPortraits();
                pi = 0;
            }
            _portrait = pi;
        }
    }

    // Hero stat from his JSON (character sheet) by key (StatKeys); no - "".
    private static string HeroStat(string? heroJson, string key)
    {
        try
        {
            return System.Text.Json.Nodes.JsonNode.Parse(heroJson ?? "")?["stats"]?.AsArray()
                .FirstOrDefault(s => (string?)s?["key"] == key)?["value"]?.ToString() ?? "";
        }
        catch { return ""; }
    }

    // Questionnaire (step 2). Returns the step to go to: 1 - world, 3/4 - further (the hero is created, Data), 0 - exit to the menu.
    public async Task<int> Show()
    {
        _startTop = Term.CursorTop;
        _titleTabsCache = null;   // title may have changed (steps available)
        _tabHovered = null;
        _exitToMenu = false;
        _recreateWarned = false;
        Term.CursorVisible = false;
        _borderDrawer.DrawSeparator();
        _bodyTop = _startTop + 1;
        _width = _display.InnerWidth(_display.ViewCols(_settings.Map));
        _height = Math.Max(24, Term.WindowHeight - _bodyTop - 1);
        _x0 = DisplayConfig.LeftMargin + 1;
        Term.SetCursorPosition(0, _bodyTop);
        for (int r = 0; r < _height; r++) _borderDrawer.DrawContentLine(() => { });
        _borderDrawer.DrawBottomBorder();

        _editing = true; // first field - immediately for input
        Redraw();

        while (true)
        {
            // AI writes a description - the screen is waiting (only Esc), waiting.
            if (_describeTask != null)
            {
                if (_describeTask.IsCompleted) FinishDescribe();
                else
                {
                    ConsoleMouseReader.DrainWhileWaiting();
                    ConsoleMouseReader.SetCursorShape(false);   // nothing is pressed - normal pointer
                    if (ConsoleMouseReader.TryReadKey() is { Key: ConsoleKey.Escape }) return 0;
                    _frame++;
                    Redraw();
                    await Task.Delay(100);
                }
                continue;
            }
            if (HandleMouse() is { } mouseResult)
            {
                if (_exitToMenu) { _exitToMenu = false; return 0; }
                if (_gotoStep is int g)
                {
                    _gotoStep = null;
                    if (await Go(g, sound: false) is int to) return to;
                    Redraw();
                    continue;
                }
                if (mouseResult && await Go(3) is int next) return next;
                continue;
            }
            // Keys - directly from the console buffer (code + symbol), as in the main menu: Console.ReadKey,
            // mixed with reading the mouse, parsed some of the keystrokes incorrectly.
            if (ConsoleMouseReader.TryReadKey() is not { } key) { Thread.Sleep(15); continue; }

            // Symbols that came in one batch: the console “prints” the file dragged into the window by - we
            // ignore such an insertion (our own picture is only done through “SELECT FILE ...").
            var keys = new List<ConsoleKeyInfo> { key };
            if (!char.IsControl(key.KeyChar))
            {
                while (ConsoleMouseReader.TryReadKey() is { } next)
                {
                    keys.Add(next);
                    if (char.IsControl(next.KeyChar)) break;
                }
                string burst = new(keys.Where(k => !char.IsControl(k.KeyChar)).Select(k => k.KeyChar).ToArray());
                if (burst.Length > 3 && IsDroppedFile(burst)) keys.RemoveAll(k => !char.IsControl(k.KeyChar));
            }

            foreach (var k in keys)
            {
                if (k.Key == ConsoleKey.Escape) return 0;
                if (k.Key is >= ConsoleKey.F1 and <= ConsoleKey.F4)
                {
                    if (await Go(k.Key - ConsoleKey.F1 + 1) is int to) return to;
                    break;
                }
                if (k.Key != ConsoleKey.Enter) _recreateWarned = false;   // warning - only for Enter in a row
                _message = ""; // hint/error - until next click
                bool create = _editing ? HandleEditKey(k) : HandleNavKey(k);
                if (create && await Go(3) is int next) return next;
            }
            Redraw();
        }
    }

    // Go to step: 1 - world (filled remains), 3/4 - further: the hero is created (or appearance changes are
    // transferred). null - we remain (not everything is filled, we are waiting for confirmation of
    // re-creation).
    private async Task<int?> Go(int step, bool sound = true)
    {
        if (step == 1) { if (sound) Sound.PlayClick(); return 1; }
        if (step == 2) return null;
        if (!CanCreate())
        {
            _message = L.T("Fill in all form fields: ") + string.Join(", ", MissingFields());
            return null;
        }
        string key = EssentialKey();
        if (_createdKey == key && Data != null)
        {
            ApplyLooks();
            return step;
        }
        if (_createdKey != null && !_recreateWarned)
        {
            _recreateWarned = true;
            _message = L.T("Race, class, level, background or hero changed — the character will be created again.\nEditor changes will be lost. Press again to create.");
            return null;
        }
        _recreateWarned = false;
        Data = await Create();
        _createdKey = key;
        CreatedNow = true;
        return step;
    }

    // Description (backstory) is not a character sheet: its edit does not recreate the character, it is only
    // transferred. The choice of HERO is based on the entry itself (the indices are shifted when the deferred
    // option appears in the leaflet).
    private string EssentialKey() => string.Join("|", System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_heroes[_fields[HeroField].Index]),
        _fields[RaceField].Value.Trim(),
        _fields[ClassField].Value.Trim(), _fields[LevelField].Value, _fields[BackgroundField].Value, Pack);

    // An already created hero: name, symbol, color and portrait from the profile - into the hero as is, without a AI.
    private void ApplyLooks()
    {
        var portrait = _portraits[_portrait];
        Data!.Name = _fields[NameField].Value.Trim();
        Data.Symbol = _fields[SymbolField].Value.Trim();
        Data.Description = _fields[DescriptionField].Value.Trim();
        Data.Alignment = _fields[AlignmentField].Value;
        Data.Color = [.. Palette[_color].Rgb];
        Data.Image = string.IsNullOrEmpty(portrait.Image) ? null : portrait.Image;
        Data.IconPack = Pack;
        Data.Gender = _fields[GenderField].Value == "—" ? null : Gender;
        if (_settings.Hero is not { } hero) return;
        hero.Name = Data.Name;
        if (hero.Stat(StatKeys.Alignment) is { } align)
            align.Value = Data.Alignment;
        if (Data.Symbol.Length > 0) hero.Symbol = Data.Symbol;
        hero.Color = [.. Data.Color];
        if (Data.Image != null) hero.Image = Data.Image;
    }

    private static bool IsDroppedFile(string text)
    {
        string path = text.Trim().Trim('"');
        try { return Path.IsPathRooted(path) && (File.Exists(path) || Directory.Exists(path)); }
        catch { return false; }
    }

    // ── Keyboard ────────────────────────────── ──────────────────────────────
    private bool HandleEditKey(ConsoleKeyInfo key)
    {
        var field = _fields[_focus];
        ref string value = ref field.Value;
        int max = field.MaxLength;

        // Description - field with cursor: ←→ Home End ↑↓ PgUp/PgDn; ↑ on the first / ↓ on the last line - to the adjacent field.
        if (_focus == DescriptionField && key.Key != ConsoleKey.Enter)
        {
            if (TextArea.Edit(ref value, ref _descCursor, key, max, _descRect.w, _descRect.rows)) { _descFollow = true; return false; }
            if (key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
            {
                StopEditing();
                MoveFocus(key.Key == ConsoleKey.UpArrow ? -1 : 1);
            }
            return false;
        }

        if (ClipboardText.IsPasteKey(key))
        {
            string paste = ClipboardText.Get();
            value += paste[..Math.Min(paste.Length, Math.Max(0, max - value.Length))];
            return false;
        }

        switch (key.Key)
        {
            case ConsoleKey.Enter when _focus == PromptField:
                if (_fields[PromptField].Value.Trim().Length == 0) return false;   // empty input - do not send
                StopEditing();
                RunDescribe();
                return false;
            case ConsoleKey.Enter:
                StopEditing();
                // In a row in text fields - immediately enter the next one (as before in the questionnaire).
                // (after CLASS - description in the lower block, after description - query line).
                if (field.Kind == Kind.Text && _focus != PromptField)
                {
                    int pos = Array.IndexOf(Order, _focus);
                    int next = Order.Skip(pos + 1).FirstOrDefault(i => _fields[i].Kind == Kind.Text && !_fields[i].ReadOnly, -1);
                    _focus = next >= 0 ? next : NextFocusable(_focus, 1);
                    _editing = _fields[_focus].Kind == Kind.Text;
                    if (_focus == DescriptionField) FocusDescriptionEnd();
                }
                return false;
            case ConsoleKey.UpArrow: StopEditing(); MoveFocus(-1); return false;
            case ConsoleKey.DownArrow: StopEditing(); MoveFocus(1); return false;
            case ConsoleKey.Backspace:
                if (value.Length > 0) value = value[..^1];
                return false;
        }
        if (!char.IsControl(key.KeyChar) && value.Length < max) value += key.KeyChar;
        return false;
    }

    private bool HandleNavKey(ConsoleKeyInfo key)
    {
        var field = _fields[_focus];
        switch (key.Key)
        {
            case ConsoleKey.UpArrow: MoveFocus(-1); return false;
            case ConsoleKey.DownArrow or ConsoleKey.Tab: MoveFocus(1); return false;
            case ConsoleKey.LeftArrow: Step(field, -1); return false;
            case ConsoleKey.RightArrow: Step(field, 1); return false;
            case ConsoleKey.Enter:
                switch (field.Kind)
                {
                    case Kind.Text: _editing = true; return false;
                    case Kind.Actions: RunAction(_action); return false;
                    case Kind.AiButton: RunDescribe(); return false;
                    case Kind.RandomButton: Randomize(); return false;
                    case Kind.Button:
                        if (CanCreate()) return true;
                        _message = L.T("Fill in all form fields: ") + string.Join(", ", MissingFields());
                        return false;
                }
                return false;
        }
        // Print or paste on a text field - enter immediately.
        if (field.Kind == Kind.Text && !field.ReadOnly && (!char.IsControl(key.KeyChar) || ClipboardText.IsPasteKey(key)))
        {
            _editing = true;
            return HandleEditKey(key);
        }
        return false;
    }

    // A text field with focus is immediately open for input - the “▌” cursor shows where the text will go.
    private void MoveFocus(int dir)
    {
        _focus = NextFocusable(_focus, dir);
        _editing = _fields[_focus].Kind == Kind.Text;
        if (_focus == DescriptionField) FocusDescriptionEnd();
        Sound.PlayClick();
    }

    // Next field in direction dir, skipping fixed (SETTING); at the edge - remains.
    private int NextFocusable(int from, int dir)
    {
        int pos = Array.IndexOf(Order, from);
        for (int i = pos + dir; i >= 0 && i < Order.Length; i += dir)
            if (!_fields[Order[i]].ReadOnly && (Order[i] != HeroField || _heroes.Count > 1)) return Order[i];
        return from;
    }

    private void Step(Field field, int dir)
    {
        if (field.Kind == Kind.Color) _color = (_color + dir + Palette.Length) % Palette.Length;
        else if (field.Kind == Kind.Portrait) _portrait = (_portrait + dir + _portraits.Count) % _portraits.Count;
        else if (field.Kind == Kind.Actions) _action = (_action + dir + PortraitActions.Length) % PortraitActions.Length;
        else if (field.Kind == Kind.Choice && field.Options.Count > 0)
        {
            int previous = field.Index;
            field.Index = (field.Index + dir + field.Options.Count) % field.Options.Count;
            if (field == _fields[HeroField]) ApplyHeroChoice(previous);
            else
            {
                field.Value = field.Options[field.Index];
                int fi = _fields.IndexOf(field);
                if (fi is RaceField or ClassField or PackField or GenderField) PortraitFor(fi is PackField or GenderField ? ClassField : fi);
            }
        }
        else return;
        Sound.PlayClick();
    }

    private void StopEditing() => _editing = false;

    // Button under the portrait - your own picture through the window Windows.
    private void RunAction(int action)
    {
        Sound.PlayClick();
        PickCustomImage();
    }

    // You can create when all text fields of the questionnaire are filled in.
    private bool CanCreate() => !MissingFields().Any();

    private IEnumerable<string> MissingFields() =>
        _fields.Where(f => f.Kind == Kind.Text && f != _fields[PromptField] && f.Value.Trim().Length == 0).Select(f => f.Label.ToLowerInvariant());

    // ── Description of the hero from AI ───────────────────── ──────────────────────
    private Task<string?>? _describeTask;
    private int _frame;
    private (int row, int x, int w)? _aiButton, _randomButton;
    // Description: cursor, scroll (first visible line), scroll behind cursor (after editing), text place.
    private int _descCursor, _descScroll;
    private bool _descFollow = true;
    private (int top, int x, int w, int rows) _descRect = (0, 0, 1, 1);

    private void FocusDescriptionEnd()
    {
        _descCursor = _fields[DescriptionField].Value.Length;
        _descFollow = true;
    }
    private bool _randomHovered;
    private readonly Dictionary<int, (int row, int x, int w)> _choiceArrows = [];   // leaflets: field → ◄ … ►
    private bool _aiHovered;
    private (int field, int dir) _arrowHovered;   // scroll arrow under the mouse

    // INVENT/CHANGE: description (appearance, character, background) by name/race/class and request; there is a
    // text - the AI finalizes it.
    private void RunDescribe()
    {
        Sound.PlayClick();
        if (_describeTask != null || _fields[AiField].ReadOnly) return;
        // Without a request, the AI just comes up with ideas from scratch; rewriting a ready-made description “about nothing” is a no-no.
        if (_fields[PromptField].Value.Trim().Length == 0 && _fields[DescriptionField].Value.Trim().Length > 0)
        {
            _message = L.T("Write in \"Input\" what to change in the description");
            _focus = PromptField;
            _editing = true;
            return;
        }
        _describeTask = _aiClient.DescribeHero(_fields[NameField].Value.Trim(), _fields[RaceField].Value.Trim(),
            _fields[ClassField].Value.Trim(), _fields[DescriptionField].Value.Trim(), _fields[PromptField].Value.Trim(),
            $"Уровень: {_fields[LevelField].Value}\nМировоззрение: {_fields[AlignmentField].Value}\nПредыстория: {_fields[BackgroundField].Value}\nGender: {Gender}");
    }

    private void FinishDescribe()
    {
        if (_describeTask?.IsFaulted == true)
            _message = _describeTask.Exception?.GetBaseException() is AiSetupException setup
                ? setup.Message : L.T("The AI didn't respond — try again.");
        else if (_describeTask is { IsCompletedSuccessfully: true, Result: { Length: > 0 } text })
        {
            _fields[DescriptionField].Value = text.Length > 1500 ? text[..1500] : text;
            _fields[PromptField].Value = "";   // request completed - input line is empty
            _descScroll = 0;
            _descCursor = text.Length;
            _descFollow = false;   // new text - from the beginning
        }
        else _message = L.T("The AI didn't respond — try again.");
        _describeTask = null;
        Redraw();
    }

    private bool _buttonHovered;
    private bool _exitToMenu;  // click on “[Esc]MENU” in the header
    private List<MouseUiHelper.TitleTab> _titleTabs => _titleTabsCache ??= MouseUiHelper.ComputeTitleTabs(_screen.Title);
    private List<MouseUiHelper.TitleTab>? _titleTabsCache;
    private int _actionHovered = -1; // button under the portrait under the mouse cursor

    // ── Mouse ───────────────────────────────── ───────────────────────────────── null - nothing; false -
    // something has changed (redrawn); true - “CREATE” is pressed.
    private bool? HandleMouse()
    {
        var (move, click, _) = ConsoleMouseReader.DrainMouseEvents();
        // Wheel above description - scrolling text.
        if (ConsoleMouseReader.TakeWheel(out var wp) is int notches and not 0)
        {
            int wr = wp.y - _bodyTop - _descRect.top, wc = wp.x - _x0;
            if (wr >= 0 && wr < _descRect.rows && wc >= _formLeft && wc < _formLeft + FormWidth + Gap + PreviewWidth)
            {
                _descScroll -= notches;
                _descFollow = false;
                Redraw();
                return false;
            }
        }
        if (move is { } m)
        {
            int hoverField = FieldAt(m.x, m.y);
            bool overButton = hoverField == _fields.Count - 1;
            int overAction = ActionAt(m.x, m.y);
            bool clickable = hoverField >= 0 && !_fields[hoverField].ReadOnly && _fields[hoverField].Kind != Kind.Actions;
            string? overTab = MouseUiHelper.GetHoveredTabKey(m.x, m.y, _titleTabs);
            ConsoleMouseReader.SetCursorShape(clickable || overTab != null || overAction >= 0 || IsArrow(m.x, m.y) != 0 || SwatchAt(m.x, m.y) >= 0);
            if (overTab != _tabHovered)
            {
                if (_tabHovered != null) MouseUiHelper.SetTabHighlight(_screen.Title, _titleTabs, _tabHovered, false, _display);
                if (overTab != null) MouseUiHelper.SetTabHighlight(_screen.Title, _titleTabs, overTab, true, _display);
                _tabHovered = overTab;
            }
            bool overAi = OnRect(_aiButton, m.x, m.y);
            bool overRandom = OnRect(_randomButton, m.x, m.y);
            if (overRandom) ConsoleMouseReader.SetCursorShape(true);
            if (overRandom != _randomHovered) { _randomHovered = overRandom; Redraw(); }
            var overArrow = ChoiceArrowAt(m.x, m.y);
            if (overAi || overArrow.dir != 0) ConsoleMouseReader.SetCursorShape(true);
            if (overButton != _buttonHovered || overAction != _actionHovered || overAi != _aiHovered || overArrow != _arrowHovered)
            {
                _buttonHovered = overButton;
                _actionHovered = overAction;
                _aiHovered = overAi;
                _arrowHovered = overArrow;
                Redraw();
            }
        }
        if (click is not { } c) return null;

        int swatch = _fields[ColorField].ReadOnly ? -1 : SwatchAt(c.x, c.y);
        if (swatch >= 0)
        {
            if (_editing) StopEditing();
            _focus = _fields.FindIndex(f => f.Kind == Kind.Color);
            _color = swatch;
            Sound.PlayClick();
            Redraw();
            return false;
        }

        // “[Esc]MENU” in the title – as Esc, “[Fn]step” – as F1–F4; the rest of the header is dragging the window.
        if (MouseUiHelper.GetHoveredTabKey(c.x, c.y, _titleTabs) is { } tabKey)
        {
            Sound.PlayClick();
            ConsoleMouseReader.SetCursorShape(false);
            if (tabKey == "Esc") _exitToMenu = true;
            else _gotoStep = tabKey[1] - '0';
            return false;
        }
        if (c.y < _bodyTop) { ConsoleMouseReader.StartWindowDrag(); return null; }
        int actionClicked = _fields[ColorField + 2].ReadOnly ? -1 : ActionAt(c.x, c.y);
        if (actionClicked >= 0)
        {
            if (_editing) StopEditing();
            _focus = _fields.FindIndex(f => f.Kind == Kind.Actions);
            _action = actionClicked;
            RunAction(actionClicked);
            Redraw();
            return false;
        }
        int arrow = IsArrow(c.x, c.y);
        if (arrow != 0)
        {
            // Portrait arrows - scroll through the portrait, no matter where the focus is.
            if (_editing) StopEditing();
            _focus = _fields.FindIndex(f => f.Kind == Kind.Portrait);
            Step(_fields[_focus], arrow);
            Redraw();
            return false;
        }

        if (OnRect(_randomButton, c.x, c.y))
        {
            _focus = RandomField;
            Randomize();
            Redraw();
            return false;
        }
        if (OnRect(_aiButton, c.x, c.y))
        {
            if (_editing) StopEditing();
            _focus = AiField;
            RunDescribe();
            Redraw();
            return false;
        }
        if (ChoiceArrowAt(c.x, c.y) is { dir: not 0 } arrowHit)
        {
            if (_editing) StopEditing();
            _focus = arrowHit.field;
            Step(_fields[arrowHit.field], arrowHit.dir);
            Redraw();
            return false;
        }

        int field = FieldAt(c.x, c.y);
        if (field < 0 || _fields[field].ReadOnly || _fields[field].Kind == Kind.Actions) return null;
        if (_fields[field].Kind == Kind.Choice)
        {
            // Choice (world/hero): click - next option.
            if (_editing) StopEditing();
            _focus = field;
            Step(_fields[field], 1);
            Redraw();
            return false;
        }
        if (_fields[field].Kind == Kind.Button)
        {
            if (_editing) StopEditing();
            Sound.PlayClick();
            if (CanCreate()) return true;
            _focus = field;
            _message = L.T("Fill in all form fields: ") + string.Join(", ", MissingFields());
            Redraw();
            return false;
        }
        if (_editing) StopEditing();
        _focus = field;
        Sound.PlayClick();
        _editing = _fields[field].Kind == Kind.Text;
        // Click on the description text - the cursor goes to this place.
        if (field == DescriptionField)
        {
            _descCursor = TextArea.CursorAt(_fields[DescriptionField].Value, _descScroll, _descRect.w,
                c.y - _bodyTop - _descRect.top, c.x - _x0 - _descRect.x);
            _descFollow = true;
        }
        Redraw();
        return false;
    }

    // The field under the mouse is only inside the questionnaire frame (the preview lines on the right coincide
    // with the field lines), the “CREATE” button is only based on its text.
    private int FieldAt(short x, short y)
    {
        foreach (var (row, field) in _fieldRows)
        {
            if (y != _bodyTop + row) continue;
            bool inside = _fields[field].Kind == Kind.Button
                ? IsOnButton(x)
                : field is DescriptionField or PromptField   // bottom block - full width
                    ? x > _x0 + _formLeft && x < _x0 + _formLeft + FormWidth + Gap + PreviewWidth - 1
                    : x > _x0 + _formLeft && x < _x0 + _formLeft + FormWidth - 1;
            return inside ? field : -1;
        }
        return -1;
    }

    private int SwatchAt(short x, short y)
    {
        for (int i = 0; i < _swatches.Count; i++)
        {
            var (row, sx) = _swatches[i];
            if (y == _bodyTop + row && x >= _x0 + sx && x < _x0 + sx + 2) return i;
        }
        return -1;
    }

    private int ActionAt(short x, short y)
    {
        for (int i = 0; i < _actionPos.Count; i++)
        {
            var (row, ax, w) = _actionPos[i];
            if (y == _bodyTop + row && x >= _x0 + ax && x < _x0 + ax + w) return i;
        }
        return -1;
    }

    private bool OnRect((int row, int x, int w)? r, short x, short y) =>
        r is { } b && y == _bodyTop + b.row && x >= _x0 + b.x && x < _x0 + b.x + b.w;

    private (int field, int dir) ChoiceArrowAt(short x, short y)
    {
        foreach (var (field, a) in _choiceArrows)
        {
            if (y != _bodyTop + a.row) continue;
            if (x == _x0 + a.x) return (field, -1);
            if (x == _x0 + a.x + a.w) return (field, 1);
        }
        return (0, 0);
    }

    private bool IsOnButton(short x) => x >= _x0 + _buttonX && x < _x0 + _buttonX + _buttonWidth;

    private int IsArrow(short x, short y)
    {
        if (_arrows is not { } a || y != _bodyTop + a.row) return 0;
        if (x == _x0 + a.left) return -1;
        if (x == _x0 + a.right) return 1;
        return 0;
    }

    // ── Portraits ─────────────────────────────── ─────────────────────────────── “SELECT FILE...”: window
    // Windows, the file is copied to Storage/Portraits and appears first in the gallery.
    private void PickCustomImage()
    {
        string? path = FileDialog.OpenFile(L.T("Hero portrait"),
            L.T("Images (PNG, JPG, BMP, GIF, SVG)") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.svg");
        if (path == null) return;
        if (!SvgToBrailleConverter.IsSupportedImageFile(path))
        {
            _message = L.T("Format not supported (PNG, JPG, BMP, GIF, SVG)");
            return;
        }
        try
        {
            Directory.CreateDirectory(SvgToBrailleConverter.PortraitsDir);
            string fileName = Path.GetFileName(path);
            string target = Path.Combine(SvgToBrailleConverter.PortraitsDir, fileName);
            for (int n = 2; File.Exists(target) && !SameFile(target, path); n++)
                target = Path.Combine(SvgToBrailleConverter.PortraitsDir, $"{Path.GetFileNameWithoutExtension(path)}_{n}{Path.GetExtension(path)}");
            if (!File.Exists(target)) File.Copy(path, target);

            string image = SvgToBrailleConverter.PortraitsPrefix + Path.GetFileName(target);
            if (SvgToBrailleConverter.Convert(image, "", PortraitWidth) == null)
            {
                _message = L.T("Could not read the image");
                return;
            }
            AddCustomPortrait(new Portrait(image, L.T("CUSTOM: ") + Path.GetFileNameWithoutExtension(target).ToUpperInvariant()));
            _message = "";
        }
        catch (Exception ex)
        {
            _message = L.T("Could not load the image: ") + ex.Message;
        }
    }

    private static bool SameFile(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    // ── Creation ─────────────────────────────── ───────────────────────────────
    private async Task<NewGameData> Create()
    {
        var portrait = _portraits[_portrait];
        var result = new NewGameData
        {
            Name = _fields[NameField].Value.Trim(),
            Symbol = _fields[SymbolField].Value.Trim(),
            Race = _fields[RaceField].Value.Trim(),
            Class = _fields[ClassField].Value.Trim(),
            Level = _fields[LevelField].Value,
            Alignment = _fields[AlignmentField].Value,
            Background = _fields[BackgroundField].Value,
            Description = _fields[DescriptionField].Value.Trim(),
            Setting = Setting,
            SettingWish = "",   // wishes for the game - step “Adventure” after the editor
            Color = [.. Palette[_color].Rgb],
            Image = string.IsNullOrEmpty(portrait.Image) ? null : portrait.Image,
            IconPack = Pack,
            Gender = _fields[GenderField].Value == "—" ? null : Gender,
        };

        // Waiting - in the center, in the place of the hint under the button (and not the general spinner at
        // the edge of the screen). A hero from the library is not created anew: he is preparing for a new
        // adventure (equipment, condition).
        _editing = false;
        var reused = _heroes[_fields[HeroField].Index].Card;
        var aiTask = reused?.HeroJson is { } heroJson ? _aiClient.ReuseHero(result, heroJson) : _aiClient.CreateNewGame(result);
        string waitText = reused?.HeroJson != null ? L.T("Preparing the hero for a new adventure...") : L.T("Creating the character...");
        for (int frame = 0; !aiTask.IsCompleted; frame++)
        {
            // While the character is being created, clicks are not accepted, the pointer is normal (not a “hand”).
            ConsoleMouseReader.DrainWhileWaiting();
            ConsoleMouseReader.SetCursorShape(false);
            _message = $"{Spinner.Frames[frame % Spinner.Frames.Length]} {waitText}";
            Redraw();
            await Task.Delay(100);
        }
        _message = "";
        await aiTask;
        return result;
    }

    // ── Rendering ────────────────────────────── ─────────────────────────────── Everything is in the center
    // of the screen: the title, below it the frames “QUESTIONNAIRE” and “HERO” of the same height, below it is
    // a button and a hint. Fields - dense, in groups; description and wishes - in several lines with
    // hyphenation.
    private void Redraw()
    {
        _chars = new char[_height, _width];
        _fg = new List<int>?[_height, _width];
        _bg = new List<int>?[_height, _width];
        for (int r = 0; r < _height; r++) for (int c = 0; c < _width; c++) _chars[r, c] = ' ';
        _fieldRows.Clear();
        _arrows = null;

        int total = FormWidth + Gap + PreviewWidth;
        int left = Math.Max(1, (_width - total) / 2);
        _formLeft = left;
        int boxHeight = BoxInner + 2;
        int blockHeight = boxHeight + 1 + DescHeight + 1 + 1 + 3;   // frames, description, button, tooltip (up to 2 lines)
        int top = Math.Max(0, (_height - blockHeight) / 2);
        int boxTop = top; // there is no separate title - “NEW GAME” is already in the title bar of the screen

        DrawBox(boxTop, left, FormWidth, boxHeight, L.T("FORM"));
        DrawBox(boxTop, left + FormWidth + Gap, PreviewWidth, boxHeight, L.T("HERO"));

        // Profile fields: groups through an empty line, from the top of the frame (not centered - otherwise the
        // entire profile would move out when the description/wishes are added to the line).
        _swatches.Clear();
        int y = boxTop + 2;
        int valueWidth = FormWidth - ValueCol - 3;
        _actionPos.Clear();
        foreach (int group in new[] { HeroField, -1, NameField, SymbolField, -1, RaceField, ClassField, GenderField, LevelField, AlignmentField, BackgroundField, -1,
                     ColorField, -1, PackField, ColorField + 1, -1, ColorField + 2 })
        {
            if (group < 0) { y++; continue; }
            y += DrawField(group, y, left, valueWidth);
        }

        // Button: inactive (dark, without arrows) until all fields are filled in; active - with arrows when
        // selected with the keyboard or hovered with the mouse. The width is the same - the text does not jump.
        // “RANDOM” - in a space at the bottom of the questionnaire frame (like the map buttons on the “World”
        // step); the finished hero does not.
        _randomButton = null;
        if (!_fields[RandomField].ReadOnly)
        {
            int ry = boxTop + boxHeight - 3, rx = left + LabelCol;
            _randomButton = (ry, rx, Chip(ry, rx, L.T("RANDOM"), _focus == RandomField || _randomHovered, false));
        }

        DrawDescription(boxTop + boxHeight + 1, left, total);

        _buttonRow = boxTop + boxHeight + 1 + DescHeight + 1;
        var button = _fields[^1];
        bool enabled = CanCreate();
        bool highlighted = enabled && (_focus == _fields.Count - 1 || _buttonHovered);
        _buttonX = left + Math.Max(0, (total - button.Label.Length - 4) / 2);
        _buttonWidth = Chip(_buttonRow, _buttonX, button.Label, highlighted, !enabled);
        _fieldRows.Add((_buttonRow, _fields.Count - 1));
        // Hint or message - up to two lines (“\n” - hyphenation).
        var hint = (_message.Length > 0 ? _message : L.T("[↑↓]FIELD   [Enter]EDIT   [←→]BROWSE   [F1]WORLD   [Esc]MENU")).Split('\n');
        for (int i = 0; i < hint.Length && i < 2; i++)
            CenterIn(_buttonRow + 2 + i, left, total, Fit(hint[i], total), _recreateWarned ? Bright : Dim);

        DrawPreview(boxTop + 1, left + FormWidth + Gap);

        Term.CursorVisible = false;
        for (int r = 0; r < _height; r++) WriteRow(r);
    }

    // Block “DESCRIPTION OF THE HERO” (as a description of the world): at the top is the text – your own or
    // from the AI; under the separator - “Enter:” of the request and INVENT / CHANGE. The finished hero is just
    // text for the entire block.
    private void DrawDescription(int top, int left, int total)
    {
        var desc = _fields[DescriptionField];
        string name = _fields[NameField].Value.Trim();
        DrawBox(top, left, total, DescHeight, name.Length > 0 ? L.T("HERO DESCRIPTION") + " · " + name.ToUpperInvariant() : L.T("HERO DESCRIPTION"));
        int lx = left + 3, width = total - 6;
        bool locked = desc.ReadOnly;
        int rows = locked ? DescHeight - 2 : DescHeight - 4;
        bool editing = _focus == DescriptionField && _editing;
        _descRect = (top + 1, lx, width, rows);
        // Text: word wrap, cursor (highlighted cell), scroll - wheel, ↑↓, PgUp/PgDn; stripe on the right.
        if (desc.Value.Length == 0 && !editing)
            Put(top + 1, lx, Fit(L.T("appearance, personality, backstory — in your own words or \"INVENT\" from a request"), width), Dim);
        else
        {
            var view = TextArea.View(desc.Value, _descCursor, ref _descScroll, width, rows, editing && _descFollow,
                out int curRow, out int curCol, out int totalLines);
            for (int i = 0; i < view.Count; i++) Put(top + 1 + i, lx, view[i], editing ? Bright : Fg);
            if (editing && curRow >= 0)
            {
                string line = view[curRow];
                Put(top + 1 + curRow, lx + curCol, curCol < line.Length ? line[curCol].ToString() : " ", _display.MainBackground, bg: Bright);
            }
            if (TextArea.Bar(rows, totalLines, _descScroll) is { } bar)
                for (int i = 0; i < rows; i++) Put(top + 1 + i, left + total - 2, bar[i] ? "┃" : "│", bar[i] ? Bright : Dim);
        }
        if (!locked)
            for (int i = 0; i < rows; i++) _fieldRows.Add((top + 1 + i, DescriptionField));
        _aiButton = null;
        if (locked) return;

        int sep = top + DescHeight - 3, iy = sep + 1;
        Put(sep, left, "├" + new string('─', total - 2) + "┤", MouseUiHelper.FrameColor(_display));
        if (_describeTask != null)
        {
            // The AI thinks - the entire line is expected, without “Enter:” and a button.
            Put(iy, lx, Fit(Spinner.Frames[_frame % Spinner.Frames.Length] + " " + L.T("The AI is writing the hero description…"), width), Bright);
            return;
        }
        string goText = desc.Value.Trim().Length > 0 ? L.T("CHANGE") : L.T("INVENT");
        int gx = left + total - 3 - (goText.Length + 4);
        bool promptFocus = _focus == PromptField;
        string inputLabel = L.T("Input: ");
        Put(iy, lx, inputLabel, promptFocus ? Bright : Fg);
        int px = lx + inputLabel.Length, promptW = gx - 2 - px;
        var prompt = _fields[PromptField].Value;
        bool empty = prompt.Length == 0 && !(promptFocus && _editing);
        string text = empty ? L.T("what to invent or how to change the description (optional)") : TailFit(prompt + (promptFocus && _editing ? "▌" : ""), promptW);
        Put(iy, px, Fit(text, promptW), empty ? Dim : promptFocus ? Bright : Fg);
        _fieldRows.Add((iy, PromptField));
        int w = Chip(iy, gx, goText, _focus == AiField || _aiHovered, false);
        _aiButton = (iy, gx, w);
    }

    // One field of the questionnaire; returns how many rows it took.
    private int DrawField(int index, int y, int left, int valueWidth)
    {
        var f = _fields[index];
        bool focused = index == _focus;
        int lx = left + LabelCol, vx = left + ValueCol;
        // The row of buttons does not have a label - its focus is shown by the arrows next to the button itself.
        if (f.Kind != Kind.Actions)
            Put(y, lx - 2, focused ? "→ " + f.Label : "  " + f.Label, focused ? Bright : Fg);

        int lines = 1;
        switch (f.Kind)
        {
            case Kind.Text:
            {
                bool editing = focused && _editing;
                bool multi = f.MaxLength > 100;
                if (f.Value.Length == 0 && !editing) { Put(y, vx, "—", Dim); break; }
                // Wrap by characters to the field width (not by words): when entering, the text does not jump,
                // but moves to a new line only when the current one is full.
                string text = f.Value + (editing ? "▌" : "");
                var wrapped = multi ? Chunk(text, valueWidth) : [text];
                if (wrapped.Count == 0) wrapped.Add(editing ? "▌" : "");
                int max = multi ? MultiLines : 1;
                // When entering - the last lines (you can see what is being printed), otherwise - the first ones with "...".
                var shown = editing ? wrapped.Skip(Math.Max(0, wrapped.Count - max)).ToList() : wrapped.Take(max).ToList();
                if (!editing && wrapped.Count > max) shown[^1] = Fit(shown[^1] + "…", valueWidth);
                for (int i = 0; i < shown.Count; i++) Put(y + i, vx, Fit(shown[i], valueWidth), editing ? Bright : Fg);
                // Height - by text: a new line appears only when the current one is full (up to 3).
                lines = shown.Count;
                break;
            }

            case Kind.Color:
            {
                // Palette with samples in a row, under the selected one - “▲” and the name: nothing jumps, ←→
                // move the arrow, the sample can be selected by clicking. Patterns - the background of two
                // characters across a gap; the selected one is not marked - it is visible in the hero’s preview
                // (portrait and name in color).
                for (int i = 0; i < Palette.Length; i++)
                {
                    int sx = vx + i * 3;
                    Put(y, sx, "  ", Fg, bg: Palette[i].Rgb);
                    if (!f.ReadOnly) _swatches.Add((y, sx));
                }
                break;
            }

            case Kind.Actions:
            {
                if (f.ReadOnly) break;   // ready-made hero - the portrait does not change The buttons under the portrait are like menu
                // items: the selected “→ ... ←” is under the mouse, and without the mouse - selected with
                // arrows (with focus on the line).
                int ax = vx - 2;
                for (int i = 0; i < PortraitActions.Length; i++)
                {
                    bool sel = _actionHovered >= 0 ? i == _actionHovered : focused && i == _action;
                    int w = Chip(y, ax, PortraitActions[i], sel, false);
                    _actionPos.Add((y, ax, w));
                    ax += w + 2;
                }
                break;
            }

            case Kind.Choice:
            {
                // Fixed-width field: arrows do not jump when scrolling, the value is in the center.
                int inner = valueWidth - 4, textW = inner;
                // HERO - “which of how many” is muted at the right arrow (like WORLD at the “World” step).
                if (index == HeroField)
                {
                    string counter = $"{f.Index + 1}/{f.Options.Count}";
                    Put(y, vx + 2 + inner - counter.Length, counter, Dim);
                    textW -= counter.Length + 1;
                }
                string text = f.Options.Count == 0 ? "—" : Fit(f.Options[f.Index], textW);
                Put(y, vx + 2 + (textW - text.Length) / 2, text, focused ? Bright : Fg);
                _choiceArrows.Remove(index);
                if (f.ReadOnly) break;   // ready-made hero - without arrows The arrows are always active (you can scroll with the mouse
                // without focus), they are brighter under the mouse.
                Put(y, vx, "◄", focused || _arrowHovered == (index, -1) ? Bright : Fg);
                Put(y, vx + 2 + inner + 1, "►", focused || _arrowHovered == (index, 1) ? Bright : Fg);
                _choiceArrows[index] = (y, vx, inner + 3);
                break;
            }

            case Kind.Portrait:
            {
                // The title is in a fixed-width field: “◄” and “►” remain in place when scrolling.
                var p = _portraits[_portrait];
                const int titleWidth = 16; // + counter “select... 1/35” on the right - do not overlap
                string title = Fit(p.Title, titleWidth);
                int rx = vx + 2 + titleWidth + 1;
                // The finished hero is a portrait of him, without arrows and counter.
                if (f.ReadOnly)
                {
                    Put(y, vx + 2 + (titleWidth - title.Length) / 2, title, Fg);
                    break;
                }
                // Arrows are always active (you can scroll with the mouse without focusing on the field).
                Put(y, vx, "◄", focused ? Bright : Fg);
                Put(y, vx + 2 + (titleWidth - title.Length) / 2, title, focused ? Bright : Fg);
                Put(y, rx, "►", focused ? Bright : Fg);
                _arrows = (y, vx, rx);
                PutRight(y, left + FormWidth - 3, $"{_portrait + 1}/{_portraits.Count}", Dim);
                break;
            }
        }
        for (int i = 0; i < lines; i++) _fieldRows.Add((y + i, index));
        return lines;
    }

    // Frame “HERO”: a portrait in the hero’s color, under it a name, a symbol as on the map and where the portrait is from.
    private void DrawPreview(int y, int left)
    {
        var color = Palette[_color].Rgb;
        var p = _portraits[_portrait];
        int inner = PreviewWidth - 2;
        int artX = left + 1 + (inner - PortraitWidth) / 2;
        var art = string.IsNullOrEmpty(p.Image) ? null : SvgToBrailleConverter.Convert(p.Image, "", PortraitWidth);
        int artRows = PortraitWidth / 2;
        if (art != null)
            for (int i = 0; i < art.Length && i < artRows; i++) Put(y + 1 + i, artX, art[i], color, skipBlank: true);
        else
            CenterIn(y + artRows / 2, left, PreviewWidth, string.IsNullOrEmpty(p.Image) ? L.T("Enter — choose a file") : L.T("no image"), Dim);

        int ty = y + 1 + artRows;
        string name = _fields[NameField].Value.Trim();
        CenterIn(ty, left, PreviewWidth, name.Length == 0 ? L.T("HERO NAME") : Fit(name, inner - 2), name.Length == 0 ? Dim : color);

        // The symbol is like the hero’s cell on the map (MapObjectsProvider: background of the room, paler, with green).
        string symbol = _fields[SymbolField].Value.Trim();
        string onMap = L.T("ON MAP  ???");
        int sx = CenterIn(ty + 1, left, PreviewWidth, onMap, Dim);
        var cellBg = ColorHelper.MixWith(ColorHelper.Pale([55, 55, 65]), [0, 200, 80], 0.18);
        Put(ty + 1, sx + onMap.Length - 3, symbol.Length == 0 ? "???" : symbol.PadRight(3)[..3], ColorHelper.Saturate(color), bg: cellBg);
    }

    // The frame is in the color of the game frames, the title is in the break in the top line: “╭─ QUESTIONNAIRE ──…──╮.”
    private void DrawBox(int y, int x, int w, int h, string title)
    {
        var line = MouseUiHelper.FrameColor(_display);   // block frames - muted, like in the “World” step
        Put(y, x, "╭─ ", line);
        Put(y, x + 3, title, Bright);
        Put(y, x + 3 + title.Length, " " + new string('─', Math.Max(0, w - title.Length - 5)) + "╮", line);
        for (int r = 1; r < h - 1; r++)
        {
            Put(y + r, x, "│", line);
            Put(y + r, x + w - 1, "│", line);
        }
        Put(y + h - 1, x, "╰" + new string('─', w - 2) + "╯", line);
    }

    // The “TEXT” button on the fill (as in the “World” step): selected - the fill is brighter, unavailable -
    // dimmed. Returns the width.
    private int Chip(int y, int x, string text, bool on, bool disabled)
    {
        string t = $"  {text}  ";
        var bg = ColorHelper.MixWith(_display.MainBackground, _display.MainForeground, on ? 0.42 : 0.16);
        Put(y, x, t, disabled ? Dim : on ? ColorHelper.Pale(_display.MainForeground, 0.85) : Fg, bg: bg);
        return t.Length;
    }

    private int CenterIn(int row, int left, int width, string text, List<int> color)
    {
        int x = left + Math.Max(0, (width - text.Length) / 2);
        Put(row, x, text, color);
        return x;
    }

    private void PutRight(int row, int right, string text, List<int> color) => Put(row, right - text.Length + 1, text, color);

    private static List<string> Chunk(string text, int width)
    {
        var lines = new List<string>();
        for (int i = 0; i < text.Length; i += width) lines.Add(text.Substring(i, Math.Min(width, text.Length - i)));
        return lines;
    }

    private static string Fit(string text, int width) =>
        text.Length <= width ? text : text[..Math.Max(0, width - 1)] + "…";

    private static string TailFit(string text, int width) =>
        text.Length <= width ? text : "…" + text[^(width - 1)..];

    private void Put(int row, int x, string text, List<int> color, bool skipBlank = false, List<int>? bg = null)
    {
        if (row < 0 || row >= _height) return;
        var cells = PixelArt.Colors(text);   // color sprite - in its own colors, transparent cells are skipped
        for (int i = 0; i < text.Length && x + i < _width; i++)
        {
            if (x + i < 0) continue;
            if (skipBlank && (text[i] == '⠀' || cells != null && cells[i].Fg == null)) continue;
            _chars[row, x + i] = text[i];
            _fg[row, x + i] = cells != null ? cells[i].Fg : color;
            _bg[row, x + i] = cells != null ? cells[i].Bg ?? bg : bg;
        }
    }

    private void WriteRow(int row)
    {
        Term.SetCursorPosition(_x0, _bodyTop + row);
        int c = 0;
        while (c < _width)
        {
            var fg = _fg[row, c];
            var bg = _bg[row, c];
            int start = c;
            while (c < _width && ReferenceEquals(_fg[row, c], fg) && ReferenceEquals(_bg[row, c], bg)) c++;
            var run = new string(Enumerable.Range(start, c - start).Select(i => _chars[row, i]).ToArray());
            ColorHelper.WriteColored(run, fgColor: fg ?? _display.MainForeground, bgColor: bg ?? _display.MainBackground);
        }
    }
}
