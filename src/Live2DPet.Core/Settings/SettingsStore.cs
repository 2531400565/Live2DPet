using System;
using System.IO;
using System.Text.Json;

namespace Live2DPet.Core.Settings;

/// <summary>
/// 用户设置的 JSON 持久化：读写 config/settings.json。
/// 主文件损坏 → 回退上一版快照 settings.prev.json（写坏不静默丢配置）；
/// 主文件缺失（首次运行或被删除=用户重置）→ 直接用默认值。
/// 读取/写入异常写入 storage-errors.log 留痕，不吞掉排查线索。
/// </summary>
public static class SettingsStore
{
    /// <summary>当前 settings.json 结构版本。未来结构升级时递增，并用 ReadVersion 驱动迁移。</summary>
    public const int CurrentVersion = 1;

    /// <summary>JSON 里的版本字段名（下划线开头，与 dialogue.json 的 _version 约定一致）。</summary>
    private const string VersionKey = "_version";

    /// <summary>
    /// 读取配置文件声明的结构版本：文件缺失/损坏/无 _version 时返回当前版本（缺省按 v1 兼容）。
    /// 留给未来升级做迁移判断的只读钩子，不改动文件。
    /// </summary>
    public static int ReadVersion(string path)
    {
        try
        {
            if (!File.Exists(path)) return CurrentVersion;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return CurrentVersion;
            if (root.TryGetProperty(VersionKey, out var v) && v.ValueKind == JsonValueKind.Number)
                return v.GetInt32();
            return CurrentVersion;   // 旧文件没有 _version：视为当前版本
        }
        catch
        {
            return CurrentVersion;   // 损坏：交给 Load 的 prev 回退逻辑，此处不阻断
        }
    }

    public static AppSettings Load(string path)
    {
        var main = TryLoad(path, out bool corrupt);
        if (main != null) return FocusConfig.Normalize(main);

        // 仅当主文件"存在但损坏"才回退上一版快照；缺失（被删除/首次运行）→ 默认，
        // 避免用户手动删除 settings.json 想重置时又被 prev 旧值"复活"。
        if (corrupt)
        {
            var prev = TryLoad(PrevPath(path), out _);
            if (prev != null) return FocusConfig.Normalize(prev);
        }
        return FocusConfig.Normalize(new AppSettings());
    }

    public static void Save(AppSettings settings, string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // 写前：若旧文件仍完好，先留一份 prev 快照；损坏则跳过（防止把坏内容传给 prev）
            if (File.Exists(path))
            {
                try
                {
                    var old = TryLoad(path, out bool oldCorrupt);
                    if (old != null && !oldCorrupt)
                        File.Copy(path, PrevPath(path), overwrite: true);
                }
                catch (Exception ex)
                {
                    StateLog.Warn(path, "settings", "保存前生成 prev 快照失败: " + ex.Message);
                }
            }

            File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            StateLog.Warn(path, "settings", "写入失败: " + ex.Message);
        }
    }

    /// <summary>读取并解析；文件缺失返回 null(corrupt=false)，存在但损坏/不可读返回 null(corrupt=true)。</summary>
    private static AppSettings? TryLoad(string path, out bool corrupt)
    {
        corrupt = false;
        try
        {
            if (!File.Exists(path)) return null;
            var txt = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(txt);
            if (settings == null) { corrupt = true; return null; }  // 内容是 "null"
            if (settings.Version > CurrentVersion)
                StateLog.Warn(path, "settings", $"settings.json 版本 {settings.Version} 高于当前 {CurrentVersion}，未知字段将按当前版本尽力处理");
            else if (settings.Version <= 0)
                settings.Version = CurrentVersion;   // 文件里 _version 缺失/异常：归位当前版本，下次保存即写回
            return settings;
        }
        catch (Exception ex)
        {
            corrupt = true;
            StateLog.Warn(path, "settings", "读取/解析失败: " + ex.Message);
            return null;
        }
    }

    private static string PrevPath(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? ".";
        return Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + ".prev.json");
    }
}
