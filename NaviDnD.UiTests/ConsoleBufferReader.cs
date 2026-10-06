using System.Runtime.InteropServices;

namespace NaviDnD.UiTests;

// Читает реальный буфер консоли через Windows Console API — так тест видит
// ровно то же, что видел бы игрок, а не то, что программа думает, что написала.
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

    // Читает length символов из строки row буфера консоли, начиная с колонки 0.
    public static string ReadRow(int row, int length)
    {
        using var handle = ConsoleHandles.OpenOutput();
        var buffer = new char[length];
        var coord = new COORD { X = 0, Y = (short)row };

        if (!ReadConsoleOutputCharacterW(handle.DangerousGetHandle(), buffer, (uint)length, coord, out uint read))
            throw new InvalidOperationException("Не удалось прочитать буфер консоли.");

        return new string(buffer, 0, (int)read);
    }

    // Читает цветовые атрибуты строки (легаси 16-цветная палитра консоли). Игра красит
    // текст 24-битным ANSI — легаси-атрибуты могут не отражать точный RGB, но разницу
    // между "покрашено" и "не покрашено" (или между разными покрасками) уловить можно.
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
