using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using NaviDnD.Installer;

namespace NaviDnD;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        if (!OperatingSystem.IsWindows()) return 1;
        try
        {
            // GUI subsystem: Windows must never allocate a console for this process.
            var start = GameLauncher.CreateTerminalStartInfo(AppContext.BaseDirectory);
            Helpers.WindowsTerminalSizing.PrepareLiveConfig(start);
            using var terminal = Process.Start(start);
            return terminal == null ? 1 : 0;
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
        {
            L.SetLanguage(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
            MessageBox(0, L.F("Could not start NaviDnD: {0}", error.Message), "NaviDnD", 0x10);
            return 1;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint window, string text, string caption, uint type);
}
