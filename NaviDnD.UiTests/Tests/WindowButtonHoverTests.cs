namespace NaviDnD.UiTests.Tests;

public class WindowButtonHoverTests
{
    [Fact]
    public void HoverPreservesWindowButtonGlyphs()
    {
        using var game = GameSession.Launch(mockedActions: ["*"]);
        string before = GameConsole.ReadRow(game.Pid, 1, GameConsole.TitleReadWidth);
        int square = before.IndexOf('\u25a2');
        Assert.True(square >= 0, before);
        string shape = GameConsole.CellShape(game.Pid, square, 1);
        int[] columns = [square - 3, square, square + 3];
        var colors = columns.Select(column => GameConsole.SampleCellColor(game.Pid, column, 1, (0, 0, 0))).ToArray();
        foreach (int column in columns)
        {
            GameConsole.SendMouseMove(game.Pid, column, 1);
            Thread.Sleep(250);
            Assert.Equal(before, GameConsole.ReadRow(game.Pid, 1, GameConsole.TitleReadWidth));
            string hoveredShape = GameConsole.CellShape(game.Pid, square, 1);
            Assert.True(shape == hoveredShape, "Before:\n" + shape + "\nAfter:\n" + hoveredShape);
            for (int i = 0; i < columns.Length; i++)
            {
                var color = GameConsole.SampleCellColor(game.Pid, columns[i], 1, (0, 0, 0));
                if (columns[i] == column) Assert.NotEqual(colors[i], color);
                else Assert.Equal(colors[i], color);
            }
        }
        GameConsole.SendMouseMove(game.Pid, square, 2);
        Thread.Sleep(250);
        Assert.Equal(before, GameConsole.ReadRow(game.Pid, 1, GameConsole.TitleReadWidth));
        Assert.Equal(shape, GameConsole.CellShape(game.Pid, square, 1));
        for (int i = 0; i < columns.Length; i++)
            Assert.Equal(colors[i], GameConsole.SampleCellColor(game.Pid, columns[i], 1, (0, 0, 0)));
    }
}

