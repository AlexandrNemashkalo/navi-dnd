using NaviDnD.Data;
using NaviDnD.Helpers;

namespace NaviDnD.Display;

public class BorderDrawer
{
    private readonly List<int> _mainFg;
    private readonly List<int> _frameFg;   // линии рамок — приглушённо (MouseUiHelper.FrameColor), текст — основным цветом
    private readonly List<int> _mainBg;
    private readonly int _innerWidth;

    private const char TopLeft = '╭';
    private const char TopRight = '╮';
    private const char BottomLeft = '╰';
    private const char BottomRight = '╯';
    private const char Horizontal = '─';
    private const char Vertical = '│';
    private const char SeparatorLeft = '├';
    private const char SeparatorRight = '┤';
    private const char SeparatorTop = '┬';
    private const char SeparatorBottom = '┴';

    public BorderDrawer(WorldState worldState, DisplayConfig display)
    {
        _mainFg = display.MainForeground;
        _frameFg = MouseUiHelper.FrameColor(display);
        _mainBg = display.MainBackground;
        _innerWidth = display.InnerWidth(display.ViewCols(worldState.Map));
        MouseUiHelper.TitleWidth = _innerWidth;
    }

    private void Write(string text)
    {
        ColorHelper.WriteColored(text, fgColor: _frameFg, bgColor: _mainBg);
    }

    private void Write(char ch) => Write(ch.ToString());

    // Симметрия с обязательным столбцом-предохранителем справа (см. ConsoleSetup.SetConsoleConfig —
    // без него автоперенос на последней колонке буфера ломает SafeNewLine) — тот же отступ слева,
    // чисто визуально, перед самой рамкой.
    private void WriteLeftMargin() => Write(new string(' ', DisplayConfig.LeftMargin));

    public void DrawSeparatorWithMiddle(char middle)
    {
        int half = _innerWidth / 2;
        int leftLen = half;
        int rightLen = _innerWidth - half - 1;

        WriteLeftMargin();
        Write(SeparatorLeft);
        Write(new string(Horizontal, leftLen));
        Write(middle.ToString());
        Write(new string(Horizontal, rightLen));
        Write(SeparatorRight);
        FillRestWithMain();
        SafeNewLine();
    }

    public void DrawSeparatorTop() => DrawSeparatorWith3Parts(SeparatorTop);
    public void DrawSeparatorBottom() => DrawSeparatorWith3Parts(SeparatorBottom);

    public void DrawSeparatorWith2Parts(char dividerChar, int leftWidth, int rightWidth)
    {
        WriteLeftMargin();
        Write(SeparatorLeft);
        Write(new string(Horizontal, leftWidth));
        Write(dividerChar);
        Write(new string(Horizontal, rightWidth));
        Write(SeparatorRight);
        FillRestWithMain();
        SafeNewLine();
    }

    private void DrawSeparatorWith3Parts(char middle)
    {
        int colWidth = (_innerWidth - 2) / 3;
        int leftWidth = colWidth;
        int middleWidth = colWidth;
        int rightWidth = _innerWidth - 2 - leftWidth - middleWidth;

        WriteLeftMargin();
        Write(SeparatorLeft);
        Write(new string(Horizontal, leftWidth));
        Write(middle.ToString());
        Write(new string(Horizontal, middleWidth));
        Write(middle.ToString());
        Write(new string(Horizontal, rightWidth));
        Write(SeparatorRight);
        FillRestWithMain();
        SafeNewLine();
    }

    public void DrawTopBorder()
    {
        WriteLeftMargin();
        Write(TopLeft);
        Write(new string(Horizontal, _innerWidth));
        Write(TopRight);
        FillRestWithMain();
        SafeNewLine();
    }

    public void DrawBottomBorder()
    {
        WriteLeftMargin();
        Write(BottomLeft);
        Write(new string(Horizontal, _innerWidth));
        Write(BottomRight);
        FillRestWithMain();
        SafeNewLine();
    }

    public void DrawTitleLine(string title)
    {
        WriteLeftMargin();
        Write(Vertical);
        int padding = (_innerWidth - title.Length) / 2;
        Write(new string(' ', padding));
        ColorHelper.WriteColored(title, fgColor: _mainFg, bgColor: _mainBg);
        Write(new string(' ', _innerWidth - padding - title.Length));
        Write(Vertical);
        FillRestWithMain();
        SafeNewLine();
    }

    public void DrawTitleLine(Action drawContent, int contentLength)
    {
        WriteLeftMargin();
        Write(Vertical);
        int padding = (_innerWidth - contentLength) / 2;
        Write(new string(' ', padding));
        drawContent();
        Write(new string(' ', _innerWidth - padding - contentLength));
        Write(Vertical);
        FillRestWithMain();
        SafeNewLine();
    }

    public void DrawSeparator()
    {
        WriteLeftMargin();
        Write(SeparatorLeft);
        Write(new string(Horizontal, _innerWidth));
        Write(SeparatorRight);
        FillRestWithMain();
        SafeNewLine();
    }

    public void DrawContentLine(Action drawContent)
    {
        WriteLeftMargin();
        int lineStart = Console.CursorLeft;
        Write(Vertical);
        drawContent();
        int filled = Console.CursorLeft - lineStart - 1;
        int remaining = _innerWidth - filled;
        if (remaining > 0)
            Write(new string(' ', remaining));
        Write(Vertical);
        FillRestWithMain();
        SafeNewLine();
    }

    public void DrawContentLine2Columns(Action drawLeftContent, Action drawRightContent, int leftWidth, int rightWidth)
    {
        WriteLeftMargin();
        int lineStart = Console.CursorLeft;
        Write(Vertical);

        // Left column
        drawLeftContent();
        int filledLeft = Console.CursorLeft - lineStart - 1;
        int remainingLeft = leftWidth - filledLeft;
        if (remainingLeft > 0)
            Write(new string(' ', remainingLeft));

        // Divider
        Write(Vertical);

        // Right column
        int rightStart = Console.CursorLeft;
        drawRightContent();
        int filledRight = Console.CursorLeft - rightStart;
        int remainingRight = rightWidth - filledRight;
        if (remainingRight > 0)
            Write(new string(' ', remainingRight));

        // Right border
        Write(Vertical);
        FillRestWithMain();
        SafeNewLine();
    }

    public void DrawSeparatorWith4Parts(char dividerChar, int w1, int w2, int w3, int w4)
    {
        WriteLeftMargin();
        Write(SeparatorLeft);
        Write(new string(Horizontal, w1));
        Write(dividerChar);
        Write(new string(Horizontal, w2));
        Write(dividerChar);
        Write(new string(Horizontal, w3));
        Write(dividerChar);
        Write(new string(Horizontal, w4));
        Write(SeparatorRight);
        FillRestWithMain();
        SafeNewLine();
    }

    // Внутренний разделитель колонок (под заголовками): крестики ┼ на пересечениях, линия во всю ширину,
    // но без стыков ├ ┤ с боковой рамкой — рамка остаётся │.
    public void DrawInnerSeparatorWith4Parts(int w1, int w2, int w3, int w4)
    {
        WriteLeftMargin();
        Write(Vertical);
        Write(new string(Horizontal, w1));
        Write('┼');
        Write(new string(Horizontal, w2));
        Write('┼');
        Write(new string(Horizontal, w3));
        Write('┼');
        Write(new string(Horizontal, w4));
        Write(Vertical);
        FillRestWithMain();
        SafeNewLine();
    }

    public void DrawContentLine4Columns(Action drawCol1, Action drawCol2, Action drawCol3, Action drawCol4,
        int w1, int w2, int w3, int w4)
    {
        WriteLeftMargin();
        int lineStart = Console.CursorLeft;
        Write(Vertical);

        drawCol1();
        int filled1 = Console.CursorLeft - lineStart - 1;
        int remaining1 = w1 - filled1;
        if (remaining1 > 0) Write(new string(' ', remaining1));
        Write(Vertical);

        int start2 = Console.CursorLeft;
        drawCol2();
        int remaining2 = w2 - (Console.CursorLeft - start2);
        if (remaining2 > 0) Write(new string(' ', remaining2));
        Write(Vertical);

        int start3 = Console.CursorLeft;
        drawCol3();
        int remaining3 = w3 - (Console.CursorLeft - start3);
        if (remaining3 > 0) Write(new string(' ', remaining3));
        Write(Vertical);

        int start4 = Console.CursorLeft;
        drawCol4();
        int remaining4 = w4 - (Console.CursorLeft - start4);
        if (remaining4 > 0) Write(new string(' ', remaining4));
        Write(Vertical);

        FillRestWithMain();
        SafeNewLine();
    }

    // junction — позиция в правой части, куда сверху приходит вертикальная линия (┴), или null.
    public void DrawContentLine2ColumnsRightSeparator(Action drawLeftContent, int leftWidth, int rightWidth, int? junction = null, char junctionChar = SeparatorBottom)
    {
        WriteLeftMargin();
        int lineStart = Console.CursorLeft;
        Write(Vertical);
        drawLeftContent();
        int filledLeft = Console.CursorLeft - lineStart - 1;
        int remainingLeft = leftWidth - filledLeft;
        if (remainingLeft > 0)
            Write(new string(' ', remainingLeft));
        Write(SeparatorLeft);
        Write(SeparatorLine(rightWidth, junction, junctionChar));
        Write(SeparatorRight);
        FillRestWithMain();
        SafeNewLine();
    }

    public void DrawBottomBorder2Columns(int leftWidth, int rightWidth)
    {
        WriteLeftMargin();
        Write(BottomLeft);
        Write(new string(Horizontal, leftWidth));
        Write(SeparatorBottom);  // ┴
        Write(new string(Horizontal, rightWidth));
        Write(BottomRight);
        FillRestWithMain();
        SafeNewLine();
    }

    // junctionChar: ┴ — вертикальная линия приходит сверху, ┬ — уходит вниз.
    public static string SeparatorLine(int width, int? junction, char junctionChar = SeparatorBottom)
    {
        var line = new string(Horizontal, width).ToCharArray();
        if (junction is int j && j >= 0 && j < width) line[j] = junctionChar;
        return new string(line);
    }

    public const char JunctionDown = SeparatorTop; // ┬

    private void FillRestWithMain()
    {
        int remaining = Console.WindowWidth - Console.CursorLeft;
        if (remaining > 0)
        {
            Write(new string(' ', remaining));
        }
    }

    private static void SafeNewLine()
    {
        if (Console.CursorTop < Console.WindowHeight - 1)
            Console.WriteLine();
    }
}
