using System.Runtime.InteropServices;

namespace NaviDnD.Helpers;

// Стандартное окно Windows «Открыть файл» (comdlg32 GetOpenFileNameW) — без WinForms: подключение
// WinForms сменило бы целевую платформу проекта (net9.0-windows) и пути сборки, от которых зависят
// MCP-сервер и UI-тесты. Окно показывается в отдельном STA-потоке поверх окна консоли.
public static class FileDialog
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public string? lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public string? lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string? lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileNameW(ref OPENFILENAME ofn);

    [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();

    private const int OFN_FILEMUSTEXIST = 0x00001000, OFN_PATHMUSTEXIST = 0x00000800, OFN_EXPLORER = 0x00080000,
        OFN_NOCHANGEDIR = 0x00000008;

    // Путь к выбранному файлу или null (отмена). filter — пары «описание\0маски» через \0.
    public static string? OpenFile(string title, string filter)
    {
        string? result = null;
        var thread = new Thread(() =>
        {
            const int bufferChars = 4096;
            IntPtr buffer = Marshal.AllocHGlobal(bufferChars * 2);
            try
            {
                Marshal.WriteInt16(buffer, 0);
                var ofn = new OPENFILENAME
                {
                    lStructSize = Marshal.SizeOf<OPENFILENAME>(),
                    hwndOwner = GetConsoleWindow(),
                    lpstrFilter = filter.Replace('|', '\0') + "\0\0",
                    nFilterIndex = 1,
                    lpstrFile = buffer,
                    nMaxFile = bufferChars,
                    lpstrTitle = title,
                    Flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR,
                };
                if (GetOpenFileNameW(ref ofn)) result = Marshal.PtrToStringUni(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}
