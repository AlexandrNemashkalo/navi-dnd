using System.Diagnostics;
using System.IO.Compression;

namespace NaviDnD.Installer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Автоматические проверки сборки без окна: база из частей рядом с установщиком / цепочка патчей из папки.
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
        // Обновление из игры ≥1.1.5: в рабочей папке — патчи (и при повреждённой базе — части базы).
        if (args.Length == 4 && args[0] == "--apply-update" && int.TryParse(args[2], out int gamePid))
        {
            RunUpdate(Path.GetFullPath(args[1]), gamePid, Path.GetFullPath(args[3]), legacy: false);
            return;
        }
        // Старый клиент (≤1.1.4) скачал по SHA256SUMS базу и этот установщик: база — рядом, патчи — из релиза.
        if (args.Length == 3 && args[0] == "--update-silent" && int.TryParse(args[2], out int parentPid))
        {
            RunUpdate(Path.GetFullPath(args[1]), parentPid, AppContext.BaseDirectory, legacy: true);
            return;
        }
        string? destination = args.Length == 3 && args[0] == "--update" ? args[1] : null;
        int waitPid = destination != null && int.TryParse(args[2], out int pid) ? pid : 0;
        Application.Run(new SetupForm(destination, waitPid));
    }

    internal static void Extract(string destination)
    {
        Installation.Apply(destination, AppContext.BaseDirectory);
    }

    // После базы: патчи рядом с установщиком (releases/patches), иначе — из последнего релиза; затем помощник
    // обновления в папку игры. Сообщение — для окна установки.
    internal static async Task<string> PatchAfterBaseAsync(string root, Action<string>? status = null)
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
            if (download != null) chain = await UpdateClient.DownloadPatchesAsync(installed, download, status);
            if (chain.Count > 0)
            {
                status?.Invoke("Применение обновлений…");
                installed = PatchInstaller.ApplyChain(root, chain);
            }
            UpdateClient.InstallHelper(root);
            return installed;
        }
        finally { if (download != null) try { Directory.Delete(download, true); } catch { } }
    }

    private static void RunUpdate(string destination, int parentPid, string workDir, bool legacy)
    {
        bool parentExited = false;
        try
        {
            try { using var parent = Process.GetProcessById(parentPid); if (!parent.WaitForExit(30000)) throw new IOException("Игра не закрылась за 30 секунд."); }
            catch (ArgumentException) { }
            parentExited = true;
            // Части базы есть — полная установка (повреждённая база, версия вне цепочки, переход со старого клиента).
            if (Directory.GetFiles(workDir, "NaviDnD-payload.*").Length > 0) Installation.Apply(destination, workDir);
            if (legacy) PatchAfterBaseAsync(destination).GetAwaiter().GetResult();
            else
            {
                var chain = UpdateClient.OrderChain(UpdateClient.InstalledVersion(destination), Directory.GetFiles(workDir, "NaviDnD-patch-*.zip"));
                if (chain.Count > 0) PatchInstaller.ApplyChain(destination, chain);
                UpdateClient.InstallHelper(destination);
            }
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
            try { File.AppendAllText(Path.Combine(destination, "Storage", "update-error.txt"), "\nНе удалось перезапустить игру: " + error.Message); } catch { }
        }
        // Скачанное — во временной папке игры/старого клиента; сам помощник там занят и удалится позже вместе с Temp.
        foreach (string pattern in new[] { "NaviDnD-payload.*", "NaviDnD-patch-*.zip" })
            foreach (string file in Directory.GetFiles(workDir, pattern))
                try { File.Delete(file); } catch { }
    }
}

internal sealed class SetupForm : Form
{
    private readonly TextBox folder = new() { Width = 390, Text = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NaviDnD") };
    private readonly Button install = new() { Text = "Установить", AutoSize = true };
    private readonly CheckBox shortcut = new() { Text = "Ярлык на рабочем столе", Checked = true, AutoSize = true };
    private readonly CheckBox launch = new() { Text = "Запустить после установки", Checked = true, AutoSize = true };
    private readonly Label status = new() { AutoSize = true };
    private bool installed;

    internal SetupForm(string? updateDestination = null, int waitPid = 0)
    {
        if (updateDestination != null) { folder.Text = updateDestination; shortcut.Checked = false; install.Text = "Обновить"; }
        Text = "Установка NaviDnD";
        ClientSize = new Size(550, 265);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20),
            FlowDirection = FlowDirection.TopDown, WrapContents = false };
        panel.Controls.Add(new Label { AutoSize = true, Text = "NaviDnD для Windows x64 — .NET уже включён." });
        panel.Controls.Add(new Label { AutoSize = true, Text = "Для ИИ нужен отдельный Codex CLI или Claude Code и вход в аккаунт." });
        panel.Controls.Add(new Label { AutoSize = true, Text = "Папка установки:" });
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(folder);
        var browse = new Button { Text = "Обзор…", AutoSize = true };
        browse.Click += (_, _) => {
            using var dialog = new FolderBrowserDialog { SelectedPath = folder.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath;
        };
        row.Controls.Add(browse);
        panel.Controls.Add(row);
        panel.Controls.Add(shortcut);
        panel.Controls.Add(launch);
        panel.Controls.Add(install);
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
                    status.Text = "Ожидание закрытия игры…";
                    await Task.Run(() => { try { using var process = Process.GetProcessById(waitPid); if (!process.WaitForExit(30000)) throw new IOException("Закройте игру перед обновлением."); } catch (ArgumentException) { } });
                }
                foreach (var process in Process.GetProcessesByName("NaviDnD"))
                {
                    using (process)
                    if (string.Equals(process.MainModule?.FileName, Path.Combine(destination, "NaviDnD.exe"), StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Закройте игру перед обновлением.");
                }
                status.Text = "Распаковка игры…";
                await Task.Run(() => Program.Extract(destination));
                // База поставлена — до актуальной версии патчами (рядом с установщиком или из релиза).
                string version;
                try
                {
                    version = await Task.Run(() => Program.PatchAfterBaseAsync(destination, text => BeginInvoke(() => status.Text = text)));
                }
                catch (Exception patchError)
                {
                    MessageBox.Show(this, $"Установлена базовая версия {UpdateClient.InstalledVersion(destination)}. Обновления не применены: " +
                        patchError.Message + "\nИгра предложит обновиться при запуске.", "Обновления", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    version = UpdateClient.InstalledVersion(destination);
                }
                string executable = Path.Combine(destination, "NaviDnD.exe");
                if (shortcut.Checked) CreateShortcut(destination, executable);
                status.Text = $"Игра {version} установлена. Настройки и сохранения создаются при запуске.";
                if (launch.Checked)
                {
                    var start = new ProcessStartInfo(ConsoleHostPath) {
                        UseShellExecute = true, WorkingDirectory = destination };
                    start.ArgumentList.Add(executable);
                    Process.Start(start);
                }
                install.Text = "Закрыть";
                installed = true;
                install.Enabled = true;
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message + "\nЗакройте игру и проверьте доступ к папке.",
                    "Ошибка установки", MessageBoxButtons.OK, MessageBoxIcon.Error);
                install.Enabled = true;
                row.Enabled = true;
            }
            finally { ControlBox = true; }
        };
        if (updateDestination != null) Shown += (_, _) => install.PerformClick();
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
