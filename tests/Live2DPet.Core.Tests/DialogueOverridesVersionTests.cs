using System.IO;
using Live2DPet.Core.Settings;
using Xunit;

namespace Live2DPet.Core.Tests;

/// <summary>台词配置 _version：写入 / 忽略所有 _ 开头字段 / 缺失按 v1 向后兼容 / 过高版本回退。</summary>
public class DialogueOverridesVersionTests
{
    private static string TempJson()
        => Path.Combine(Path.GetTempPath(), "live2dpet_dlg_ver_" + System.Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void Parse_IgnoresVersionAndCommentFields()
    {
        const string json = """
        {
          "_version": 1,
          "_comment": ["随便写的说明"],
          "Idle": ["发呆中"]
        }
        """;
        var o = DialogueOverrides.Parse(json, out string? err);
        Assert.Null(err);
        Assert.Equal(new[] { "发呆中" }, o!.Get("Idle"));
        Assert.Null(o.Get("_version"));        // 元字段不会变成分组
        Assert.Null(o.Get("_comment"));
    }

    [Fact]
    public void Parse_IgnoresUnknownUnderscoreField()
    {
        const string json = """
        {
          "_custom": "随便什么",
          "_version": 1,
          "Pet": ["摸摸头"]
        }
        """;
        var o = DialogueOverrides.Parse(json, out string? err);
        Assert.Null(err);
        Assert.Equal(new[] { "摸摸头" }, o!.Get("Pet"));
    }

    [Fact]
    public void ReadVersion_MissingDefaultsToCurrent()
    {
        // 缺 _version → 视为当前版本（向后兼容旧文件）
        var root = System.Text.Json.JsonDocument.Parse("{ \"Idle\": [] }").RootElement;
        Assert.Equal(DialogueOverrides.CurrentVersion, DialogueOverrides.ReadVersion(root));
    }

    [Fact]
    public void ReadVersion_ParsesNumberOrString()
    {
        Assert.Equal(2, DialogueOverrides.ReadVersion(System.Text.Json.JsonDocument.Parse("{ \"_version\": 2 }").RootElement));
        Assert.Equal(3, DialogueOverrides.ReadVersion(System.Text.Json.JsonDocument.Parse("{ \"_version\": \"3\" }").RootElement));
    }

    [Fact]
    public void ReadVersion_InvalidValueFallsBackToCurrent()
    {
        Assert.Equal(DialogueOverrides.CurrentVersion,
            DialogueOverrides.ReadVersion(System.Text.Json.JsonDocument.Parse("{ \"_version\": \"abc\" }").RootElement));
    }

    [Fact]
    public void Parse_FutureVersion_ReturnsNullWithError()
    {
        var o = DialogueOverrides.Parse("""{ "_version": 99, "Idle": ["x"] }""", out string? err);
        Assert.Null(o);
        Assert.NotNull(err);
        Assert.Contains("99", err!);
    }

    [Fact]
    public void Save_WritesVersionAndCommentHeader()
    {
        string path = TempJson();
        try
        {
            var o = new DialogueOverrides();
            o.Set("Wake", new[] { "你回来啦" });
            Assert.True(DialogueOverrides.Save(path, o, out _));
            string text = File.ReadAllText(path);
            Assert.Contains("_version", text);
            Assert.Contains(DialogueOverrides.CurrentVersion.ToString(), text);
            Assert.Contains("_comment", text);
            // 写出的文件能被原样解析，且元字段被忽略
            var back = DialogueOverrides.Parse(File.ReadAllText(path), out string? err);
            Assert.Null(err);
            Assert.Equal(new[] { "你回来啦" }, back!.Get("Wake"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void LoadOrCreate_MissingFile_GeneratesVersion()
    {
        string path = TempJson();
        try
        {
            DialogueOverrides.LoadOrCreate(path, out _, out bool created);
            Assert.True(created);
            string text = File.ReadAllText(path);
            Assert.Contains("_version", text);
            Assert.Contains("_comment", text);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
