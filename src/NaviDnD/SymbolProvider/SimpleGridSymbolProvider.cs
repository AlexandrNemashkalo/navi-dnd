namespace NaviDnD;

public class SimpleGridSymbolProvider : IGridSymbolProvider
{

    public char Vertical => ' '; //'│';

    public char Horizontal => ' '; //'─';

    private Dictionary<string, char> _symbols = new Dictionary<string, char>()
    {
        ["0110"] = '┌',
        ["0011"] = '┐',
        ["1100"] = '└',
        ["1001"] = '┘',
        ["0111"] = '┬',
        ["1101"] = '┴',
        ["1110"] = '├',
        ["1011"] = '┤',
        ["1111"] = '+', //'┼'

    };

    public char GetCorner(string cornerCode)
    {
        return _symbols[cornerCode];
    }
}


//            Console.WriteLine("❤ "); 
//            Console.WriteLine("☀ ");
//            Console.WriteLine("☹ ");
//            Console.WriteLine("⚪");
//            Console.WriteLine("⛏ ");
//            Console.WriteLine("☠ ");
//            Console.WriteLine("☸ ");
//            Console.WriteLine("⚓ ");
//            Console.WriteLine("⚡ ");
//            Console.WriteLine("⚰ ");
//            Console.WriteLine("⚱ ");
//            Console.WriteLine("✅ ");
//            Console.WriteLine("✨ ");
//            Console.WriteLine("❎ ");
//            Console.WriteLine("❓ ");
//            Console.WriteLine("❗ ");
//            Console.WriteLine("⚔️ ");
//            Console.WriteLine("⚒️ ");
//            Console.WriteLine("❄️ ");
//            Console.WriteLine("☑ ☘ ☝ ♨  ⚐ ⚑ ⚗ ⚙ ⚜ ⚠ ⚫ ⛓ ⛔ ⛩ ⛪ ✏ ✒ ✔ ✖ ");