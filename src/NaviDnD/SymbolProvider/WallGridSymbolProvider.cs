namespace NaviDnD;

public class WallGridSymbolProvider : IGridSymbolProvider
{
    public char Vertical => '\u2551';
    public char Horizontal => '\u2550';

    public char GetCorner(string cornerCode)
    {
        // Only actual walls contribute arms; cell-grid lines never notch a wall.
        int mask = 0;
        for (int i = 0; i < 4; i++) if (cornerCode[i] == '2') mask |= 1 << i;
        return mask switch
        {
            6 => '\u2554', 12 => '\u2557', 3 => '\u255a', 9 => '\u255d',
            14 => '\u2566', 11 => '\u2569', 7 => '\u2560', 13 => '\u2563',
            15 => '\u256c', 1 or 4 or 5 => '\u2551',
            2 or 8 or 10 => '\u2550', _ => ' '
        };
    }
}
