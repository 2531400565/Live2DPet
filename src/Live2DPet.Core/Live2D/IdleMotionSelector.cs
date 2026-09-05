using System;
using System.Collections.Generic;
using System.Linq;

namespace Live2DPet.Core.Live2D;

/// <summary>
/// 从模型可用的动作分组里选出"适合待机"的那一组：优先取名为 Idle 的分组；
/// 若模型没有 Idle 分组，则退回"所有非互动分组"（Tap/TapBody/Flick/Pinch/Shake 等
/// 触发式动作不用于随机待机，避免与用户的点按互动语义混淆）。
/// 纯函数，便于单元测试。
/// </summary>
public static class IdleMotionSelector
{
    private static readonly HashSet<string> InteractionGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        "Tap", "TapBody", "Tap@Body", "Flick", "PinchIn", "PinchOut", "Pinch", "Shake"
    };

    /// <summary>从全部动作分组中挑选待机分组（保持输入顺序；空输入返回空列表）。</summary>
    public static IReadOnlyList<string> Select(IEnumerable<string> allGroups)
    {
        var all = allGroups as string[] ?? allGroups?.ToArray() ?? Array.Empty<string>();
        if (all.Length == 0) return Array.Empty<string>();

        var idle = all.Where(g => g.Equals("Idle", StringComparison.OrdinalIgnoreCase)).ToList();
        if (idle.Count > 0) return idle;

        return all.Where(g => !InteractionGroups.Contains(g)).ToList();
    }
}
