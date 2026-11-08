using NaviDnD.Data;
using NaviDnD.Display;

namespace NaviDnD.Tests;

public class LegendVisibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DepartedCreatureIsNotSelectableEvenWhenMapIsRevealed(bool reveal)
    {
        var storage = new Storage();
        storage.WorldState.Hero = new() { Position = [2, 2] };
        storage.WorldState.Map.Entities =
        [
            new() { Name = "Sparrow", Symbol = "AAA", Position = [3, 2], Deleted = true },
            new() { Name = "Rada", Symbol = "RAD", Position = [4, 2] }
        ];
        storage.VisibleCells.UnionWith([(3, 2), (4, 2)]);
        var display = new DisplayConfig { SelectedMapItemIndex = 1 };
        var legend = new LegendDisplay(storage, display, new AppConfig { RevealMap = reveal });

        Assert.Equal(-1, legend.GetSelectableIndexForCell(3, 2));
        Assert.Equal(1, legend.GetSelectableIndexForCell(4, 2));
        Assert.Equal(2, legend.GetSelectableMapItemCount());
        Assert.Equal("Rada", legend.GetSelectedEntity()?.Name);
    }
}
