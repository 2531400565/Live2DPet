using System.IO;
using Live2DPet.Core.Pet;
using Live2DPet.Core.Settings;
using Xunit;

namespace Live2DPet.Core.Tests;

/// <summary>台词自定义（config/dialogue.json）：清洗 / 合并补齐 / 损坏回退 / 首次生成 / 热应用。</summary>
public class DialogueOverridesTests
{
    // ---- 清洗 Sanitize ----

    [Fact]
    public void Sanitize_DropsBlankAndTrims()
    {
        var clean = DialogueOverrides.Sanitize(new[] { "  hi  ", "", "  ", "yo" });
        Assert.Equal(new[] { "hi", "yo" }, clean);
    }

    [Fact]
    public void Sanitize_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(DialogueOverrides.Sanitize(null));
        Assert.Null(DialogueOverrides.Sanitize(Array.Empty<string>()));
        Assert.Null(DialogueOverrides.Sanitize(new[] { "  ", "" }));
    }

    [Fact]
    public void Sanitize_TruncatesLongLine()
    {
        var longLine = new string('x', DialogueOverrides.MaxLineLength + 50);
        var clean = DialogueOverrides.Sanitize(new[] { longLine });
        Assert.Single(clean!);
        Assert.Equal(DialogueOverrides.MaxLineLength, clean![0].Length);
    }

    [Fact]
    public void Sanitize_CapsGroupCount()
    {
        var many = new string[DialogueOverrides.MaxLinesPerGroup + 10];
        for (int i = 0; i < many.Length; i++) many[i] = "line" + i;
        var clean = DialogueOverrides.Sanitize(many);
        Assert.Equal(DialogueOverrides.MaxLinesPerGroup, clean!.Length);
    }

    // ---- 分组名规范 ----

    [Theory]
    [InlineData("Greeting", "Greeting")]
    [InlineData("greeting", "Greeting")]
    [InlineData(" GREETING ", "Greeting")]
    [InlineData("Idle", "Idle")]
    public void Canonical_NormalizesKnownGroup(string input, string expected)
        => Assert.Equal(expected, DialogueOverrides.Canonical(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("NotARealGroup")]
    [InlineData("_comment")]
    public void Canonical_UnknownOrBlank_ReturnsNull(string? input)
        => Assert.Null(DialogueOverrides.Canonical(input));

    [Fact]
    public void IsKnownGroup_AgreesWithCanonical()
    {
        Assert.True(DialogueOverrides.IsKnownGroup("break"));
        Assert.False(DialogueOverrides.IsKnownGroup("xyz"));
    }

    // ---- Get / Set ----

    [Fact]
    public void SetThenGet_RoundTripsThroughSanitize()
    {
        var o = new DialogueOverrides();
        Assert.Null(o.Get("Greeting"));            // 未配置
        o.Set("Greeting", new[] { " 你好 ", "" });
        Assert.Equal(new[] { "你好" }, o.Get("Greeting"));
    }

    [Fact]
    public void Set_NullOrEmpty_RevertsToBuiltinFallback()
    {
        var o = new DialogueOverrides();
        o.Set("Feed", new[] { "投喂成功！" });
        o.Set("Feed", null);                        // 等价于"该组回退内置"
        Assert.Null(o.Get("Feed"));
    }

    // ---- 内置台词快照 ----

    [Fact]
    public void BuiltinFor_ReturnsCloneNotSharedReference()
    {
        var a = PetDialogue.BuiltinFor("Greeting");
        var b = PetDialogue.BuiltinFor("Greeting");
        Assert.Equal(a, b);
        Assert.NotSame(a, b);
        a[0] = "MUTATED";
        Assert.NotEqual(a[0], PetDialogue.BuiltinFor("Greeting")[0]);
    }

    [Fact]
    public void BuiltinFor_UnknownGroup_ReturnsEmpty()
        => Assert.Empty(PetDialogue.BuiltinFor("Nope"));

    // ---- 合并补齐 EnsureComplete / FromBuiltin ----

    [Fact]
    public void FromBuiltin_AllTwelveGroupsPopulated()
    {
        var o = DialogueOverrides.FromBuiltin();
        foreach (var g in DialogueOverrides.GroupNames)
            Assert.NotNull(o.Get(g));
        Assert.Equal(DialogueOverrides.GroupNames.Length, o.Count);
    }

    [Fact]
    public void EnsureComplete_FillsMissingGroupsFromBuiltin()
    {
        var o = new DialogueOverrides();
        o.Set("Greeting", new[] { "自定义问候" });
        bool changed = o.EnsureComplete();
        Assert.True(changed);
        Assert.Equal("自定义问候", o.Get("Greeting")![0]);
        Assert.NotNull(o.Get("Feed"));           // 补齐
        Assert.Equal(DialogueOverrides.GroupNames.Length, o.Count);
    }

    [Fact]
    public void EnsureComplete_AlreadyComplete_ReturnsFalse()
    {
        var o = DialogueOverrides.FromBuiltin();
        Assert.False(o.EnsureComplete());
    }

    // ---- 解析 Parse（容错）----

    [Fact]
    public void Parse_ValidJson_MergesKnownGroups()
    {
        const string json = """
        {
          "Greeting": ["早安，{name}~"],
          "Feed": ["开饭啦"]
        }
        """;
        var o = DialogueOverrides.Parse(json, out string? err);
        Assert.Null(err);
        Assert.Equal(new[] { "早安，{name}~" }, o!.Get("Greeting"));
        Assert.Equal(new[] { "开饭啦" }, o.Get("Feed"));
        Assert.Null(o.Get("Idle"));               // 未给 → 回退内置（null 表示用内置）
    }

    [Fact]
    public void Parse_ToleratesTrailingCommasAndComments()
    {
        const string json = """
        {
          // 这是注释，解析应忽略
          "Idle": ["发呆中",],
          "_comment": ["随便写的说明"],
          "UnknownGroup": ["被忽略"]
        }
        """;
        var o = DialogueOverrides.Parse(json, out string? err);
        Assert.Null(err);
        Assert.Equal(new[] { "发呆中" }, o!.Get("Idle"));
        Assert.Null(o.Get("UnknownGroup"));       // 未知分组忽略
    }

    [Fact]
    public void Parse_SingleStringTreatedAsOneLine()
    {
        var o = DialogueOverrides.Parse("""{ "Pet": "摸摸头" }""", out _);
        Assert.Equal(new[] { "摸摸头" }, o!.Get("Pet"));
    }

    [Fact]
    public void Parse_NonObjectRoot_ReturnsNullWithError()
    {
        var o = DialogueOverrides.Parse("""[1,2,3]""", out string? err);
        Assert.Null(o);
        Assert.NotNull(err);
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsNullWithError()
    {
        var o = DialogueOverrides.Parse("""{ "Greeting": """, out string? err);
        Assert.Null(o);
        Assert.NotNull(err);
    }

    [Fact]
    public void Parse_ArrayWithBadElement_SkipsDirtyItem()
    {
        // 数组里混入数字/对象：仅跳过脏条目，不废掉整组
        const string json = """
        { "HappyIdle": ["元气满满", 123, { "x": 1 }, "转圈圈"] }
        """;
        var o = DialogueOverrides.Parse(json, out _);
        Assert.Equal(new[] { "元气满满", "转圈圈" }, o!.Get("HappyIdle"));
    }

    // ---- 加载 / 首次生成 / 损坏回退 ----

    private static string TempJson()
        => Path.Combine(Path.GetTempPath(), "live2dpet_dlg_test_" + System.Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void LoadOrCreate_FileMissing_CreatesTemplateAndReportsCreated()
    {
        string path = TempJson();
        try
        {
            var o = DialogueOverrides.LoadOrCreate(path, out string? err, out bool created);
            Assert.True(created);
            Assert.Null(err);
            Assert.True(File.Exists(path));
            Assert.True(o.IsEmpty);               // 模板内容 == 内置台词 → 等价"全用内置"
            // 生成的文件能被再次解析且含 _comment
            string text = File.ReadAllText(path);
            Assert.Contains("_comment", text);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void LoadOrCreate_ValidFile_LoadsOverrides()
    {
        string path = TempJson();
        try
        {
            File.WriteAllText(path, """{ "Sleep": ["我先眯一会儿~"] }""");
            var o = DialogueOverrides.LoadOrCreate(path, out _, out bool created);
            Assert.False(created);
            Assert.Equal(new[] { "我先眯一会儿~" }, o.Get("Sleep"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void LoadOrCreate_MissingGroup_AutoCompletedAndWrittenBack()
    {
        string path = TempJson();
        try
        {
            File.WriteAllText(path, """{ "Startle": ["吓一跳！"] }""");   // 只给了 1 组
            DialogueOverrides.LoadOrCreate(path, out _, out _);
            string text = File.ReadAllText(path);
            // 补齐后文件应包含所有 12 个分组名
            foreach (var g in DialogueOverrides.GroupNames)
                Assert.Contains("\"" + g + "\"", text);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void LoadOrCreate_CorruptedFile_FallsBackToBuiltinAndDoesNotOverwrite()
    {
        string path = TempJson();
        try
        {
            File.WriteAllText(path, """{ 这不是合法 json """);
            var o = DialogueOverrides.LoadOrCreate(path, out string? err, out bool created);
            Assert.NotNull(err);
            Assert.False(created);
            Assert.True(o.IsEmpty);               // 回退内置
            // 用户文件原样保留（不覆盖），方便用户自己修
            Assert.Contains("这不是合法", File.ReadAllText(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void LoadOrCreate_EmptyFile_FallsBackWithError()
    {
        string path = TempJson();
        try
        {
            File.WriteAllText(path, "   ");
            var o = DialogueOverrides.LoadOrCreate(path, out string? err, out _);
            Assert.NotNull(err);
            Assert.True(o.IsEmpty);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ---- 热应用 Apply / ApplyOverrides / ResetToBuiltin ----

    [Fact]
    public void Apply_MergesCustomAndFallsBackBuiltinForMissing()
    {
        var builtinGreeting = (string[])PetDialogue.Greetings.Clone();
        try
        {
            var o = new DialogueOverrides();
            o.Set("Greeting", new[] { "自定义早安" });
            o.Apply();
            Assert.Equal(new[] { "自定义早安" }, PetDialogue.Greetings);
            Assert.Equal(PetDialogue.BuiltinFor("Feed"), PetDialogue.FeedReplies); // 未覆盖 → 内置
        }
        finally { PetDialogue.ResetToBuiltin(); }

        Assert.Equal(builtinGreeting, PetDialogue.Greetings);   // 全局状态已还原
    }

    [Fact]
    public void ApplyOverrides_Null_ResetsAllToBuiltin()
    {
        try
        {
            PetDialogue.ApplyOverrides(new DialogueOverrides
            {
                Greeting = new[] { "临时" }
            });
            Assert.Equal(new[] { "临时" }, PetDialogue.Greetings);
            PetDialogue.ResetToBuiltin();
            Assert.Equal(PetDialogue.BuiltinFor("Greeting"), PetDialogue.Greetings);
        }
        finally { PetDialogue.ResetToBuiltin(); }
    }

    // ---- 保存 Save 可读性（中文不转义 + _comment 头）----

    [Fact]
    public void Save_WritesReadableChineseAndCommentHeader()
    {
        string path = TempJson();
        try
        {
            var o = new DialogueOverrides();
            o.Set("Wake", new[] { "你回来啦，{name}醒啦~" });
            Assert.True(DialogueOverrides.Save(path, o, out _));
            string text = File.ReadAllText(path);
            Assert.Contains("_comment", text);
            Assert.Contains("你回来啦，{name}醒啦~", text);   // 中文未被 \uXXXX 转义
            Assert.DoesNotContain("\\u", text);
            // 写出的文件能原样读回
            var back = DialogueOverrides.Parse(File.ReadAllText(path), out _);
            Assert.Equal(new[] { "你回来啦，{name}醒啦~" }, back!.Get("Wake"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ---- Count / IsEmpty ----

    [Fact]
    public void Count_ReflectsEffectiveGroupsOnly()
    {
        var o = new DialogueOverrides();
        Assert.Equal(0, o.Count);
        Assert.True(o.IsEmpty);
        o.Set("Break", new[] { "休息一下" });
        Assert.Equal(1, o.Count);
        Assert.False(o.IsEmpty);
    }
}
