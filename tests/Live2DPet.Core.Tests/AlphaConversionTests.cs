using Live2DPet.Core.Imaging;
using Xunit;

namespace Live2DPet.Core.Tests;

/// <summary>
/// AlphaConversion 单测：覆盖预乘 → 反预乘（导出去黑边）的核心算法与边界。
/// 不依赖 System.Drawing / 渲染管线，纯字节级验证。
/// </summary>
public class AlphaConversionTests
{
    [Fact]
    public void Unpremultiply_Alpha255_LeavesColorsUnchanged()
    {
        // 完全不透明：预乘色 == 直色，应原样保留
        var px = new byte[] { 10, 20, 30, 255 };
        AlphaConversion.Unpremultiply(px);
        Assert.Equal(new byte[] { 10, 20, 30, 255 }, px);
    }

    [Fact]
    public void Unpremultiply_Alpha0_ClearsColors()
    {
        // 完全透明：无论通道值多少，反预乘后应为 0,0,0,0
        var px = new byte[] { 7, 8, 9, 0 };
        AlphaConversion.Unpremultiply(px);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, px);
    }

    [Fact]
    public void Unpremultiply_RecoversStraightColor_FromPremultiplied()
    {
        // 直色 (255,128,64) 在 a=128 时的预乘值应为 (128,64,32,128)
        var premult = new byte[] { 128, 64, 32, 128 };
        AlphaConversion.Unpremultiply(premult);
        Assert.Equal(new byte[] { 255, 128, 64, 128 }, premult);
    }

    [Fact]
    public void Premultiply_StraightColor_BecomesPremultiplied()
    {
        var straight = new byte[] { 255, 128, 64, 128 };
        AlphaConversion.Premultiply(straight);
        Assert.Equal(new byte[] { 128, 64, 32, 128 }, straight);
    }

    [Fact]
    public void Premultiply_Then_Unpremultiply_RoundTrips()
    {
        var straight = new byte[] { 255, 128, 64, 128 };
        var expected = (byte[])straight.Clone();
        AlphaConversion.Premultiply(straight);
        Assert.Equal(new byte[] { 128, 64, 32, 128 }, straight);
        AlphaConversion.Unpremultiply(straight);
        Assert.Equal(expected, straight);   // 直色 → 预乘 → 直色，应无损还原
    }

    [Fact]
    public void Unpremultiply_HandlesMultiplePixels_AndIgnoresAlphaChannel()
    {
        // 两像素：第1个半透明预乘，第2个不透明
        var px = new byte[]
        {
            128, 64, 32, 128,   // → (255,128,64,128)
            200, 50, 10, 255    // 不透明：原样
        };
        AlphaConversion.Unpremultiply(px);
        Assert.Equal(new byte[]
        {
            255, 128, 64, 128,
            200, 50, 10, 255
        }, px);
    }

    [Fact]
    public void Unpremultiply_PartialAlpha_DarkEdgeAvoided()
    {
        // 预乘 (100,50,25,128)（暗值≈黑边来源）应被反乘回亮直色：
        // r=round(100*255/128)=199、g=round(50*255/128)=100、b=round(25*255/128)=50
        var px = new byte[] { 100, 50, 25, 128 };
        AlphaConversion.Unpremultiply(px);
        Assert.Equal(new byte[] { 199, 100, 50, 128 }, px);
    }

    [Fact]
    public void Unpremultiply_NullBuffer_IsNoOp()
    {
        // 不应抛异常
        AlphaConversion.Unpremultiply(null);
        AlphaConversion.Unpremultiply(null, 16);
    }
}
