using System;
using System.Linq;
using Live2DPet.Core.Live2D;
using Xunit;

namespace Live2DPet.Core.Tests;

public class IdleMotionSelectorTests
{
    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(IdleMotionSelector.Select(Array.Empty<string>()));
        Assert.Empty(IdleMotionSelector.Select(null!));
    }

    [Fact]
    public void HasIdleGroup_ReturnsOnlyIdle()
    {
        var all = new[] { "Tap", "Flick", "Idle", "TapBody", "Pinch" };
        var r = IdleMotionSelector.Select(all);
        Assert.Equal(new[] { "Idle" }, r);
    }

    [Fact]
    public void IdleMatch_IsCaseInsensitive()
    {
        var r = IdleMotionSelector.Select(new[] { "Tap", "IDLE" });
        Assert.Equal(new[] { "IDLE" }, r);
    }

    [Fact]
    public void NoIdleGroup_ExcludesInteractionGroups()
    {
        var all = new[] { "Tap", "IdleLoop", "Breathe", "Flick", "Shake", "wave" };
        var r = IdleMotionSelector.Select(all);
        Assert.Equal(new[] { "IdleLoop", "Breathe", "wave" }, r);   // 保持顺序、剔除 Tap/Flick/Shake
    }

    [Fact]
    public void NoIdleGroup_AndOnlyInteractionGroups_ReturnsEmpty()
    {
        var r = IdleMotionSelector.Select(new[] { "Tap", "Flick", "Tap@Body", "PinchIn", "PinchOut", "Pinch", "Shake", "TapBody" });
        Assert.Empty(r);
    }
}
