using System;
using System.IO;

namespace Live2DPet.Core;

/// <summary>
/// 存储层极简日志：把状态文件（petstate/settings）的损坏、读写或备份异常
/// 追加到与数据文件同目录的 storage-errors.log，便于事后排查
/// "存档/配置为何被回退或丢失"。写入自身失败静默忽略，绝不掩盖原始问题。
/// </summary>
public static class StateLog
{
    private static readonly object _lock = new();

    /// <summary>记录一条告警。nearFilePath 用于推导日志所在目录（与数据文件同目录）。</summary>
    public static void Warn(string nearFilePath, string tag, string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(nearFilePath);
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "storage-errors.log");
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{tag}] {message}\r\n";
            lock (_lock)
                File.AppendAllText(path, line);
        }
        catch
        {
            // 日志写不进去也不能影响主流程
        }
    }
}
