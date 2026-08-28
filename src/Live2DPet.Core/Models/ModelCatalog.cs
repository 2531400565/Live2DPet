using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Live2DPet.Core.Models;

/// <summary>一个可用的 Live2D 模型。</summary>
public sealed record ModelInfo
{
    /// <summary>稳定标识（相对 assets/models 的目录路径，用 / 分隔），用于持久化。</summary>
    public required string Id { get; init; }
    /// <summary>显示名（模型所在目录名）。</summary>
    public required string DisplayName { get; init; }
    /// <summary>模型目录绝对路径（含结尾分隔符），供引擎加载。</summary>
    public required string Dir { get; init; }
    /// <summary>model3.json 文件名（不含 .model3.json），供引擎加载（LoadModel 会拼成 dir/name.model3.json）。</summary>
    public required string Name { get; init; }
}

/// <summary>扫描 assets/models 下所有可加载模型（递归查找 *.model3.json）。</summary>
public static class ModelCatalog
{
    public static IReadOnlyList<ModelInfo> Scan(string modelsRoot)
    {
        var list = new List<ModelInfo>();
        if (!Directory.Exists(modelsRoot)) return list;

        foreach (var json in Directory.EnumerateFiles(modelsRoot, "*.model3.json", SearchOption.AllDirectories))
        {
            var dir = Path.GetDirectoryName(json);
            if (dir == null) continue;
            // 关键：双层剥扩展名。文件名形如 "hiyori_free_t08.model3.json"，
            // GetFileNameWithoutExtension 只剥最后一个 ".json"，会留下 "hiyori_free_t08.model3"，
            // 引擎 LoadModel 拿到这个名字就会去拼 "...model3\...model3.json" 而失败。
            // 再剥一次 ".model3" 才得到引擎期望的模型名。
            var name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(json));
            var displayName = Path.GetFileName(dir);
            var rel = Path.GetRelativePath(modelsRoot, dir).Replace('\\', '/');
            list.Add(new ModelInfo
            {
                Id = rel,
                DisplayName = displayName,
                Dir = dir + Path.DirectorySeparatorChar,
                Name = name
            });
        }

        return list.OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }
}