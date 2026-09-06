using System;

namespace Live2DPet.Core.Imaging;

/// <summary>
/// 像素 Alpha 预乘 / 反预乘工具。
/// 渲染管线（PetGlHost）读回的帧是<b>预乘 Alpha</b>——颜色通道已乘以不透明度，
/// 而 GDI+ / PNG 位图使用<b>直 Alpha（非预乘）</b>。
/// 导出透明 PNG 前必须<b>反预乘</b>，否则半透明边缘会因 GDI+ 误当直色而出现黑边/暗边。
/// <para>纯算法、无外部依赖、可单元测试。每像素 4 字节；反预乘只针对三个颜色通道除以 Alpha，
/// 与 R/G/B 的具体字节排布无关（BGRA 与 RGBA 同样适用）。</para>
/// </summary>
public static class AlphaConversion
{
    /// <summary>
    /// 把<b>预乘 Alpha</b> 像素原地转换为<b>直 Alpha</b>（非预乘）。
    /// 每个像素：a==0 → 颜色清零（完全透明）；a==255 → 不变（完全不透明即直色）；
    /// 否则 color = round(color * 255 / a)。
    /// </summary>
    /// <param name="pixels">像素字节数组（长度须为 4 的倍数）；为 null 时直接返回。</param>
    /// <param name="length">参与转换的字节数；省略或负数表示使用整个数组。</param>
    public static void Unpremultiply(byte[]? pixels, int length = -1)
    {
        if (pixels == null) return;
        int n = length < 0 ? pixels.Length : Math.Min(length, pixels.Length);
        for (int i = 0; i + 3 < n; i += 4)
        {
            int a = pixels[i + 3];
            if (a == 0)
            {
                pixels[i] = 0;
                pixels[i + 1] = 0;
                pixels[i + 2] = 0;
                continue;
            }
            if (a == 255) continue;   // 完全不透明：预乘色 == 直色
            double inv = 255.0 / a;
            pixels[i]     = (byte)Math.Min(255, Math.Round(pixels[i]     * inv));
            pixels[i + 1] = (byte)Math.Min(255, Math.Round(pixels[i + 1] * inv));
            pixels[i + 2] = (byte)Math.Min(255, Math.Round(pixels[i + 2] * inv));
        }
    }

    /// <summary>
    /// 把<b>直 Alpha</b> 像素原地转换为<b>预乘 Alpha</b>（用于测试 / 与渲染管线对齐）。
    /// a==0 → 颜色清零；a==255 → 不变；否则 color = round(color * a / 255)。
    /// </summary>
    public static void Premultiply(byte[]? pixels, int length = -1)
    {
        if (pixels == null) return;
        int n = length < 0 ? pixels.Length : Math.Min(length, pixels.Length);
        for (int i = 0; i + 3 < n; i += 4)
        {
            int a = pixels[i + 3];
            if (a == 0)
            {
                pixels[i] = 0;
                pixels[i + 1] = 0;
                pixels[i + 2] = 0;
                continue;
            }
            if (a == 255) continue;   // 完全不透明：直色 == 预乘色
            double f = a / 255.0;
            pixels[i]     = (byte)Math.Min(255, Math.Round(pixels[i]     * f));
            pixels[i + 1] = (byte)Math.Min(255, Math.Round(pixels[i + 1] * f));
            pixels[i + 2] = (byte)Math.Min(255, Math.Round(pixels[i + 2] * f));
        }
    }
}
