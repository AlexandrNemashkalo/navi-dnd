using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NaviDnD.Helpers;

// The terminal owns the HWND, so process identity and WM_SETICON alone cannot
// make Windows pin the game. Give the window and installed shortcut the same ID.
public static class WindowsTaskbar
{
    public const string AppId = "NaviDnD.Game";
    private static readonly Guid PropertySet = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    public static void SetWindowIdentity(nint window, ProcessStartInfo launch, string iconPath)
    {
        if (!OperatingSystem.IsWindows() || window == 0) return;
        Guid iid = typeof(IPropertyStore).GUID;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(window, ref iid, out var store));
        try
        {
            // Relaunch data must precede the ID: assigning it makes the shell regroup the window.
            Set(store, 2, CommandLine(launch));
            Set(store, 3, iconPath + ",0");
            Set(store, 4, "NaviDnD");
            Set(store, 5, AppId);
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    public static void SetShortcutIdentity(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        Guid iid = typeof(IPropertyStore).GUID;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreFromParsingName(path, 0, 2, ref iid, out var store));
        try
        {
            Set(store, 5, AppId);
            Marshal.ThrowExceptionForHR(store.Commit());
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    public static void ClearWindowIdentity(nint window)
    {
        if (!OperatingSystem.IsWindows() || window == 0) return;
        Guid iid = typeof(IPropertyStore).GUID;
        if (SHGetPropertyStoreForWindow(window, ref iid, out var store) < 0) return;
        try
        {
            // Release shell-owned property values before the terminal destroys its HWND.
            foreach (uint id in new uint[] { 2, 3, 4, 5 }) Set(store, id, null);
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    public static string CommandLine(ProcessStartInfo start) =>
        string.Join(" ", new[] { start.FileName }.Concat(start.ArgumentList).Select(QuoteArgument));

    private static string QuoteArgument(string value)
    {
        // CommandLineToArgvW/CRT quoting, including quotes and trailing backslashes.
        var result = new System.Text.StringBuilder("\"");
        int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    private static void Set(IPropertyStore store, uint id, string? text)
    {
        var key = new PropertyKey { Format = PropertySet, Id = id };
        var value = text == null ? default : new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(text) }; // VT_EMPTY / VT_LPWSTR
        try { Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value)); }
        finally { Marshal.FreeCoTaskMem(value.Pointer); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid Format; public uint Id; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Type, Reserved1, Reserved2, Reserved3;
        public nint Pointer, Padding;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(nint window, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetPropertyStoreFromParsingName(string path, nint binding, uint flags,
        ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
}
