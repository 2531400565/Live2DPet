using System;
using System.IO;
using System.Text;

namespace Live2DPet.Core;

/// <summary>
/// 轻量结构化日志：写入 exe 旁 logs/app.log，按大小滚动，最多保留 3 个历史文件。
/// 设计原则：
/// - 任何写入失败都必须静默吞掉——日志绝不能反过来搞崩程序（桌宠是常驻进程）；
/// - 线程安全（渲染/钩子/窗口线程都会写）；
/// - 不引第三方依赖，Core 为 net8.0，App/Platform/Rendering 均可引用。
/// </summary>
public static class AppLog
{
    private const long MaxFileBytes = 256 * 1024;   // 单个日志文件上限
    private const int MaxBackups = 3;               // 保留的历史文件数量

    private static readonly object _lock = new();
    private static string? _dir;

    public enum Level { Info, Warn, Error }

    /// <summary>日志文件所在目录（首次写入时创建）。</summary>
    public static string DirectoryPath
    {
        get
        {
            if (_dir == null)
            {
                _dir = Path.Combine(AppContext.BaseDirectory, "logs");
                try { System.IO.Directory.CreateDirectory(_dir); } catch { }
            }
            return _dir;
        }
    }

    public static string FilePath => Path.Combine(DirectoryPath, "app.log");

    public static void Info(string msg) => Write(Level.Info, msg);
    public static void Warn(string msg) => Write(Level.Warn, msg);
    public static void Error(string msg) => Write(Level.Error, msg);

    /// <summary>记录异常（含内部异常链）。不抛出、不返回。</summary>
    public static void Error(Exception ex, string? context = null)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(context)) sb.Append(context).Append(" | ");
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            sb.Append(e.GetType().Name).Append(": ").Append(e.Message);
            if (e.InnerException != null) sb.Append(" <- ");
        }
        Write(Level.Error, sb.ToString());
        if (ex.StackTrace != null) Write(Level.Error, ex.StackTrace);
    }

    private static void Write(Level level, string msg)
    {
        if (string.IsNullOrEmpty(msg)) return;
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant()}] {msg}";
        try
        {
            lock (_lock)
            {
                string path = FilePath;
                RollIfNeeded(path);
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // 写日志失败一律忽略
        }
    }

    /// <summary>当前日志超过上限时滚动：app.log → app.1.log → … → app.3.log（最旧的丢弃）。</summary>
    private static void RollIfNeeded(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaxFileBytes) return;

            for (int i = MaxBackups; i >= 1; i--)
            {
                string src = i == 1 ? path : Path.Combine(DirectoryPath, $"app.{i - 1}.log");
                string dst = Path.Combine(DirectoryPath, $"app.{i}.log");
                if (!File.Exists(src)) continue;
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(src, dst);
            }
        }
        catch
        {
            // 滚动失败不影响本次写入
        }
    }

    /// <summary>在资源管理器中打开日志目录（托盘"查看日志"用）。</summary>
    public static void OpenFolder()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = DirectoryPath,
                UseShellExecute = true
            });
        }
        catch
        {
            // 打开失败忽略
        }
    }
}
