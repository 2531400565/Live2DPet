using System;
using System.IO;
using Live2DPet.Core.Settings;
using Xunit;

namespace Live2DPet.Core.Tests;

/// <summary>
/// C2 settings.json 版本化：_version 写出、旧档缺省按 v1、ReadVersion 钩子、损坏不阻断。
/// </summary>
public class SettingsVersionTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), "l2dp_sv_" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void Save_WritesVersionField()
    {
        var path = TempPath();
        try
        {
            SettingsStore.Save(new AppSettings(), path);
            var raw = File.ReadAllText(path);
            Assert.Contains("\"_version\": 1", raw);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Load_OldFileWithoutVersion_KeepsDataAndDefaultsCurrent()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, """{"PetName":"皮皮"}""");   // 旧档：无 _version
            var loaded = SettingsStore.Load(path);
            Assert.Equal("皮皮", loaded.PetName);
            Assert.Equal(SettingsStore.CurrentVersion, loaded.Version);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Load_ThenSave_BackWritesVersion()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, """{"PetName":"皮皮"}""");
            var loaded = SettingsStore.Load(path);
            SettingsStore.Save(loaded, path);   // 旧档重存后应带 _version:1
            Assert.Contains("\"_version\": 1", File.ReadAllText(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void ReadVersion_MissingFile_ReturnsCurrent()
    {
        var path = Path.Combine(Path.GetTempPath(), "l2dp_nonexist_" + Guid.NewGuid().ToString("N") + ".json");
        Assert.Equal(SettingsStore.CurrentVersion, SettingsStore.ReadVersion(path));
    }

    [Fact]
    public void ReadVersion_ReadsDeclaredNumber()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, """{"_version": 3}""");
            Assert.Equal(3, SettingsStore.ReadVersion(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void ReadVersion_InvalidJson_ReturnsCurrent()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, "{ not json !!");
            Assert.Equal(SettingsStore.CurrentVersion, SettingsStore.ReadVersion(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
