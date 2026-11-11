using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

/// <summary>
/// 闪启 Launcher 安装引导器：把内嵌的 MSI 释放到临时目录后交给 msiexec 静默安装，
/// 装完自动启动程序。用 .NET Framework 自带编译器构建，目标机无需任何运行时。
/// </summary>
internal static class Program
{
    private const string MsiResource = "app.msi";
    private const string MsiFileName = "ShanQiLauncher.msi";

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string tempDir = Path.Combine(Path.GetTempPath(), "ShanQiLauncherSetup");
        string msiPath = Path.Combine(tempDir, MsiFileName);

        using (SetupForm form = new SetupForm())
        {
            form.Shown += delegate
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    bool ok = false;

                    try
                    {
                        Directory.CreateDirectory(tempDir);

                        using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(MsiResource))
                        {
                            if (source != null)
                            {
                                using (FileStream target = File.Create(msiPath))
                                {
                                    source.CopyTo(target);
                                }
                            }
                        }

                        form.SetTip("正在安装，请稍候…");

                        ProcessStartInfo info = new ProcessStartInfo(
                            "msiexec.exe",
                            "/i \"" + msiPath + "\" /qn /norestart");

                        info.UseShellExecute = false;

                        using (Process process = Process.Start(info))
                        {
                            if (process != null)
                            {
                                process.WaitForExit();
                                ok = process.ExitCode == 0 || process.ExitCode == 3010;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowError(ex.Message);
                    }
                    finally
                    {
                        try
                        {
                            if (File.Exists(msiPath))
                                File.Delete(msiPath);
                        }
                        catch
                        {
                        }

                        try
                        {
                            if (Directory.Exists(tempDir) && Directory.GetFileSystemEntries(tempDir).Length == 0)
                                Directory.Delete(tempDir);
                        }
                        catch
                        {
                        }
                    }

                    if (ok)
                    {
                        form.SetTip("安装完成，正在启动…");
                        StartApp();

                        Thread.Sleep(600);
                    }
                    else
                    {
                        ShowError("安装未完成，请重试或以管理员身份运行。");
                    }

                    try
                    {
                        form.BeginInvoke((MethodInvoker)delegate { form.Close(); });
                    }
                    catch
                    {
                    }
                });
            };

            Application.Run(form);
        }
    }

    private static void StartApp()
    {
        try
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ShanQiLauncher",
                "Flow.Launcher.exe");

            if (File.Exists(path))
                Process.Start(path);
        }
        catch
        {
        }
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(message, "闪启 Launcher 安装程序", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}

internal sealed class SetupForm : Form
{
    private readonly Label _tip;

    public SetupForm()
    {
        Text = "闪启 Launcher 安装程序";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(430, 146);
        BackColor = Color.FromArgb(28, 28, 31);
        ForeColor = Color.FromArgb(232, 232, 232);
        Font = new Font("Microsoft YaHei UI", 9F);
        Icon = LoadIcon();

        Label title = new Label();
        title.Text = "闪启 Launcher";
        title.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
        title.ForeColor = Color.White;
        title.SetBounds(24, 20, 380, 30);
        Controls.Add(title);

        _tip = new Label();
        _tip.Text = "正在准备安装，请稍候…";
        _tip.SetBounds(24, 56, 380, 22);
        _tip.ForeColor = Color.FromArgb(154, 154, 154);
        Controls.Add(_tip);

        ProgressBar bar = new ProgressBar();
        bar.Style = ProgressBarStyle.Marquee;
        bar.MarqueeAnimationSpeed = 24;
        bar.SetBounds(24, 86, 382, 6);
        Controls.Add(bar);

        Label hint = new Label();
        hint.Text = "安装完成后会从开始菜单创建快捷方式";
        hint.SetBounds(24, 104, 382, 20);
        hint.ForeColor = Color.FromArgb(110, 110, 118);
        Controls.Add(hint);
    }

    public void SetTip(string text)
    {
        try
        {
            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)delegate { _tip.Text = text; });
            }
            else
            {
                _tip.Text = text;
            }
        }
        catch
        {
        }
    }

    private static Icon LoadIcon()
    {
        try
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("setup.ico");

            if (stream != null)
                return new Icon(stream);
        }
        catch
        {
        }

        return null;
    }
}
