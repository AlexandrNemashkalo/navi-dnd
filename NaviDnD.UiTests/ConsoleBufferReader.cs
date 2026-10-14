using System.Runtime.InteropServices;

namespace NaviDnD.UiTests;

// Reads the real console buffer via Windows Console API - so the test sees exactly what the player would see,
// and not what the program thinks it wrote.
internal static class ConsoleBufferReader
{
    [StructLayout(LayoutKind.Sequential)]
    private struct COORD
    {
        public short X;
        public short Y;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ReadConsoleOutputCharacterW(
        IntPtr hConsoleOutput, [Out] char[] lpCharacter, uint nLength,
        COORD dwReadCoord, out uint lpNumberOfCharsRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadConsoleOutputAttribute(
        IntPtr hConsoleOutput, [Out] ushort[] lpAttribute, uint nLength,
        COORD dwReadCoord, out uint lpNumberOfAttrsRead);

    // Reads length characters from line row of the console buffer, starting at column 0.
    public static string ReadRow(int row, int length)
    {
        using var handle = ConsoleHandles.OpenOutput();
        var buffer = new char[length];
        var coord = new COORD { X = 0, Y = (short)row };

        if (!ReadConsoleOutputCharacterW(handle.DangerousGetHandle(), buffer, (uint)length, coord, out uint read))
            throw new InvalidOperationException("Не удалось прочитать буфер консоли.");

        return new string(buffer, 0, (int)read);
    }

    // Reads the color attributes of a string (legacy 16-color console palette). The game colors the text with
    // 24-bit ANSI - legacy attributes may not reflect the exact RGB, but you can tell the difference between
    // “colored” and “not colored” (or between different colorings).
    public static ushort[] ReadRowAttributes(int row, int length)
    {
        using var handle = ConsoleHandles.OpenOutput();
        var buffer = new ushort[length];
        var coord = new COORD { X = 0, Y = (short)row };

        if (!ReadConsoleOutputAttribute(handle.DangerousGetHandle(), buffer, (uint)length, coord, out uint read))
            throw new InvalidOperationException("Не удалось прочитать цветовые атрибуты буфера консоли.");

        return buffer;
    }
}
