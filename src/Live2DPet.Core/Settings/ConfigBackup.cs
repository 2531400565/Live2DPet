using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Live2DPet.Core.Settings;

/// <summary>
/// 配置与养成数据的备份 / 还原（换机、重装、误删后的救命功能）。
/// 打包为单个 zip，内容固定为 config 目录下的四个 json（settings/petstate/parameter-mapping/dialogue）。
///
/// 安全约束（防止"还原个备份把程序搞坏"）：
/// - 只接受白名单文件名，压缩包里的其他条目一律忽略；
/// - 拒绝路径穿越（..、绝对路径）与异常大小的条目；
/// - 覆盖前先解析校验（JSON 可反序列化），校验失败则不落盘。
/// </summary>
public static class ConfigBackup
{
    /// <summary>允许备份/还原的文件白名单（相对 config 目录）。</summary>
    private static readonly string[] Allowed =
    {
        "settings.json",
        "petstate.json",
        "parameter-mapping.json",
        "dialogue.json"   // 用户自定义台词：跟随备份/还原，还原后由 ReloadFromDisk 重新加载
    };

    private const long MaxEntryBytes = 8 * 1024 * 1024;   // 单个条目上限，防止畸形压缩包

    /// <summary>导出：把 config 目录下存在的文件打包成 zip。</summary>
    public static bool Export(string configDir, string zipPath, out string error, out int fileCount)
    {
        error = "";
        fileCount = 0;
        try
        {
            var files = new List<string>();
            foreach (var name in Allowed)
            {
                string p = Path.Combine(configDir, name);
                if (File.Exists(p)) files.Add(p);
            }
            if (files.Count == 0)
            {
                error = "config 目录下没有可备份的配置文件。";
                return false;
            }

            string? dir = Path.GetDirectoryName(zipPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(zipPath)) File.Delete(zipPath);

            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            foreach (var f in files)
            {
                zip.CreateEntryFromFile(f, Path.GetFileName(f), CompressionLevel.Optimal);
                fileCount++;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 还原：校验压缩包内的白名单条目，解析通过后才覆盖到 config 目录。
    /// 调用方需在成功后自行重新加载设置与养成状态。
    /// </summary>
    public static bool Import(string zipPath, string configDir, out string error, out int fileCount)
    {
        error = "";
        fileCount = 0;
        try
        {
            if (!File.Exists(zipPath))
            {
                error = "备份文件不存在。";
                return false;
            }

            Directory.CreateDirectory(configDir);

            using var zip = ZipFile.OpenRead(zipPath);
            // 先在内存里校验全部条目，全部通过再落盘（避免"覆盖一半失败"的半吊子状态）
            var pending = new List<(string Name, byte[] Bytes)>();
            foreach (var entry in zip.Entries)
            {
                // 统一分隔符后再判定：Windows 压缩包可能用 '\'，而 Linux 上 '\' 不是分隔符，
                // 若不先归一化，路径穿越条目会在 Linux（CI）上被漏判。
                string raw = entry.FullName.Replace('\\', '/');
                if (raw.Contains("..") || raw.StartsWith('/') || Path.IsPathRooted(entry.FullName))
                {
                    error = $"压缩包内条目路径不合法：{entry.FullName}";
                    return false;
                }

                string name = Path.GetFileName(raw);
                if (name.Length == 0 || !IsAllowed(name)) continue;          // 非白名单：忽略
                if (name != raw) { error = $"压缩包内条目名称不合法：{entry.FullName}"; return false; }
                if (entry.Length is <= 0 or > MaxEntryBytes) { error = $"条目大小异常：{name}"; return false; }

                using var ms = new MemoryStream();
                using (var s = entry.Open()) s.CopyTo(ms);
                var bytes = ms.ToArray();
                if (!TryValidate(name, bytes, out string why)) { error = $"{name} 内容不合法：{why}"; return false; }
                pending.Add((name, bytes));
            }

            if (pending.Count == 0)
            {
                error = "压缩包里没有找到可识别的配置文件。";
                return false;
            }

            foreach (var (name, bytes) in pending)
            {
                File.WriteAllBytes(Path.Combine(configDir, name), bytes);
                fileCount++;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool IsAllowed(string name)
        => Array.Exists(Allowed, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>覆盖前的最小校验：必须是合法 JSON 对象。</summary>
    private static bool TryValidate(string name, byte[] bytes, out string why)
    {
        why = "";
        try
        {
            string text = System.Text.Encoding.UTF8.GetString(bytes).Trim();
            if (text.Length == 0) { why = "空文件"; return false; }
            if (text[0] is not ('{' or '[')) { why = "不是 JSON 对象"; return false; }
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                why = "根节点不是对象";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            why = ex.Message;
            return false;
        }
    }

    /// <summary>生成默认备份文件名（放到"文档\Live2DPet"下）。</summary>
    public static string DefaultExportPath()
    {
        string baseDir;
        try { baseDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }
        catch { baseDir = AppContext.BaseDirectory; }
        if (string.IsNullOrEmpty(baseDir)) baseDir = AppContext.BaseDirectory;
        string dir = Path.Combine(baseDir, "Live2DPet");
        return Path.Combine(dir, $"live2dpet-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
    }
}
