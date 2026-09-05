using System;
using System.IO;
using System.IO.Compression;
using Live2DPet.Core.Settings;
using Xunit;

namespace Live2DPet.Core.Tests;

/// <summary>
/// 配置备份 / 还原：换机、重装场景的正确性与安全性（白名单、路径穿越、损坏文件）。
/// </summary>
public class ConfigBackupTests : IDisposable
{
    private readonly string _root;
    private readonly string _configDir;
    private readonly string _workDir;

    public ConfigBackupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "Live2DPetTests", Guid.NewGuid().ToString("N"));
        _configDir = Path.Combine(_root, "config");
        _workDir = Path.Combine(_root, "work");
        Directory.CreateDirectory(_configDir);
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* 清理失败不影响测试 */ }
    }

    private void WriteConfig(string name, string json) => File.WriteAllText(Path.Combine(_configDir, name), json);

    [Fact]
    public void Export_PacksExistingConfigFiles_AndReportsCount()
    {
        WriteConfig("settings.json", "{\"Opacity\":1.0}");
        WriteConfig("petstate.json", "{\"Level\":3}");
        // parameter-mapping.json 不存在：不应被计入

        string zip = Path.Combine(_workDir, "backup.zip");
        bool ok = ConfigBackup.Export(_configDir, zip, out string err, out int count);

        Assert.True(ok, err);
        Assert.Equal(2, count);
        Assert.True(File.Exists(zip));

        using var archive = ZipFile.OpenRead(zip);
        Assert.Contains(archive.Entries, e => e.FullName == "settings.json");
        Assert.Contains(archive.Entries, e => e.FullName == "petstate.json");
        Assert.DoesNotContain(archive.Entries, e => e.FullName == "parameter-mapping.json");
    }

    [Fact]
    public void Export_WithNoConfigFiles_FailsCleanly()
    {
        bool ok = ConfigBackup.Export(_configDir, Path.Combine(_workDir, "empty.zip"), out string err, out int count);

        Assert.False(ok);
        Assert.Equal(0, count);
        Assert.False(string.IsNullOrEmpty(err));
    }

    [Fact]
    public void Import_RestoresFiles_RoundTripKeepsContent()
    {
        const string settingsJson = "{\"Opacity\":0.75,\"Scale\":1.5}";
        WriteConfig("settings.json", settingsJson);
        WriteConfig("petstate.json", "{\"Level\":7}");

        string zip = Path.Combine(_workDir, "round.zip");
        Assert.True(ConfigBackup.Export(_configDir, zip, out _, out _));

        // 故意改坏现有配置，再还原，验证内容确实被覆盖回来
        WriteConfig("settings.json", "{\"Opacity\":0.1}");
        Assert.True(ConfigBackup.Import(zip, _configDir, out string err, out int count), err);

        Assert.Equal(2, count);
        Assert.Equal(settingsJson, File.ReadAllText(Path.Combine(_configDir, "settings.json")));
    }

    [Theory]
    [InlineData("..\\..\\evil.json")]   // Windows 风格分隔符
    [InlineData("../../evil.json")]     // Unix 风格分隔符（CI 跑在 Linux 上）
    public void Import_RejectsArchiveWithPathTraversalEntry(string evilPath)
    {
        string zip = Path.Combine(_workDir, "evil.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry(evilPath);
            using var w = new StreamWriter(entry.Open());
            w.Write("{\"x\":1}");
        }

        bool ok = ConfigBackup.Import(zip, _configDir, out string err, out int count);

        Assert.False(ok);
        Assert.Equal(0, count);
        Assert.Contains("不合法", err);
        Assert.False(File.Exists(Path.Combine(_root, "evil.json")));
    }

    [Fact]
    public void Import_RejectsNonJsonContent_WithoutTouchingDisk()
    {
        WriteConfig("settings.json", "{\"Keep\":true}");
        string before = File.ReadAllText(Path.Combine(_configDir, "settings.json"));

        string zip = Path.Combine(_workDir, "bad.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("settings.json");
            using var w = new StreamWriter(entry.Open());
            w.Write("this is definitely not json");
        }

        bool ok = ConfigBackup.Import(zip, _configDir, out string err, out _);

        Assert.False(ok);
        Assert.False(string.IsNullOrEmpty(err));
        // 校验失败必须"一个字节都不落盘"
        Assert.Equal(before, File.ReadAllText(Path.Combine(_configDir, "settings.json")));
    }

    [Fact]
    public void Import_MissingFile_FailsWithMessage()
    {
        bool ok = ConfigBackup.Import(Path.Combine(_workDir, "nope.zip"), _configDir, out string err, out _);
        Assert.False(ok);
        Assert.Contains("不存在", err);
    }

    [Fact]
    public void Import_IgnoresNonWhitelistedEntries()
    {
        string zip = Path.Combine(_workDir, "mixed.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(archive.CreateEntry("petstate.json").Open())) w.Write("{\"Level\":2}");
            using (var w = new StreamWriter(archive.CreateEntry("suspicious.exe").Open())) w.Write("MZ fake");
        }

        Assert.True(ConfigBackup.Import(zip, _configDir, out string err, out int count), err);

        Assert.Equal(1, count);
        Assert.True(File.Exists(Path.Combine(_configDir, "petstate.json")));
        Assert.False(File.Exists(Path.Combine(_configDir, "suspicious.exe")));
    }
}
