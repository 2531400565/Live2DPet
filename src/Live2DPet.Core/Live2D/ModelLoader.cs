using System.IO;
using System.Text.Json;

namespace Live2DPet.Core.Live2D;

/// <summary>
/// 模型加载辅助：确保参数映射文件存在（首次运行写入默认映射），
/// 并解析模型真实参数 → 语义键。
/// </summary>
public static class ModelLoader
{
    /// <summary>读取映射文件；不存在则用默认映射生成一份（用户可后续编辑）。</summary>
    public static ParamMappingFile LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                var txt = File.ReadAllText(path);
                var parsed = JsonSerializer.Deserialize<ParamMappingFile>(txt);
                if (parsed is { Defaults.Count: > 0 }) return parsed;
            }
            catch
            {
                // 解析失败则重建默认文件
            }
        }

        var def = ParameterMapping.Default();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var opts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(def, opts));
        return def;
    }
}
