namespace NaviDnD.Tests;

// Подсветка текста мастера: урон, лечение, исход броска и имена — в русской и английской игре.
public class DialogHighlighterTests
{
    private static readonly List<int> Mech = [1, 1, 1], Bad = [2, 2, 2], Good = [3, 3, 3], Goblin = [4, 4, 4];

    private static List<int>? ColorOf(string line, string word, params (string name, List<int> color)[] names)
    {
        var h = new DialogHighlighter(names, new DialogHighlighter.Palette(Mech, Bad, Good));
        int depth = 0;
        int at = line.IndexOf(word, StringComparison.Ordinal);
        int pos = 0;
        foreach (var (text, color) in h.Segments(line, ref depth))
        {
            if (at >= pos && at < pos + text.Length) return color;
            pos += text.Length;
        }
        return null;
    }

    [Theory]
    [InlineData("Клинок наносит 7 колющего урона.", "7 колющего")]
    [InlineData("The blade deals 7 piercing damage.", "7 piercing")]
    [InlineData("You take damage 1d6+2=5.", "damage 1d6")]
    public void DamageIsBad(string line, string word) => Assert.Equal(Bad, ColorOf(line, word));

    [Theory]
    [InlineData("Лечение 2к4=5.", "Лечение")]
    [InlineData("You regain 8 hit points.", "regain 8")]
    [InlineData("Healing 1d8+3 restores you.", "Healing")]
    public void HealingIsGood(string line, string word) => Assert.Equal(Good, ColorOf(line, word));

    [Theory]
    [InlineData("Попадание!", "Попадание", true)]
    [InlineData("A hit!", "hit", true)]
    [InlineData("Success.", "Success", true)]
    [InlineData("Промах.", "Промах", false)]
    [InlineData("The arrow misses.", "misses", false)]
    [InlineData("Saving throw failed.", "failed", false)]
    public void RollOutcomeIsColored(string line, string word, bool good) => Assert.Equal(good ? Good : Bad, ColorOf(line, word));

    [Fact]
    public void HitPointsAndMissionAreNotOutcomes()
    {
        Assert.Null(ColorOf("You have 12 hit points left.", "hit"));
        Assert.Null(ColorOf("A new mission awaits.", "mission"));
    }

    [Theory]
    [InlineData("The Goblins attack.", "Goblins")]
    [InlineData("The goblin's blade glints.", "goblin")]
    [InlineData("Гоблину не уйти.", "Гоблину")]
    public void CreatureNamesInBothLanguages(string line, string word) =>
        Assert.Equal(Goblin, ColorOf(line, word, ("Goblin", Goblin), ("Гоблин", Goblin)));
}
