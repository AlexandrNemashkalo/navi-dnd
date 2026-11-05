using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace NaviDnD.Tests;

public class TerminalOutputTests
{
    [Theory]
    [InlineData("1234567890\nX", 0, 0, "\u001b[2;1HX")]
    [InlineData("1234567890\r\nX", 4, 2, "\u001b[4;5HX")]
    [InlineData("abc\nX", 4, 2, "\u001b[4;5HX")]
    public void NewlineUsesExplicitGridPositionAfterFullOrPartialRow(string text, int x, int y, string expected)
    {
        var type = typeof(Storage).Assembly.GetType("NaviDnD.Helpers.UnixTerminal")!;
        var terminal = RuntimeHelpers.GetUninitializedObject(type);
        void Set(string field, object value) => type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(terminal, value);
        Set("_width", 10); Set("_height", 8);
        Set("_centered", true); Set("_layoutCols", 10); Set("_layoutRows", 5);
        Set("_offX", x); Set("_offY", y);
        var writerType = type.GetNestedType("TrackingWriter", BindingFlags.NonPublic)!;
        using var stream = new MemoryStream();
        var writer = (TextWriter)Activator.CreateInstance(writerType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [stream, terminal], null)!;
        writer.Write(text);
        writer.Flush();
        string output = Encoding.UTF8.GetString(stream.ToArray());
        Assert.EndsWith(expected, output);
        Assert.DoesNotContain('\n', output);
        Assert.Equal(1, writerType.GetProperty("Row")!.GetValue(writer));
        Assert.Equal(1, writerType.GetProperty("Col")!.GetValue(writer));
    }
}
