using System.IO;
using System.Text.Json;

namespace Live2DPet.Core.Settings;

/// <summary>
/// 用户设置的 JSON 持久化：读写 config/settings.json。
/// 读取失败或文件缺失时回退默认值；写入失败不致命（静默忽略）。
/// </summary>
public static class SettingsStore
{
    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var txt = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(txt);
                if (settings != null) return settings;
            }
        }
        catch
        {
            // 文件损坏或格式不符 → 回退默认
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings, string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 写入失败不致命（例如目录只读）
        }
    }
}
