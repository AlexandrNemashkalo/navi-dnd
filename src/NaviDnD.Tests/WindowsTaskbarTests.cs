using System.Diagnostics;
using System.Runtime.InteropServices;
using NaviDnD.Helpers;

namespace NaviDnD.Tests;

public class WindowsTaskbarTests
{
    [Fact]
    public void RelaunchCommandRoundTripsWindowsArguments()
    {
        if (!OperatingSystem.IsWindows()) return;
        var start = new ProcessStartInfo(@"C:\Game with spaces\alacritty.exe");
        string[] arguments = ["--config-file", @"C:\Игра\terminal.toml", "", "quoted \"value\"", @"C:\ends with slash\", "literal & value"];
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        nint argv = CommandLineToArgvW(WindowsTaskbar.CommandLine(start), out int count);
        Assert.NotEqual(0, argv);
        try
        {
            var actual = Enumerable.Range(0, count)
                .Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))).ToArray();
            Assert.Equal(new[] { start.FileName }.Concat(arguments), actual);
        }
        finally { LocalFree(argv); }
    }

    [Fact]
    public void WindowExposesGameIdentityAndCompleteRelaunchCommand()
    {
        if (!OperatingSystem.IsWindows()) return;
        nint window = CreateWindowEx(0, "STATIC", "Taskbar property test", 0, 0, 0, 1, 1, 0, 0, 0, 0);
        Assert.NotEqual(0, window);
        try
        {
            const string directory = @"C:\Users\Player Name\AppData\Local\NaviDnD-Run";
            var terminalStart = WindowsAlacritty.CreateStartInfo(Path.Combine(directory, "alacritty.exe"),
                Path.Combine(directory, "NaviDnD.exe"), Path.Combine(directory, "Terminal", "alacritty-windows.toml"), [], directory);
            Assert.True(WindowsTaskbar.CommandLine(terminalStart).Length > 260);
            var start = WindowsAlacritty.CreateTaskbarStartInfo(Path.Combine(directory, "NaviDnD.exe"));
            WindowsTaskbar.SetWindowIdentity(window, start, @"C:\Game folder\dnd.ico");
            Guid iid = typeof(IPropertyStore).GUID;
            Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(window, ref iid, out var store));
            try
            {
                Assert.Equal(WindowsTaskbar.AppId, Read(store, 5));
                Assert.Equal("NaviDnD", Read(store, 4));
                Assert.Equal(@"C:\Game folder\dnd.ico,0", Read(store, 3));
                Assert.Equal(WindowsTaskbar.CommandLine(start), Read(store, 2));
                Assert.Equal("\"" + Path.Combine(directory, "NaviDnD.exe") + "\"", Read(store, 2));
                WindowsTaskbar.ClearWindowIdentity(window);
                foreach (uint id in new uint[] { 2, 3, 4, 5 }) Assert.Null(Read(store, id));
            }
            finally { Marshal.ReleaseComObject(store); }
        }
        finally { DestroyWindow(window); }
    }

    [Fact]
    public void ShortcutUsesSameIdentityAsGameWindow()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = Path.Combine(Path.GetTempPath(), $"NaviDnD taskbar {Guid.NewGuid()}.lnk");
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        object? link = null;
        try
        {
            dynamic shortcut = link = ((dynamic)shell).CreateShortcut(path);
            shortcut.TargetPath = Path.Combine(Environment.SystemDirectory, "notepad.exe");
            shortcut.Save();
            WindowsTaskbar.SetShortcutIdentity(path);
            Guid iid = typeof(IPropertyStore).GUID;
            Marshal.ThrowExceptionForHR(SHGetPropertyStoreFromParsingName(path, 0, 0, ref iid, out var store));
            try { Assert.Equal(WindowsTaskbar.AppId, Read(store, 5)); }
            finally { Marshal.ReleaseComObject(store); }
        }
        finally
        {
            if (link != null) Marshal.ReleaseComObject(link);
            Marshal.ReleaseComObject(shell);
            File.Delete(path);
        }
    }

    private static string? Read(IPropertyStore store, uint id)
    {
        var key = new PropertyKey { Format = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = id };
        Marshal.ThrowExceptionForHR(store.GetValue(ref key, out var value));
        try
        {
            if (value.Type == 0) return null;
            Assert.Equal(31, value.Type);
            return Marshal.PtrToStringUni(value.Pointer);
        }
        finally { PropVariantClear(ref value); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Sequential)] private struct PropVariant
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
    [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(nint window, ref Guid iid, out IPropertyStore store);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHGetPropertyStoreFromParsingName(string path, nint binding, uint flags, ref Guid iid, out IPropertyStore store);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}
