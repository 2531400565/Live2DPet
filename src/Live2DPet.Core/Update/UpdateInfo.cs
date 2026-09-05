using System;

namespace Live2DPet.Core.Update;

/// <summary>一个可用的更新版本信息（从 GitHub Release 解析）。</summary>
public sealed class UpdateInfo
{
    /// <summary>Git tag，如 "v1.2.0"。</summary>
    public string Tag { get; init; } = "";

    /// <summary>解析后的语义版本（去 v、去预发布后缀）。</summary>
    public Version Version { get; init; } = new(0, 0, 0);

    /// <summary>Release 标题。</summary>
    public string Name { get; init; } = "";

    /// <summary>Release 说明（Markdown）。</summary>
    public string Body { get; init; } = "";

    /// <summary>zip 资产下载地址。</summary>
    public string DownloadUrl { get; init; } = "";

    /// <summary>zip 资产的 SHA256（可能带 "sha256:" 前缀），用于下载后校验。</summary>
    public string Sha256 { get; init; } = "";

    /// <summary>zip 资产字节大小。</summary>
    public long Size { get; init; }

    /// <summary>是否为预发布（默认不参与自动更新）。</summary>
    public bool Prerelease { get; init; }
}
