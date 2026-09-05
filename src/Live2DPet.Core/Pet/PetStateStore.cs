using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Live2DPet.Core.Pet;

/// <summary>
/// 养成状态的 JSON 持久化：读写 config/petstate.json。
/// 读取失败/缺失 → 回退到上一版快照或最新时间戳备份；写入失败静默忽略（不致命）。
/// </summary>
public static class PetStateStore
{
    private const int MaxBackups = 5;
    // 时间戳备份节流：至少间隔 5 分钟才生成一份新快照，避免每次保存都把备份目录刷爆
    private static DateTime _lastTimestampedBackup = DateTime.MinValue;

    public static PetState Load(string path)
    {
        // 主文件优先
        var main = TryLoad(path);
        if (main != null) return main;

        // 主文件缺失/损坏 → 依次回退：petstate.prev.json → 最新的时间戳备份
        foreach (var bak in EnumerateBackups(path))
        {
            var fb = TryLoad(bak);
            if (fb != null) return fb;
        }
        return new PetState();
    }

    private static PetState? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var txt = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PetState>(txt);
        }
        catch
        {
            return null;   // 损坏/格式不符
        }
    }

    public static void Save(PetState state, string path)
    {
        try
        {
            state.LastSeen = DateTime.UtcNow;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // 写入前：若已有"完好"的旧版本，先留一份上一版快照；再按节流生成带时间戳快照
            if (File.Exists(path))
            {
                var prev = TryLoad(path);   // 仅当旧文件可解析才值得备份（避免把损坏内容传下去）
                if (prev != null && !string.IsNullOrEmpty(dir))
                {
                    try
                    {
                        string prevPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + ".prev.json");
                        File.Copy(path, prevPath, overwrite: true);

                        var now = DateTime.UtcNow;
                        if ((now - _lastTimestampedBackup).TotalMinutes >= 5)
                        {
                            _lastTimestampedBackup = now;
                            var bakDir = Path.Combine(dir, "backups");
                            Directory.CreateDirectory(bakDir);
                            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                            string bakName = Path.GetFileNameWithoutExtension(path) + "_" + stamp + ".bak";
                            File.Copy(path, Path.Combine(bakDir, bakName), overwrite: false);
                            PruneBackups(Path.Combine(bakDir, Path.GetFileNameWithoutExtension(path) + "_*.bak"));
                        }
                    }
                    catch (Exception ex)
                    {
                        StateLog.Warn(path, "petstate", "生成 prev/时间戳备份失败: " + ex.Message);
                    }
                }
            }

            File.WriteAllText(path, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 写入失败不致命
        }
    }

    private static void PruneBackups(string searchPattern)
    {
        try
        {
            var dir = Path.GetDirectoryName(searchPattern);
            var name = Path.GetFileName(searchPattern);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return;
            foreach (var f in Directory.GetFiles(dir, name)
                         .OrderByDescending(x => new FileInfo(x).LastWriteTimeUtc)
                         .Skip(MaxBackups))
                File.Delete(f);
        }
        catch (Exception ex)
        {
            StateLog.Warn(searchPattern, "petstate", "清理旧备份失败: " + ex.Message);
        }
    }

    private static System.Collections.Generic.IEnumerable<string> EnumerateBackups(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir)) yield break;
        var baseName = Path.GetFileNameWithoutExtension(path);
        // 1) 上一版快照
        yield return Path.Combine(dir, baseName + ".prev.json");
        // 2) 时间戳快照（最新在前）
        var bakDir = Path.Combine(dir, "backups");
        if (Directory.Exists(bakDir))
        {
            foreach (var f in Directory.GetFiles(bakDir, baseName + "_*.bak")
                         .OrderByDescending(x => new FileInfo(x).LastWriteTimeUtc))
                yield return f;
        }
    }

    /// <summary>彻底清空养成数据：删除主文件、上一版快照与全部时间戳备份（用于"重置养成"）。
    /// 失败静默忽略——下一次 Save 会重新生成干净的主文件。</summary>
    public static void Purge(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                var baseName = Path.GetFileNameWithoutExtension(path);
                SafeDelete(Path.Combine(dir, baseName + ".prev.json"));
                var bakDir = Path.Combine(dir, "backups");
                if (Directory.Exists(bakDir))
                {
                    foreach (var f in Directory.GetFiles(bakDir, baseName + "_*.bak"))
                        SafeDelete(f);
                }
            }
            SafeDelete(path);
        }
        catch (Exception ex) { StateLog.Warn(path, "petstate", "重置清理失败: " + ex.Message); /* 不致命 */ }
    }

    private static void SafeDelete(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); }
        catch (Exception ex) { StateLog.Warn(file, "petstate", "删除文件失败: " + ex.Message); }
    }
}
