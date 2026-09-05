using System;
using Live2DPet.Core.Update;
using Xunit;

namespace Live2DPet.Core.Tests;

public class UpdateTests
{
    private const string SampleReleaseJson = """
    {
      "tag_name": "v1.2.0",
      "name": "Live2DPet v1.2.0",
      "body": "**Full Changelog**: https://github.com/2531400565/Live2DPet/compare/v1.1.0...v1.2.0",
      "prerelease": false,
      "assets": [
        { "name": "other.txt", "browser_download_url": "https://example.com/other.txt", "size": 1, "digest": "" },
        { "name": "Live2DPet-v1.2.0-win-x64.zip", "browser_download_url": "https://example.com/Live2DPet-v1.2.0-win-x64.zip", "size": 19049554, "digest": "sha256:be986d12f51a90dd51e1fbffb7c77bd31866a2a1254ad0bf2ba2ea8159ca27f1" }
      ]
    }
    """;

    [Fact]
    public void ParseRelease_PicksZipAsset_AndStripsShaPrefix()
    {
        var info = GitHubUpdateClient.ParseRelease(SampleReleaseJson);
        Assert.NotNull(info);
        Assert.Equal("v1.2.0", info!.Tag);
        Assert.Equal(new Version(1, 2, 0), info.Version);
        Assert.Equal("Live2DPet v1.2.0", info.Name);
        Assert.Equal("https://example.com/Live2DPet-v1.2.0-win-x64.zip", info.DownloadUrl);
        Assert.Equal("be986d12f51a90dd51e1fbffb7c77bd31866a2a1254ad0bf2ba2ea8159ca27f1", info.Sha256);
        Assert.Equal(19049554, info.Size);
        Assert.False(info.Prerelease);
    }

    [Fact]
    public void ParseRelease_ReturnsNull_OnInvalidJson()
    {
        Assert.Null(GitHubUpdateClient.ParseRelease("not json"));
        Assert.Null(GitHubUpdateClient.ParseRelease("{\"tag_name\":\"\"}"));
        Assert.Null(GitHubUpdateClient.ParseRelease("[]"));
    }

    [Theory]
    [InlineData("1.1.0", "1.2.0", true)]
    [InlineData("1.1.0.0", "1.1.0", false)]   // 同版本（仅格式差异）→ 不更新
    [InlineData("2.0.0", "1.2.0", false)]
    [InlineData("1.1.9", "1.2.0", true)]
    [InlineData("1.1.0", "1.1.0", false)]
    public void IsNewer_BehavesCorrectly(string current, string latest, bool expected)
    {
        var c = Version.Parse(current);
        var l = Version.Parse(latest);
        Assert.Equal(expected, GitHubUpdateClient.IsNewer(c, l));
    }

    [Theory]
    [InlineData("sha256:ABC123", "abc123", true)]
    [InlineData("ABC123", "abc123", true)]
    [InlineData("abc123", "ABC123", true)]
    [InlineData("abc", "def", false)]
    public void ShaMatches_IgnoresCaseAndPrefix(string expected, string actual, bool match)
    {
        Assert.Equal(match, GitHubUpdateClient.ShaMatches(expected, actual));
    }
}
