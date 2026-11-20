using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("每日待办安装程序")]
[assembly: AssemblyProduct("DailyReminder Setup")]
[assembly: AssemblyVersion("1.4.0.0")]
[assembly: AssemblyFileVersion("1.4.0.0")]

internal static class SetupProgram
{
    internal const string Registration = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DailyReminder";
    internal const string Marker = "DailyReminder.Installation.1";

    [STAThread]
    private static int Main(string[] arguments)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
#if UNINSTALLER
            if (arguments.Length >= 2 && arguments[0] == "--remove-from")
            {
                RemoveInstallation(arguments[1]);
                if (!arguments.Contains("--quiet")) MessageBox.Show("卸载完成，待办记录和设置已保留。", "每日待办");
                return 0;
            }
            if (MessageBox.Show("卸载每日待办？\n你的待办记录和背景将保留。", "每日待办", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;
            string directory = Path.GetDirectoryName(Application.ExecutablePath);
            ValidateRemoval(directory);
            string temporaryDirectory = Path.Combine(Path.GetTempPath(), "DailyReminder-Uninstall", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);
            string helper = Path.Combine(temporaryDirectory, "Uninstall.exe");
            File.Copy(Application.ExecutablePath, helper);
            Process.Start(new ProcessStartInfo(helper, "--remove-from " + Quote(directory)) { UseShellExecute = true });
            return 0;
#else
            if (arguments.Length >= 2 && arguments[0] == "--extract-to")
            {
                string target = ValidateDirectory(arguments[1]);
                ExtractPayload(target);
                if (arguments.Contains("--test-shortcut")) CreateShortcut(Path.Combine(target, "每日待办.lnk"), Path.Combine(target, "DailyReminder.exe"));
                return 0;
            }
            Application.Run(new SetupForm());
            return 0;
#endif
        }
        catch (Exception exception)
        {
            if (!arguments.Contains("--quiet") && !arguments.Contains("--extract-to")) MessageBox.Show(exception.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    internal static string Quote(string text) { return "\"" + text + "\""; }

    internal static string ValidateDirectory(string directory)
    {
        string resolved = Path.GetFullPath(Environment.ExpandEnvironmentVariables(directory)).TrimEnd(Path.DirectorySeparatorChar);
        if (String.IsNullOrWhiteSpace(directory) || resolved.Length <= Path.GetPathRoot(resolved).TrimEnd(Path.DirectorySeparatorChar).Length)
            throw new InvalidOperationException("请选择一个独立的安装文件夹。");
        if (resolved.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请选择 Windows 系统目录以外的文件夹。");
        return resolved;
    }

    internal static void StopApplication(string exactDirectory)
    {
        foreach (Process process in Process.GetProcessesByName("DailyReminder"))
        {
            using (process)
            {
                string path;
                try { path = process.MainModule.FileName; }
                catch (System.ComponentModel.Win32Exception) { continue; }
                catch (InvalidOperationException) { continue; }
                if (exactDirectory != null && !String.Equals(Path.GetDirectoryName(path), exactDirectory, StringComparison.OrdinalIgnoreCase)) continue;
                if (!String.Equals(FileVersionInfo.GetVersionInfo(path).ProductName, "DailyReminder", StringComparison.OrdinalIgnoreCase)) continue;
                process.CloseMainWindow();
                if (!process.WaitForExit(3000))
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
            }
        }
    }

    internal static void ExtractPayload(string directory)
    {
        Directory.CreateDirectory(directory);
        string existing = Path.Combine(directory, "DailyReminder.exe");
        if (File.Exists(existing) && !String.Equals(FileVersionInfo.GetVersionInfo(existing).ProductName, "DailyReminder", StringComparison.OrdinalIgnoreCase))
            throw new IOException("此目录已有其他同名程序，请选择另一个目录。");
        ExtractResource("DailyReminder.Payload", existing);
        ExtractResource("DailyReminder.Uninstall", Path.Combine(directory, "Uninstall.exe"));
        File.WriteAllText(Path.Combine(directory, "install.id"), Marker, new UTF8Encoding(false));
    }

    private static void ExtractResource(string name, string destination)
    {
        string temporary = destination + ".installing-" + Guid.NewGuid().ToString("N");
        using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        {
            if (resource == null) throw new InvalidDataException("安装包缺少文件，请重新获取完整安装包。");
            using (FileStream output = File.Create(temporary)) resource.CopyTo(output);
        }
        if (File.Exists(destination)) File.Replace(temporary, destination, null);
        else File.Move(temporary, destination);
    }

    internal static string DesktopShortcut { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "每日待办.lnk"); } }
    internal static string MenuShortcut { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "DailyReminder", "每日待办.lnk"); } }

    internal static void CreateShortcut(string shortcutPath, string executable)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath));
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        try
        {
            shortcut.TargetPath = executable;
            shortcut.WorkingDirectory = Path.GetDirectoryName(executable);
            shortcut.IconLocation = executable + ",0";
            shortcut.Description = "每日待办与桌面提醒";
            shortcut.Save();
        }
        finally
        {
            Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
    }

    internal static void RegisterInstallation(string directory, bool desktopShortcut)
    {
        string executable = Path.Combine(directory, "DailyReminder.exe");
        if (desktopShortcut) CreateShortcut(DesktopShortcut, executable);
        CreateShortcut(MenuShortcut, executable);
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Registration))
        {
            key.SetValue("DisplayName", "每日待办");
            key.SetValue("DisplayVersion", "1.4.0");
            key.SetValue("Publisher", "DailyReminder");
            key.SetValue("InstallLocation", directory);
            key.SetValue("DisplayIcon", executable + ",0");
            key.SetValue("UninstallString", Quote(Path.Combine(directory, "Uninstall.exe")));
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", (int)(new FileInfo(executable).Length / 1024), RegistryValueKind.DWord);
        }
        string startup = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "DailyReminder.vbs");
        if (File.Exists(startup))
        {
            string command = (Quote(executable) + " --minimized").Replace("\"", "\"\"");
            File.WriteAllText(startup, "Set sh = CreateObject(\"WScript.Shell\")\r\nsh.Run \"" + command + "\", 0, False\r\n", new UTF8Encoding(false));
        }
    }

    private static void ValidateRemoval(string directory)
    {
        string resolved = ValidateDirectory(directory);
        string marker = Path.Combine(resolved, "install.id");
        if (!File.Exists(marker) || File.ReadAllText(marker, Encoding.UTF8).Trim() != Marker)
            throw new InvalidOperationException("没有找到匹配的安装信息，未删除任何文件。");
    }

    private static void RemoveInstallation(string directory)
    {
        string resolved = ValidateDirectory(directory);
        ValidateRemoval(resolved);
        StopApplication(resolved);
        Thread.Sleep(500);
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Registration))
        {
            if (key != null && String.Equals(key.GetValue("InstallLocation") as string, resolved, StringComparison.OrdinalIgnoreCase))
            {
                RemoveShortcut(DesktopShortcut, resolved);
                RemoveShortcut(MenuShortcut, resolved);
                Registry.CurrentUser.DeleteSubKeyTree(Registration, false);
                string startup = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "DailyReminder.vbs");
                if (File.Exists(startup) && File.ReadAllText(startup).IndexOf(Path.Combine(resolved, "DailyReminder.exe"), StringComparison.OrdinalIgnoreCase) >= 0) File.Delete(startup);
            }
        }
        foreach (string name in new[] { "DailyReminder.exe", "Uninstall.exe", "install.id" })
        {
            string path = Path.Combine(resolved, name);
            for (int attempt = 0; File.Exists(path) && attempt < 15; attempt++)
            {
                try { File.Delete(path); }
                catch (IOException) { if (attempt == 14) throw; Thread.Sleep(200); }
            }
        }
        if (!Directory.EnumerateFileSystemEntries(resolved).Any()) Directory.Delete(resolved, false);
    }

    private static void RemoveShortcut(string shortcutPath, string directory)
    {
        if (!File.Exists(shortcutPath)) return;
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        try
        {
            if (String.Equals((string)shortcut.TargetPath, Path.Combine(directory, "DailyReminder.exe"), StringComparison.OrdinalIgnoreCase)) File.Delete(shortcutPath);
        }
        finally { Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell); }
    }
}

#if !UNINSTALLER
internal sealed class SetupForm : Form
{
    private readonly TextBox pathBox = new TextBox();
    private readonly CheckBox desktopBox = new CheckBox();
    private readonly CheckBox launchBox = new CheckBox();
    private readonly Button browseButton = new Button();
    private readonly Button installButton = new Button();
    private readonly Label status = new Label();
    private bool installing;

    internal SetupForm()
    {
        Text = "每日待办安装";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        ClientSize = new Size(570, 300);
        Font = new Font("Microsoft YaHei UI", 9);
        BackColor = Color.FromArgb(246, 248, 251);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        var logo = new PictureBox { Image = Icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(25, 24), Size = new Size(46, 46) };
        var title = new Label { Text = "安装每日待办", Font = new Font(Font.FontFamily, 15, FontStyle.Bold), AutoSize = true, Location = new Point(86, 24) };
        var description = new Label { Text = "选择程序目录，桌面只放快捷方式", AutoSize = true, ForeColor = Color.DimGray, Location = new Point(88, 56) };
        var pathLabel = new Label { Text = "程序安装目录", AutoSize = true, Location = new Point(25, 100) };
        pathBox.SetBounds(25, 126, 425, 28);
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SetupProgram.Registration))
            pathBox.Text = key == null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DailyReminder") : key.GetValue("InstallLocation") as string;
        browseButton.Text = "浏览…";
        browseButton.SetBounds(462, 124, 82, 29);
        browseButton.Click += Browse_Click;
        var dataLabel = new Label { Text = "记录与背景自动保存至 %LOCALAPPDATA%\\DailyReminder", AutoSize = true, ForeColor = Color.DimGray, Location = new Point(25, 164) };
        desktopBox.Text = "创建桌面快捷方式";
        desktopBox.Checked = true;
        desktopBox.SetBounds(25, 192, 200, 25);
        launchBox.Text = "安装完成后打开";
        launchBox.Checked = true;
        launchBox.SetBounds(250, 192, 200, 25);
        status.SetBounds(25, 242, 350, 40);
        status.ForeColor = Color.DimGray;
        status.Text = "无需单独安装运行环境";
        installButton.Text = "安装";
        installButton.SetBounds(430, 237, 114, 35);
        installButton.BackColor = Color.FromArgb(36, 114, 158);
        installButton.ForeColor = Color.White;
        installButton.FlatStyle = FlatStyle.Flat;
        installButton.Click += Install_Click;
        Controls.AddRange(new Control[] { logo, title, description, pathLabel, pathBox, browseButton, dataLabel, desktopBox, launchBox, status, installButton });
        FormClosing += delegate(object sender, FormClosingEventArgs eventArgs) { eventArgs.Cancel = installing; };
        AcceptButton = installButton;
    }

    private void Browse_Click(object sender, EventArgs eventArgs)
    {
        using (var dialog = new FolderBrowserDialog { Description = "选择程序存放目录", SelectedPath = pathBox.Text })
        {
            if (dialog.ShowDialog(this) == DialogResult.OK)
                pathBox.Text = String.Equals(Path.GetFileName(dialog.SelectedPath.TrimEnd('\\')), "DailyReminder", StringComparison.OrdinalIgnoreCase) ? dialog.SelectedPath : Path.Combine(dialog.SelectedPath, "DailyReminder");
        }
    }

    private async void Install_Click(object sender, EventArgs eventArgs)
    {
        try
        {
            string directory = SetupProgram.ValidateDirectory(pathBox.Text);
            installing = true;
            pathBox.Enabled = browseButton.Enabled = installButton.Enabled = desktopBox.Enabled = launchBox.Enabled = false;
            status.Text = "正在安装，请稍候…";
            await Task.Run(delegate { SetupProgram.StopApplication(null); SetupProgram.ExtractPayload(directory); });
            SetupProgram.RegisterInstallation(directory, desktopBox.Checked);
            status.Text = "安装完成";
            installing = false;
            if (launchBox.Checked) Process.Start(new ProcessStartInfo(Path.Combine(directory, "DailyReminder.exe")) { WorkingDirectory = directory, UseShellExecute = true });
            Close();
        }
        catch (Exception exception)
        {
            installing = false;
            pathBox.Enabled = browseButton.Enabled = installButton.Enabled = desktopBox.Enabled = launchBox.Enabled = true;
            status.Text = "安装未完成，可以更换目录后重试。";
            MessageBox.Show(this, exception.Message, "安装未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
#endif
