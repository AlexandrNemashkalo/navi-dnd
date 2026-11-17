using System.Text;

namespace NaviDnD;

public static class AtomicFile
{
    public static void WriteAllText(string path, string text) => Write(path, stream =>
    {
        using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(text);
    });

    // Stage in the same directory so replacement stays on the same filesystem.
    // A failed write never truncates the previous file.
    public static void Write(string path, Action<Stream> write)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
