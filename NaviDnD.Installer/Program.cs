using System.Diagnostics;
using System.IO.Compression;

namespace NaviDnD.Installer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Automated packaging check: extract the same payload without opening UI.
        if (args.Length == 2 && args[0] == "--extract")
        {
            Extract(args[1]);
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new SetupForm());
    }

    internal static void Extract(string destination)
    {
        string root = Path.GetFullPath(destination);
        Directory.CreateDirectory(root);
        var parts = Directory.GetFiles(AppContext.BaseDirectory, "NaviDnD-payload.*")
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        if (parts.Length == 0) throw new FileNotFoundException("Скачайте всю папку releases: рядом с установщиком нужны NaviDnD-payload.*.");
        using var stream = new FileStream(Path.Combine(Path.GetTempPath(), "NaviDnD-" + Guid.NewGuid() + ".zip"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
        foreach (var part in parts) { using var input = File.OpenRead(part); input.CopyTo(stream); }
        stream.Position = 0;
        using var archive = new ZipArchive(stream);
        foreach (var entry in archive.Entries)
        {
            string target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Некорректный путь в архиве.");
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
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

    internal SetupForm()
    {
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
                status.Text = "Распаковка игры…";
                await Task.Run(() => Program.Extract(destination));
                string executable = Path.Combine(destination, "NaviDnD.exe");
                if (shortcut.Checked) CreateShortcut(destination, executable);
                status.Text = "Игра установлена. Настройки и сохранения создаются при запуске.";
                if (launch.Checked) Process.Start(new ProcessStartInfo(executable) {
                    UseShellExecute = true, WorkingDirectory = destination });
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
    }

    private static void CreateShortcut(string destination, string executable)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic link = shell.CreateShortcut(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "NaviDnD.lnk"));
        link.TargetPath = executable;
        link.WorkingDirectory = destination;
        link.IconLocation = Path.Combine(destination, "dnd.ico") + ",0";
        link.Save();
    }
}
