using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Live2DPet.Core.Pet;
using Live2DPet.Core.Settings;

namespace Live2DPet.App;

/// <summary>
/// 程序入口（纯 WinForms，无 WPF）。
/// 初始化崩溃日志 → 启动 WinForms 消息循环 → 退出后清理。
/// 任何未处理异常都会写崩溃日志并尝试自启（带次数上限，避免死循环）。
/// </summary>
internal static class Program
{
    private const string SingletonMutexName = "Global\\Live2DPet_Singleton_";
    private const string ActivateEventName = "Global\\Live2DPet_Activate_";
    private const string RestartEnv = "L2P_RESTARTS";
    private const int MaxRestarts = 3;

    private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "config", "settings.json");

    [STAThread]
    private static void Main()
    {
        // 崩溃日志 + 自启：任何未处理异常都落盘并在退出前拉起新进程
        CrashLog.Init();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => { RelaunchAfterCrash(e.Exception); Environment.Exit(1); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            RelaunchAfterCrash(e.ExceptionObject as Exception);
            Environment.Exit(1);
        };

        string user = Environment.UserName ?? "default";
        string mutexName = SingletonMutexName + user;
        string activateName = ActivateEventName + user;

        // 单实例保护 + 崩溃自启竞态处理：若互斥量被（正在退出的）旧实例持有，
        // 等其释放后重试获取，避免自启的新进程误判"已在运行"而秒退。
        for (int attempt = 0; attempt < 6; attempt++)
        {
            using var mutex = new Mutex(true, mutexName, out bool createdNew);
            if (createdNew)
            {
                try
                {
                    StartupLog("Main: begin");
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using var app = new PetApplication(activateName);
                    StartupLog("Main: before Application.Run");
                    Application.Run(app.UiHost);
                    StartupLog("Main: after Run");
                }
                catch (Exception ex)
                {
                    StartupLog("Main: EXCEPTION " + ex);
                    CrashLog.Write(ex);
                    MessageBox.Show("程序启动失败：\n" + ex.Message + "\n\n详见 logs 目录。",
                        "Live2DPet", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }
            // 已有实例在运行：唤醒它；稍等后重试（覆盖崩溃自启的释放竞态）
            SignalExistingInstance(activateName);
            if (attempt < 5) Thread.Sleep(400);
        }
    }

    /// <summary>未处理异常时：写日志，若设置允许且未超过自启上限，拉起新进程后由调用方退出。</summary>
    private static void RelaunchAfterCrash(Exception? ex)
    {
        try { CrashLog.Write(ex); } catch { }
        try
        {
            bool allow = true;
            try { allow = SettingsStore.Load(SettingsPath).CrashAutoRestart; } catch { }
            if (!allow) return;

            int count = 0;
            int.TryParse(Environment.GetEnvironmentVariable(RestartEnv), out count);
            if (count >= MaxRestarts) return;   // 连续崩溃过多则放弃，避免死循环

            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            var psi = new ProcessStartInfo(exe) { UseShellExecute = true };
            psi.EnvironmentVariables[RestartEnv] = (count + 1).ToString();
            Process.Start(psi);
        }
        catch { /* 自启失败不致命 */ }
    }

    /// <summary>通知已在运行的实例"把自己显示出来"，然后退出新实例。</summary>
    private static void SignalExistingInstance(string evtName)
    {
        for (int i = 0; i < 15; i++)
        {
            try
            {
                using var evt = EventWaitHandle.OpenExisting(evtName);
                evt.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(200);
            }
        }
        // 兜底：极端情况下新实例直接提示用户
        MessageBox.Show("Live2D 桌宠已经在运行啦~", "Live2DPet",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static void StartupLog(string msg)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "init.log"), $"{DateTime.Now:HH:mm:ss.fff} {msg}\n");
        }
        catch { }
    }
}
