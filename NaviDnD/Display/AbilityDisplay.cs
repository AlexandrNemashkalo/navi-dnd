using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.Helpers;

namespace NaviDnD.Display;

public class AbilityDisplay
{
    private readonly WorldState _settings;
    private readonly DisplayConfig _display;

    public AbilityDisplay(WorldState settings, DisplayConfig display)
    {
        _settings = settings;
        _display = display;
    }

    public void Draw()
    {
        var startTop = Console.CursorTop;
        var borderDrawer = new BorderDrawer(_settings, _display);
        borderDrawer.DrawSeparator();
        borderDrawer.DrawTitleLine(L.T("СПУТНИК"));
        borderDrawer.DrawSeparator();

        int currentHeight = Console.CursorTop - startTop;
        _display.AbilityHigh = currentHeight;

        int needed = _display.MaxHigh - currentHeight;
        for (int i = 0; i < needed; i++)
            borderDrawer.DrawContentLine(() => { });

        borderDrawer.DrawSeparator();
    }

    private (List<Action> lines, List<int> groupSizes) BuildAbilityLines(List<HeroAbility> abilities, int innerWidth, BorderDrawer borderDrawer)
    {
        var lines = new List<Action>();
        var groupSizes = new List<int>();

        foreach (var ability in abilities)
        {
            string name = ability.Name;
            string description = ability.Description ?? string.Empty;
            string marker = "• ";
            string separator = ": ";

            int prefixLength = marker.Length + name.Length + separator.Length;
            var descLines = TextWrapper.WrapText(description, innerWidth, innerWidth - prefixLength);
            int abilityLineCount = Math.Max(1, descLines.Count);
            groupSizes.Add(abilityLineCount);

            lines.Add(() =>
                borderDrawer.DrawContentLine(() =>
                {
                    Console.Write(marker);
                    var abilityColor = ability.Color ?? new List<int> { 220, 200, 100 };
                    ColorHelper.WriteColored(name, abilityColor);
                    Console.Write(separator);
                    if (descLines.Count > 0)
                        Console.Write(descLines[0]);
                }));

            for (int i = 1; i < descLines.Count; i++)
            {
                int lineIndex = i;  // Capture current value to avoid closure bug
                lines.Add(() =>
                    borderDrawer.DrawContentLine(() =>
                    {
                        Console.Write(descLines[lineIndex]);
                    }));
            }
        }

        return (lines, groupSizes);
    }

    private static List<(int lineStart, int lineCount)> CalculatePages(List<int> groupSizes, int pageSize)
    {
        var pages = new List<(int, int)>();
        int lineStart = 0, i = 0;
        while (i < groupSizes.Count)
        {
            int pageLines = 0, pageStart = lineStart;
            while (i < groupSizes.Count && pageLines + groupSizes[i] <= pageSize)
            {
                pageLines += groupSizes[i]; lineStart += groupSizes[i]; i++;
            }
            if (pageLines == 0) // entry too large — force include alone
            {
                pageLines = groupSizes[i]; lineStart += groupSizes[i]; i++;
            }
            pages.Add((pageStart, pageLines));
        }
        return pages;
    }

}
