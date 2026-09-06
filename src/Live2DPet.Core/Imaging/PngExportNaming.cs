using System;
using System.IO;

namespace Live2DPet.Core.Imaging;

/// <summary>
/// 透明 PNG 导出的文件名 / 路径生成（纯函数，不触碰 IO，便于单元测试）。
/// 约定：默认保存到"图片"库下的 <see cref="SubFolder"/> 子目录，文件名按本地时间戳命名。
/// </summary>
public static class PngExportNaming
{
    /// <summary>导出子目录名（位于"图片"库下）。</summary>
    public const string SubFolder = "Live2DPet";

    /// <summary>文件名格式：<c>Pet_yyyy-MM-dd_HHmmss.png</c>（基于本地时间，秒级精度避免覆盖）。</summary>
    public static string BuildFileName(DateTime when)
        => "Pet_" + when.ToString("yyyy-MM-dd_HHmmss") + ".png";

    /// <summary>
    /// 完整保存路径：<c>&lt;图片库&gt;\Live2DPet\Pet_yyyy-MM-dd_HHmmss.png</c>。
    /// <paramref name="picturesDir"/> 由调用方用 <c>Environment.GetFolderPath(MyPictures)</c> 传入，
    /// 这里只负责拼接，保持纯函数、可在单测中注入任意目录。
    /// </summary>
    public static string BuildSavePath(string picturesDir, DateTime when)
        => Path.Combine(picturesDir, SubFolder, BuildFileName(when));
}
