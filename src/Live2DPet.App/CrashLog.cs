using System;
using System.IO;
using System.Text;

namespace Live2DPet.App;

/// <summary>
/// 崩溃日志：把未处理异常（含内部异常链）写入 exe 旁 logs/crash-*.log，便于反馈定位。
/// 写入失败静默忽略，绝不掩盖原始异常。
/// </summary>
public static class CrashLog
{
    private static readonly object _lock = new();

    public static void Init()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write(e.ExceptionObject as Exception);
    }

    public static void Write(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");
            lock (_lock)
                File.WriteAllText(path, Format(ex));
        }
        catch
        {
            // 写日志失败不能掩盖原始异常
        }
    }

    private static string Format(Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Unhandled exception");
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            sb.AppendLine("----------------------------------------");
            sb.AppendLine($"Type:    {e.GetType().FullName}");
            sb.AppendLine($"Message: {e.Message}");
            sb.AppendLine("StackTrace:");
            sb.AppendLine(e.StackTrace);
        }
        return sb.ToString();
    }
}
