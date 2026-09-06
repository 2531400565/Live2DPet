using System.IO;
using Live2DPet.Core.Pet;
using Live2DPet.Core.Settings;
using Xunit;

namespace Live2DPet.Core.Tests;

/// <summary>v1.3 宠物昵称：默认值与设置持久化往返。</summary>
public class PetNameTests : IDisposable
{
    private readonly string _dir;

    public PetNameTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "l2dpet_nametests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 清理失败忽略 */ }
    }

    [Fact]
    public void AppSettings_DefaultPetName_IsDialogueDefault()
    {
        Assert.Equal(PetDialogue.DefaultPetName, new AppSettings().PetName);
    }

    [Fact]
    public void SettingsStore_SaveLoad_RoundTripsPetName()
    {
        string path = Path.Combine(_dir, "settings.json");

        var settings = new AppSettings { PetName = "小埋" };
        SettingsStore.Save(settings, path);

        var loaded = SettingsStore.Load(path);
        Assert.Equal("小埋", loaded.PetName);
    }

    [Fact]
    public void SettingsStore_MissingFile_UsesDefaultPetName()
    {
        // 首次运行（无配置文件）应拿到默认昵称，而不是 null/空串
        var loaded = SettingsStore.Load(Path.Combine(_dir, "nope.json"));
        Assert.Equal(PetDialogue.DefaultPetName, loaded.PetName);
    }

    [Fact]
    public void SettingsStore_OldFileWithoutPetName_FallsBackToNewDefault()
    {
        // 老版本升级：settings.json 里还没有 PetName 字段
        string path = Path.Combine(_dir, "legacy.json");
        File.WriteAllText(path, "{ \"Opacity\": 0.8, \"Fps\": 30 }");

        var loaded = SettingsStore.Load(path);

        Assert.Equal(PetDialogue.DefaultPetName, loaded.PetName);
        Assert.Equal(0.8, loaded.Opacity);   // 其余字段不受影响
    }
}
