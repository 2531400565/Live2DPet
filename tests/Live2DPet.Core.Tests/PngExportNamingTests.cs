using System.IO;
using System.Text.RegularExpressions;
using Live2DPet.Core.Imaging;
using Xunit;

namespace Live2DPet.Core.Tests;

/// <summary>
/// PngExportNaming 单测：文件名格式与保存路径拼接（纯函数，注入任意目录均可测）。
/// </summary>
public class PngExportNamingTests
{
    private static readonly Regex FileNamePattern =
        new(@"^Pet_\d{4}-\d{2}-\d{2}_\d{6}\.png$", RegexOptions.None);

    [Fact]
    public void SubFolder_IsLive2DPet()
    {
        Assert.Equal("Live2DPet", PngExportNaming.SubFolder);
    }

    [Fact]
    public void BuildFileName_MatchesPattern_AndIsSecondPrecise()
    {
        var when = new System.DateTime(2026, 9, 6, 21, 20, 8);
        string name = PngExportNaming.BuildFileName(when);
        Assert.Equal("Pet_2026-09-06_212008.png", name);
        Assert.Matches(FileNamePattern, name);
    }

    [Fact]
    public void BuildFileName_ZeroPadsMonthDayTime()
    {
        var when = new System.DateTime(2026, 1, 2, 3, 4, 5);
        Assert.Equal("Pet_2026-01-02_030405.png", PngExportNaming.BuildFileName(when));
    }

    [Fact]
    public void BuildSavePath_CombinesPicturesDir_SubFolder_AndFileName()
    {
        var when = new System.DateTime(2026, 9, 6, 21, 20, 8);
        string path = PngExportNaming.BuildSavePath(@"C:\Users\me\Pictures", when);

        Assert.Equal(
            Path.Combine(@"C:\Users\me\Pictures", "Live2DPet", "Pet_2026-09-06_212008.png"),
            path);
        Assert.EndsWith("Pet_2026-09-06_212008.png", path);
        Assert.Contains("Live2DPet", path);
    }

    [Fact]
    public void BuildSavePath_WithTrailingSeparator_DoesNotDoubleSeparate()
    {
        // Path.Combine 对尾斜杠目录也能正确拼接，不出现双分隔符歧义
        string path = PngExportNaming.BuildSavePath(@"/pics/", System.DateTime.Now);
        Assert.DoesNotContain(@"//Live2DPet", path.Replace('\\', '/'));
        Assert.Contains("Live2DPet", path);
    }
}
