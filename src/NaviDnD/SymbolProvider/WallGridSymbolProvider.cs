namespace NaviDnD;

public class WallGridSymbolProvider : IGridSymbolProvider
{
    private Dictionary<string, char> _symbols = new Dictionary<string, char>()
    {
        ["0220"] = '╔',
        ["0210"] = '╒',
        ["0120"] = '╓',
        ["0022"] = '╗',
        ["0021"] = '╖',
        ["0012"] = '╕',
        ["2200"] = '╚',
        ["2100"] = '╙',
        ["1200"] = '╘',
        ["2002"] = '╝',
        ["2001"] = '╜',
        ["1002"] = '╛',
        ["0222"] = '╦',
        ["0212"] = '╤',
        ["0121"] = '╥',
        ["2202"] = '╩',
        ["2101"] = '╨',
        ["1202"] = '╧',
        ["2220"] = '╠',
        ["2120"] = '╟',
        ["1210"] = '╞',
        ["2022"] = '╣',
        ["2021"] = '╢',
        ["1012"] = '╡',
        ["2222"] = '╬',
        ["2121"] = '╫',
        ["1212"] = '╪',

        ["1021"] = '╖',
        ["2011"] = '╜',
        ["0211"] = '╒',
        ["2110"] = '╙',

        ["1120"] = '╓',
        ["1102"] = '╛',
        ["1201"] = '╘',
        ["0112"] = '╕',
    };

    public char Vertical => '║';

    public char Horizontal => '═';

    public char GetCorner(string cornerCode)
    {
        return _symbols[cornerCode];
    }
}