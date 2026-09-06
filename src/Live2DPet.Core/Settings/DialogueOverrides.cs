using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using Live2DPet.Core.Pet;

namespace Live2DPet.Core.Settings;

/// <summary>
/// 用户自定义台词（config/dialogue.json）。
/// 设计要点：
/// <list type="number">
/// <item><b>逐组合并</b>：12 组各自独立，缺失 / 空数组 / 全是空白的组自动用内置台词补齐，
/// 永远不会出现"宠物没话说"的空窗。</item>
/// <item><b>容错优先</b>：文件损坏、字段类型不对、IO 失败一律回退内置台词并回传原因；
/// 不抛异常，也不覆盖用户写坏的文件（留给用户自己修）。</item>
/// <item><b>热生效</b>：改完文件在托盘点「重新加载台词」即可，无需重启。</item>
/// <item><b>零依赖</b>：只用 BCL 的 System.Text.Json，不引入任何 NuGet 包。</item>
/// </list>
/// </summary>
public sealed class DialogueOverrides
{
    /// <summary>JSON 中的注释字段名（下划线开头，解析时忽略，可随意删）。</summary>
    public const string CommentKey = "_comment";

    /// <summary>单组最多保留多少句（超出截断，避免超大文件拖慢启动）。</summary>
    public const int MaxLinesPerGroup = 50;

    /// <summary>单句最大长度（超出截断，气泡宽度有限，太长会溢出）。</summary>
    public const int MaxLineLength = 200;

    /// <summary>可覆盖的分组名；顺序即写入 JSON 的顺序，与 <see cref="PetDialogue"/> 内置快照一一对应。</summary>
    public static readonly string[] GroupNames =
    {
        "Greeting", "Pet", "Feed", "Play", "Bathe", "Idle",
        "Wake", "Sleep", "Startle", "Break", "Hungry", "HappyIdle"
    };

    /// <summary>每组中文说明，写进 JSON 的 _comment 模板里，方便用户照着改。</summary>
    private static readonly (string Name, string Desc)[] GroupDocs =
    {
        ("Greeting",  "普通问候：启动、非首次见面时"),
        ("Pet",       "抚摸 / 点击身体时的回应"),
        ("Feed",      "喂食成功的回应"),
        ("Play",      "陪玩成功的回应"),
        ("Bathe",     "洗澡成功的回应"),
        ("Idle",      "待机时的碎碎念"),
        ("Wake",      "你回来啦，宠物从打盹中醒来"),
        ("Sleep",     "你长时间离开，宠物进入打盹"),
        ("Startle",   "被拎起来（拖拽）瞬间的惊吓台词"),
        ("Break",     "久坐休息提醒"),
        ("Hungry",    "饿了，求投喂"),
        ("HappyIdle", "状态全满时的活泼待机台词")
    };

    /// <summary>普通问候。</summary>
    public string[]? Greeting { get; set; }

    /// <summary>抚摸 / 点击。</summary>
    public string[]? Pet { get; set; }

    /// <summary>喂食。</summary>
    public string[]? Feed { get; set; }

    /// <summary>陪玩。</summary>
    public string[]? Play { get; set; }

    /// <summary>洗澡。</summary>
    public string[]? Bathe { get; set; }

    /// <summary>待机碎碎念。</summary>
    public string[]? Idle { get; set; }

    /// <summary>醒来。</summary>
    public string[]? Wake { get; set; }

    /// <summary>打盹。</summary>
    public string[]? Sleep { get; set; }

    /// <summary>被拖拽时受惊。</summary>
    public string[]? Startle { get; set; }

    /// <summary>休息提醒。</summary>
    public string[]? Break { get; set; }

    /// <summary>饿了。</summary>
    public string[]? Hungry { get; set; }

    /// <summary>状态全满时的活泼待机。</summary>
    public string[]? HappyIdle { get; set; }

    /// <summary>实际生效（非空）的组数：0 表示完全使用内置台词。</summary>
    public int Count
    {
        get
        {
            int n = 0;
            foreach (var g in GroupNames)
                if (Sanitize(Get(g)) != null) n++;
            return n;
        }
    }

    /// <summary>是否一份空的覆盖（等价于"全部用内置台词"）。</summary>
    public bool IsEmpty => Count == 0;

    /// <summary>按标准分组名取该组自定义台词（未配置返回 null）。</summary>
    public string[]? Get(string? group) => Canonical(group) switch
    {
        "Greeting"  => Greeting,
        "Pet"       => Pet,
        "Feed"      => Feed,
        "Play"      => Play,
        "Bathe"     => Bathe,
        "Idle"      => Idle,
        "Wake"      => Wake,
        "Sleep"     => Sleep,
        "Startle"   => Startle,
        "Break"     => Break,
        "Hungry"    => Hungry,
        "HappyIdle" => HappyIdle,
        _           => null
    };

    /// <summary>设置某组台词（自动清洗；传 null / 空数组等价于"该组回退内置"）。</summary>
    public void Set(string? group, string[]? lines)
    {
        var clean = Sanitize(lines);
        switch (Canonical(group))
        {
            case "Greeting":  Greeting  = clean; break;
            case "Pet":       Pet       = clean; break;
            case "Feed":      Feed      = clean; break;
            case "Play":      Play      = clean; break;
            case "Bathe":     Bathe     = clean; break;
            case "Idle":      Idle      = clean; break;
            case "Wake":      Wake      = clean; break;
            case "Sleep":     Sleep     = clean; break;
            case "Startle":   Startle   = clean; break;
            case "Break":     Break     = clean; break;
            case "Hungry":    Hungry    = clean; break;
            case "HappyIdle": HappyIdle = clean; break;
        }
    }

    /// <summary>把任意大小写 / 带空白的分组名规范成标准名；不是已知分组返回 null。</summary>
    public static string? Canonical(string? group)
    {
        if (string.IsNullOrWhiteSpace(group)) return null;
        foreach (var n in GroupNames)
            if (string.Equals(n, group.Trim(), StringComparison.OrdinalIgnoreCase)) return n;
        return null;
    }

    /// <summary>是否为可覆盖的已知分组名。</summary>
    public static bool IsKnownGroup(string? group) => Canonical(group) != null;

    /// <summary>
    /// 清洗一组台词：剔除空白句、去掉首尾空格、截断超长句与总条数。
    /// 全空或 null 返回 null —— 表示"不使用自定义，回退内置台词"。
    /// </summary>
    public static string[]? Sanitize(string[]? lines)
    {
        if (lines == null || lines.Length == 0) return null;

        var list = new List<string>(Math.Min(lines.Length, MaxLinesPerGroup));
        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var s = raw.Trim();
            if (s.Length > MaxLineLength) s = s.Substring(0, MaxLineLength);
            list.Add(s);
            if (list.Count >= MaxLinesPerGroup) break;   // 超出部分直接丢弃
        }
        return list.Count == 0 ? null : list.ToArray();
    }

    /// <summary>把自定义台词应用到 <see cref="PetDialogue"/>（立即生效，无需重启）。</summary>
    public void Apply() => PetDialogue.ApplyOverrides(this);

    /// <summary>
    /// 用内置台词补齐缺失的分组，返回是否发生了补齐。
    /// 用于"文件里少了某组"时把模板补全，方便用户下次照着改。
    /// </summary>
    public bool EnsureComplete()
    {
        bool changed = false;
        foreach (var g in GroupNames)
        {
            if (Sanitize(Get(g)) != null) continue;
            Set(g, PetDialogue.BuiltinFor(g));
            changed = true;
        }
        return changed;
    }

    /// <summary>生成一份"12 组全是内置台词"的完整覆盖，用作首次生成的模板。</summary>
    public static DialogueOverrides FromBuiltin()
    {
        var o = new DialogueOverrides();
        o.EnsureComplete();
        return o;
    }

    /// <summary>
    /// 加载配置文件（任何情况下都不抛异常）：
    /// <list type="bullet">
    /// <item>文件不存在 → 生成带 <c>_comment</c> 注释的完整模板（内容为内置台词），返回空覆盖，<paramref name="created"/>=true；</item>
    /// <item>文件存在但缺分组 → 用内置台词补齐并回写，<paramref name="created"/>=false；</item>
    /// <item>文件损坏 / 不可读 → 回退内置台词，<paramref name="error"/> 写明原因，<b>不改动用户文件</b>。</item>
    /// </list>
    /// </summary>
    public static DialogueOverrides LoadOrCreate(string path, out string? error, out bool created)
    {
        error = null;
        created = false;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(path))
            {
                if (!Save(path, FromBuiltin(), out string? saveErr))
                    error = "生成台词模板失败: " + saveErr;
                created = true;
                return new DialogueOverrides();   // 模板内容 == 内置台词，无需覆盖
            }

            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "台词文件是空的，已回退内置台词";
                return new DialogueOverrides();
            }

            var o = Parse(text, out error);
            if (o == null) return new DialogueOverrides();   // 损坏 → 空覆盖（全部用内置）

            // 缺失项自动补齐：把少掉的分组补回文件里，用户下次打开就能照着改
            if (o.EnsureComplete()) Save(path, o, out _);

            return o;
        }
        catch (Exception ex)
        {
            error = "读取台词文件失败: " + ex.Message;
            return new DialogueOverrides();
        }
    }

    /// <summary>解析 JSON 文本；损坏返回 null 并通过 <paramref name="error"/> 给出原因。
    /// 未知字段（含 <c>_comment</c>）静默忽略，不因多余字段报错。</summary>
    public static DialogueOverrides? Parse(string json, out string? error)
    {
        error = null;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,   // 容忍手写的 // 与 /* */ 注释
                AllowTrailingCommas = true
            });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "台词文件根节点不是对象";
                return null;
            }

            var o = new DialogueOverrides();
            foreach (var prop in root.EnumerateObject())
            {
                var name = Canonical(prop.Name);
                if (name == null) continue;                  // _comment / 未知分组：忽略
                var clean = Sanitize(ReadLines(prop.Value));
                if (clean != null) o.Set(name, clean);
            }
            return o;
        }
        catch (JsonException ex)
        {
            error = "台词文件 JSON 格式有误: " + ex.Message;
            return null;
        }
    }

    /// <summary>读取一个分组的值：字符串数组优先；单个字符串按一句处理；其余类型视为未配置。</summary>
    private static string[]? ReadLines(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Array:
            {
                var list = new List<string>();
                foreach (var item in value.EnumerateArray())
                    // 数组里混进数字 / null / 对象：跳过该条，不因一条脏数据废掉整组
                    if (item.ValueKind == JsonValueKind.String)
                        list.Add(item.GetString() ?? string.Empty);
                return list.ToArray();
            }
            case JsonValueKind.String:
                return new[] { value.GetString() ?? string.Empty };
            default:
                return null;   // null / 数字 / 布尔 / 嵌套对象 → 当作没配
        }
    }

    /// <summary>写入配置文件（<c>_comment</c> 注释头 + 固定分组顺序）。失败返回 false 并给出原因，不抛异常。</summary>
    public static bool Save(string path, DialogueOverrides overrides, out string? error)
    {
        error = null;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions
            {
                Indented = true,
                // 默认编码器会把中文转成 \uXXXX，用户没法直接读改；本地配置文件放宽即可
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }))
            {
                w.WriteStartObject();

                w.WriteStartArray(CommentKey);
                foreach (var line in CommentLines()) w.WriteStringValue(line);
                w.WriteEndArray();

                foreach (var name in GroupNames)
                {
                    var lines = Sanitize(overrides.Get(name)) ?? PetDialogue.BuiltinFor(name);
                    w.WriteStartArray(name);
                    foreach (var l in lines) w.WriteStringValue(l);
                    w.WriteEndArray();
                }

                w.WriteEndObject();
            }

            File.WriteAllBytes(path, stream.ToArray());
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>写进 JSON 顶部的 <c>_comment</c> 注释模板（纯说明，解析时忽略，可随意删）。</summary>
    public static IReadOnlyList<string> CommentLines()
    {
        var head = new[]
        {
            "Live2DPet 台词自定义 —— config/dialogue.json",
            "",
            "【怎么用】",
            "  1. 改下面任意一组里的句子并保存即可；每组随机抽取，多写几句更耐听。",
            "  2. 想恢复内置台词：把整组写成 []，或直接删掉这一组。",
            "  3. 改完不用重启：托盘右键 →「重新加载台词」立即生效。",
            "  4. 文件写坏了也不会崩：自动回退内置台词，日志里会有提示。",
            "",
            "【占位符】",
            "  {name} = 宠物昵称（设置页「外观」里改），例：\"{name}等你好久啦~\"",
            "",
            "【分组说明】"
        };
        return head.Concat(GroupDocs.Select(d => $"  {d.Name,-10} {d.Desc}")).ToArray();
    }
}
