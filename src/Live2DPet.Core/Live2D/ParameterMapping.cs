using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Live2DPet.Core.Live2D;

/// <summary>单个语义参数的映射项（用户可在 parameter-mapping.json 修改 Id / 范围）。</summary>
public sealed class ParamEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>参数取值范围 [min, max]，用于上层裁剪与强度缩放。</summary>
    [JsonPropertyName("range")]
    public float[] Range { get; set; } = { -1f, 1f };
}

/// <summary>参数映射文件结构：defaults 为首选 ID，fallbacks 为找不到时的候选。</summary>
public sealed class ParamMappingFile
{
    [JsonPropertyName("defaults")]
    public Dictionary<string, ParamEntry> Defaults { get; set; } = new();

    [JsonPropertyName("fallbacks")]
    public Dictionary<string, List<string>> Fallbacks { get; set; } = new();
}

/// <summary>解析后的参数：实际模型中的 ID（可能为 null 表示模型没有该参数）。</summary>
public sealed class ResolvedParam
{
    public string? ActualId;
    public float Min;
    public float Max;
    public bool Present;
}

/// <summary>语义参数 → 模型真实参数 ID 的映射与解析。</summary>
public static class ParameterMapping
{
    /// <summary>与 Cubism 标准参数 ID 对齐的默认映射；用户可在 JSON 里覆盖。</summary>
    public static ParamMappingFile Default()
    {
        var d = new Dictionary<string, ParamEntry>
        {
            ["AngleX"]     = new() { Id = "ParamAngleX",     Range = new[] { -30f, 30f } },
            ["AngleY"]     = new() { Id = "ParamAngleY",     Range = new[] { -30f, 30f } },
            ["AngleZ"]     = new() { Id = "ParamAngleZ",     Range = new[] { -30f, 30f } },
            ["BodyAngleX"] = new() { Id = "ParamBodyAngleX", Range = new[] { -10f, 10f } },
            ["EyeBallX"]   = new() { Id = "ParamEyeBallX",   Range = new[] { -1f, 1f } },
            ["EyeBallY"]   = new() { Id = "ParamEyeBallY",   Range = new[] { -1f, 1f } },
            ["Breath"]     = new() { Id = "ParamBreath",     Range = new[] { 0f, 1f } },
        };
        var f = new Dictionary<string, List<string>>
        {
            ["AngleX"]   = new() { "ParamHeadAngleX" },
            ["EyeBallX"] = new() { "ParamEyeLOpen", "ParamEyeROpen" },
            ["EyeBallY"] = new() { "ParamEyeLOpen", "ParamEyeROpen" },
        };
        return new ParamMappingFile { Defaults = d, Fallbacks = f };
    }

    /// <summary>把语义键解析成模型真实参数 ID（精确匹配 → 回退列表 → null）。</summary>
    public static Dictionary<string, ResolvedParam> Resolve(ParamMappingFile map, IEnumerable<string> available)
    {
        var avail = new HashSet<string>(available);
        var result = new Dictionary<string, ResolvedParam>();
        foreach (var kv in map.Defaults)
        {
            string? actual = avail.Contains(kv.Value.Id) ? kv.Value.Id : null;
            if (actual == null && map.Fallbacks.TryGetValue(kv.Key, out var fb))
                actual = fb.FirstOrDefault(avail.Contains);
            result[kv.Key] = new ResolvedParam
            {
                ActualId = actual,
                Min = kv.Value.Range[0],
                Max = kv.Value.Range[1],
                Present = actual != null
            };
        }
        return result;
    }
}
