namespace NaviDnD.Display;

public static class DiceArt
{
    private static readonly char[] _noiseBraille = [.. Enumerable.Range(0x2801, 0xFF).Select(i => (char)i)];

    public static string[] GetD20Lines(int number = 20) =>
        D20.Replace("20", number.ToString().PadLeft(2)).Split('\n');

    public static string[] GetD20RollLines(int number = 20) =>
        GetD20Lines(number);

    public static string[] GetNoisyD20RollLines(int number, float intensity, Random rng) =>
        GetNoisyD20Lines(number, intensity, rng);

    public static string[] GetD20PairLines(int roll1, int roll2)
    {
        string text = D20Pair;
        int first = text.IndexOf("20");
        if (first >= 0)
        {
            text = text[..first] + roll1.ToString().PadLeft(2) + text[(first + 2)..];
            int second = text.IndexOf("20", first + 2);
            if (second >= 0)
                text = text[..second] + roll2.ToString().PadLeft(2) + text[(second + 2)..];
        }
        return text.Split('\n');
    }

    public static string[] GetNoisyD20PairLines(int roll1, int roll2, float intensity, Random rng)
    {
        var source = GetD20PairLines(roll1, roll2);
        var result = new string[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            var sb = new System.Text.StringBuilder(source[i].Length);
            foreach (char c in source[i])
            {
                if (c is > '⠀' and <= '⣿')
                    sb.Append(rng.NextDouble() < intensity ? _noiseBraille[rng.Next(_noiseBraille.Length)] : c);
                else
                    sb.Append(c);
            }
            result[i] = sb.ToString();
        }
        return result;
    }

    public static string[] GetNoisyD20Lines(int number, float intensity, Random rng)
    {
        var source = GetD20Lines(number);
        var result = new string[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            var sb = new System.Text.StringBuilder(source[i].Length);
            foreach (char c in source[i])
            {
                if (c is > '⠀' and <= '⣿')
                {
                    sb.Append(rng.NextDouble() < intensity
                        ? _noiseBraille[rng.Next(_noiseBraille.Length)]
                        : c);
                }
                else
                    sb.Append(c);
            }
            result[i] = sb.ToString();
        }
        return result;
    }


    public static readonly string D20 =
"""
⠀⠀⠀⠀⠀⠀⠀⣀⢤⢤⣄⠀⠀⠀⠀⠀⠀⠀
⠀⠀⠀⢀⡠⠖⠋⡴⠃⠈⠳⡉⠒⠤⣀⠀⠀⠀
⠀⢰⢮⡁⠀⢀⠞⠀⠀⠀⠀⠙⣄⠀⢀⡹⢶⠀
⠀⢸⠀⠈⣹⢟⠒⠒⠒⠒⠒⠒⢺⢮⠉⠀⢸⠀
⠀⢸⠀⡰⠃⠈⢧⠀20⠀⣰⠃⠀⢣⠀⢸⠀
⠀⢸⡴⠁⠀⠀⠀⢣⡀⠀⡰⠁⠀⠀⠀⢳⣸⠀
⠀⠘⠷⣤⠤⠤⢤⣀⣱⣼⣁⡤⠤⠤⢤⡶⠟⠀
⠀⠀⠀⠈⠙⠲⢤⣀⠀⡇⢀⡠⠔⠊⠁⠀⠀⠀
⠀⠀⠀⠀⠀⠀⠀⠈⠑⠛⠉⠀⠀⠀⠀⠀⠀⠀
""";

    public static readonly string D20Pair =
"""
⠀⠀⠀⠀⠀⠀⠀⣀⢤⢤⣄⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⣀⢤⢤⣄⠀⠀⠀⠀⠀⠀⠀
⠀⠀⠀⢀⡠⠖⠋⡴⠃⠈⠳⡉⠒⠤⣀⠀⠀⠀⠀⠀⠀⢀⡠⠖⠋⡴⠃⠈⠳⡉⠒⠤⣀⠀⠀⠀
⠀⢰⢮⡁⠀⢀⠞⠀⠀⠀⠀⠙⣄⠀⢀⡹⢶⠀⠀⢰⢮⡁⠀⢀⠞⠀⠀⠀⠀⠙⣄⠀⢀⡹⢶⠀
⠀⢸⠀⠈⣹⢟⠒⠒⠒⠒⠒⠒⢺⢮⠉⠀⢸⠀⠀⢸⠀⠈⣹⢟⠒⠒⠒⠒⠒⠒⢺⢮⠉⠀⢸⠀
⠀⢸⠀⡰⠃⠈⢧⠀20⠀⣰⠃⠀⢣⠀⢸⠀⠀⢸⠀⡰⠃⠈⢧⠀20⠀⣰⠃⠀⢣⠀⢸⠀
⠀⢸⡴⠁⠀⠀⠀⢣⡀⠀⡰⠁⠀⠀⠀⢳⣸⠀⠀⢸⡴⠁⠀⠀⠀⢣⡀⠀⡰⠁⠀⠀⠀⢳⣸⠀
⠀⠘⠷⣤⠤⠤⢤⣀⣱⣼⣁⡤⠤⠤⢤⡶⠟⠀⠀⠘⠷⣤⠤⠤⢤⣀⣱⣼⣁⡤⠤⠤⢤⡶⠟⠀
⠀⠀⠀⠈⠙⠲⢤⣀⠀⡇⢀⡠⠔⠊⠁⠀⠀⠀⠀⠀⠀⠈⠙⠲⢤⣀⠀⡇⢀⡠⠔⠊⠁⠀⠀⠀
⠀⠀⠀⠀⠀⠀⠀⠈⠑⠛⠉⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠈⠑⠛⠉⠀⠀⠀⠀⠀⠀⠀
""";

}