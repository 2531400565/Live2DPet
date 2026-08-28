using System;
using Microsoft.Win32;

namespace Live2DPet.Platform;

/// <summary>
/// 开机自启：读写 HKCU\Software\Microsoft\Windows\CurrentVersion\Run 下的启动项。
/// 值为当前可执行文件路径（带引号）。写失败静默忽略（不致命）。
/// </summary>
public static class AutoStartManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Live2DPet";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) != null;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue(ValueName, "\"" + exe + "\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // 注册表写入失败不致命（例如被策略禁止）
        }
    }
}
