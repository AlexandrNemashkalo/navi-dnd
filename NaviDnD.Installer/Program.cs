using System.Diagnostics;
using System.IO.Compression;

namespace NaviDnD.Installer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ChooseLanguage(args.Length >= 2 && args[0] is "--apply-update" or "--update-silent" or "--update" ? args[1] : null);
        // Automatic build checks without window: base of parts next to the installer / chain of patches from a folder.
        if (args.Length == 2 && args[0] == "--extract")
        {
            Extract(args[1]);
            return;
        }
        if (args.Length == 3 && args[0] == "--apply-patches")
        {
            string root = Path.GetFullPath(args[1]);
            PatchInstaller.ApplyChain(root, UpdateClient.OrderChain(UpdateClient.InstalledVersion(root),
                Directory.GetFiles(Path.GetFullPath(args[2]), "NaviDnD-patch-*.zip")));
            return;
        }
        ApplicationConfiguration.Initialize();
        // Update from game ≥1.1.5: in the working folder - patches (and if the database is damaged - parts of the database).
        if (args.Length == 4 && args[0] == "--apply-update" && int.TryParse(args[2], out int gamePid))
        {
            RunUpdate(Path.GetFullPath(args[1]), gamePid, Path.GetFullPath(args[3]), legacy: false);
            return;
        }
        // An old client (≤1.1.4) downloaded the database and this installer using SHA256SUMS: the database is nearby, the patches are from the release.
        if (args.Length == 3 && args[0] == "--update-silent" && int.TryParse(args[2], out int parentPid))
        {
            RunUpdate(Path.GetFullPath(args[1]), parentPid, AppContext.BaseDirectory, legacy: true);
            return;
        }
        string? destination = args.Length == 3 && args[0] == "--update" ? args[1] : null;
        int waitPid = destination != null && int.TryParse(args[2], out int pid) ? pid : 0;
        Application.Run(new SetupForm(destination, waitPid));
    }

    // Language of the window and messages: as in the installed game (Storage/settings.json; without the field
    // Language - version before language selection, Russian); the new installation is English, just like the
    // default game.
    private static void ChooseLanguage(string? root)
    {
        string language = L.English;
        try
        {
            string path = Path.Combine(root ?? "", "Storage", "settings.json");
            if (root != null && File.Exists(path))
                language = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)).RootElement.TryGetProperty("Language", out var value)
                    ? value.GetString() ?? L.Russian : L.Russian;
        }
        catch { }
        L.SetLanguage(language);
    }

    internal static void Extract(string destination, Action<string, double?>? progress = null)
    {
        Installation.Apply(destination, AppContext.BaseDirectory, progress);
    }

    // After the base: patches next to the installer (releases/patches), otherwise - from the latest release;
    // then the update assistant to the game folder. progress - stage and share 0..1 (null - without share) for
    // the installation window.
    internal static async Task<string> PatchAfterBaseAsync(string root, Action<string, double?>? progress = null)
    {
        string installed = UpdateClient.InstalledVersion(root);
        var chain = UpdateClient.LocalPatches(AppContext.BaseDirectory, installed);
        string? download = null;
        if (chain.Count == 0)
        {
            download = Path.Combine(Path.GetTempPath(), "NaviDnD-patches-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(download);
        }
        try
        {
            if (download != null) chain = await UpdateClient.DownloadPatchesAsync(installed, download, text => progress?.Invoke(text, null));
            if (chain.Count > 0) installed = PatchInstaller.ApplyChain(root, chain, progress);
            progress?.Invoke(L.T("Installing the update helper…"), null);
            UpdateClient.InstallHelper(root);
            return installed;
        }
        finally { if (download != null) try { Directory.Delete(download, true); } catch { } }
    }

    // Saves - to the format of the new version: the game itself without a window (“NaviDnD.exe --migrate”; the
    // migration code is in it, the assistant is not rebuilt for each migration). Only if the installed version
    // is not older than this assistant: the commands do not know the previous games and would simply start. A
    // failure does not cancel the update - the game will transfer the files at startup, the error will be shown
    // in “Updates” (update-error.txt is written by the migration itself; here - if it did not work at all).
    internal static void MigrateSaves(string root)
    {
        try
        {
            string exe = Path.Combine(root, "NaviDnD.exe");
            if (!File.Exists(exe) || !Directory.Exists(Path.Combine(root, "Storage"))) return;
            var own = typeof(Program).Assembly.GetName().Version ?? new Version(0, 0, 0);
            if (!Version.TryParse(UpdateClient.InstalledVersion(root), out var installed)
                || new Version(installed.Major, installed.Minor, Math.Max(0, installed.Build)) < new Version(own.Major, own.Minor, Math.Max(0, own.Build)))
                return;
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root };
            start.ArgumentList.Add("--migrate");
            start.ArgumentList.Add(root);
            using var process = Process.Start(start) ?? throw new IOException(L.T("Could not start updating the saves."));
            if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new IOException(L.T("Updating the saves did not finish within 5 minutes."));
            }
            if (process.ExitCode is not (0 or 1)) throw new IOException(L.F("Updating the saves failed (code {0}).", process.ExitCode));
        }
        catch (Exception error)
        {
            try { File.AppendAllText(Path.Combine(root, "Storage", "update-error.txt"), error.Message + "\n"); } catch { }
        }
    }

    private static void RunUpdate(string destination, int parentPid, string workDir, bool legacy)
    {
        bool parentExited = false;
        try
        {
            try { using var parent = Process.GetProcessById(parentPid); if (!parent.WaitForExit(30000)) throw new IOException(L.T("The game didn't close within 30 seconds.")); }
            catch (ArgumentException) { }
            parentExited = true;
            // Parts of the database are available - complete installation (damaged database, off-chain version, transition from an old client).
            if (Directory.GetFiles(workDir, "NaviDnD-payload.*").Length > 0) Installation.Apply(destination, workDir);
            if (legacy) PatchAfterBaseAsync(destination).GetAwaiter().GetResult();
            else
            {
                var chain = UpdateClient.OrderChain(UpdateClient.InstalledVersion(destination), Directory.GetFiles(workDir, "NaviDnD-patch-*.zip"));
                if (chain.Count > 0) PatchInstaller.ApplyChain(destination, chain);
                UpdateClient.InstallHelper(destination);
            }
            MigrateSaves(destination);
        }
        catch (Exception error)
        {
            try { Directory.CreateDirectory(Path.Combine(destination, "Storage")); File.WriteAllText(Path.Combine(destination, "Storage", "update-error.txt"), error.Message); } catch { }
        }
        if (!parentExited) return;
        try
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "conhost.exe"))
                { UseShellExecute = true, WorkingDirectory = destination };
            start.ArgumentList.Add(Path.Combine(destination, "NaviDnD.exe"));
            Process.Start(start);
        }
        catch (Exception error)
        {
            try { File.AppendAllText(Path.Combine(destination, "Storage", "update-error.txt"), "\n" + L.T("Could not restart the game: ") + error.Message); } catch { }
        }
        // Downloaded - in the temporary folder of the game/old client; the assistant himself is busy there and will leave later along with Temp.
        foreach (string pattern in new[] { "NaviDnD-payload.*", "NaviDnD-patch-*.zip" })
            foreach (string file in Directory.GetFiles(workDir, pattern))
                try { File.Delete(file); } catch { }
    }
}

internal sealed class SetupForm : Form
{
    private readonly TextBox folder = new() { Width = 390, Text = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NaviDnD") };
    private readonly Button install = new() { Text = L.T("Install"), AutoSize = true };
    private readonly CheckBox shortcut = new() { Text = L.T("Desktop shortcut"), Checked = true, AutoSize = true };
    private readonly CheckBox launch = new() { Text = L.T("Launch after installation"), Checked = true, AutoSize = true };
    private readonly Label status = new() { AutoSize = true };
    private readonly ProgressBar bar = new() { Maximum = 1000, Visible = false };
    private bool installed;
    private string? shownText;
    private int shownValue = -2;

    internal SetupForm(string? updateDestination = null, int waitPid = 0)
    {
        if (updateDestination != null) { folder.Text = updateDestination; shortcut.Checked = false; install.Text = L.T("Update"); }
        Text = L.T("NaviDnD Setup");
        // Window by Content: At Windows 150-200% scale the font is larger and the fixed size window has cut off the button.
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        var textWidth = new Size(LogicalToDeviceUnits(510), 0);   // long lines wrap instead of stretching the window
        folder.Width = LogicalToDeviceUnits(390);
        status.MaximumSize = textWidth;
        bar.Size = new Size(textWidth.Width, LogicalToDeviceUnits(20));
        var panel = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(LogicalToDeviceUnits(20)), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        panel.Controls.Add(new Label { AutoSize = true, MaximumSize = textWidth, Text = L.T("NaviDnD for Windows x64 — .NET is included.") });
        panel.Controls.Add(new Label { AutoSize = true, MaximumSize = textWidth, Text = L.T("The AI needs Codex CLI or Claude Code installed separately and signed in.") });
        panel.Controls.Add(new Label { AutoSize = true, MaximumSize = textWidth, Text = L.T("Install folder:") });
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(folder);
        var browse = new Button { Text = L.T("Browse…"), AutoSize = true };
        browse.Click += (_, _) => {
            using var dialog = new FolderBrowserDialog { SelectedPath = folder.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath;
        };
        row.Controls.Add(browse);
        panel.Controls.Add(row);
        panel.Controls.Add(shortcut);
        panel.Controls.Add(launch);
        panel.Controls.Add(install);
        panel.Controls.Add(bar);
        panel.Controls.Add(status);
        Controls.Add(panel);
        install.Click += async (_, _) => {
            if (installed) { Close(); return; }
            install.Enabled = false;
            row.Enabled = false;
            ControlBox = false;
            try
            {
                string destination = Path.GetFullPath(folder.Text);
                if (waitPid > 0)
                {
                    status.Text = L.T("Waiting for the game to close…");
                    await Task.Run(() => { try { using var process = Process.GetProcessById(waitPid); if (!process.WaitForExit(30000)) throw new IOException(L.T("Close the game before updating.")); } catch (ArgumentException) { } });
                }
                foreach (var process in Process.GetProcessesByName("NaviDnD"))
                {
                    using (process)
                    if (string.Equals(process.MainModule?.FileName, Path.Combine(destination, "NaviDnD.exe"), StringComparison.OrdinalIgnoreCase))
                        throw new IOException(L.T("Close the game before updating."));
                }
                Report(L.T("Preparing the archive…"), 0);
                await Task.Run(() => Program.Extract(destination, Report));
                // The database has been installed - up to the current version with patches (next to the installer or from the release).
                string version;
                try
                {
                    version = await Task.Run(() => Program.PatchAfterBaseAsync(destination, Report));
                }
                catch (Exception patchError)
                {
                    MessageBox.Show(this, L.F("Base version {0} is installed. Updates were not applied: ", UpdateClient.InstalledVersion(destination)) +
                        patchError.Message + "\n" + L.T("The game will offer to update when it starts."), L.T("Updates"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    version = UpdateClient.InstalledVersion(destination);
                }
                // Over the previous installation - saving it to the format of the new version.
                Report(L.T("Updating saves…"), null);
                await Task.Run(() => Program.MigrateSaves(destination));
                string executable = Path.Combine(destination, "NaviDnD.exe");
                if (shortcut.Checked) CreateShortcut(destination, executable);
                Report(L.F("Game {0} is installed. Settings and saves are created on first launch.", version), 1);
                if (launch.Checked)
                {
                    var start = new ProcessStartInfo(ConsoleHostPath) {
                        UseShellExecute = true, WorkingDirectory = destination };
                    start.ArgumentList.Add(executable);
                    Process.Start(start);
                }
                install.Text = L.T("Close");
                installed = true;
                install.Enabled = true;
            }
            catch (Exception error)
            {
                bar.Visible = false;
                MessageBox.Show(this, error.Message + "\n" + L.T("Close the game and check access to the folder."),
                    L.T("Setup error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                install.Enabled = true;
                row.Enabled = true;
            }
            finally { ControlBox = true; }
        };
        if (updateDestination != null) Shown += (_, _) => install.PerformClick();
    }

    // Stage and share 0..1 (null - running bar when the share cannot be recognized) from the work thread;
    // window - only when changed, unpacking reports each of the thousands of files.
    private void Report(string text, double? fraction)
    {
        int value = fraction is { } f ? (int)(Math.Clamp(f, 0, 1) * bar.Maximum) : -1;
        if (text == shownText && value == shownValue) return;
        shownText = text;
        shownValue = value;
        BeginInvoke(() =>
        {
            status.Text = text;
            bar.Visible = true;
            bar.Style = value < 0 ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            if (value >= 0) bar.Value = value;
        });
    }

    private static string ConsoleHostPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "conhost.exe");

    private static void CreateShortcut(string destination, string executable)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic link = shell.CreateShortcut(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "NaviDnD.lnk"));
        link.TargetPath = ConsoleHostPath;
        link.Arguments = "\"" + executable + "\"";
        link.WorkingDirectory = destination;
        link.IconLocation = Path.Combine(destination, "dnd.ico") + ",0";
        link.Save();
    }
}
