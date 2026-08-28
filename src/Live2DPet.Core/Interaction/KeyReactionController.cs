using System;
using System.Collections.Generic;

namespace Live2DPet.Core.Interaction;

/// <summary>
/// 键盘互动的反应决策（纯逻辑，无引擎/Win32 依赖，可单测）。
///
/// 设计目标：键盘是高频事件，若每次按键都重放动作会显得角色一直在"刷新动作"，
/// 很不合理。因此改为「打字爆发」检测：
///   - 只在短时间内连续敲了足够多的键（判定为正在打字）才偶尔反应一次；
///   - 反应后进入冷却，避免连续刷屏；
///   - 反应统一用轻量的 Tap（小回应），不让角色剧烈抖动。
///
/// 单点散按（如按一下方向键、调音量）不会触发，符合"桌宠偶尔回应你打字"的预期。
///
/// 虚拟键码速查：
///   0x0D Enter / 0x20 Space  → Tap
///   方向键/其它               → 仍归为 Tap（打字用轻点就好）
/// </summary>
public sealed class KeyReactionController
{
    private readonly Queue<DateTime> _recent = new();
    private DateTime _lastReaction = DateTime.MinValue;

    private readonly TimeSpan _cooldown;
    private readonly TimeSpan _window;
    private readonly int _minKeys;

    public KeyReactionController(
        TimeSpan? cooldown = null,
        TimeSpan? window = null,
        int minKeys = 3)
    {
        _cooldown = cooldown ?? TimeSpan.FromSeconds(3);   // 反应后至少静 3 秒
        _window = window ?? TimeSpan.FromSeconds(2.5);     // 多键落的统计窗口
        _minKeys = minKeys;                                // 窗口内至少敲几键才算"在打字"
    }

    /// <summary>
    /// 评估一次按键互动。返回要播放的分组名；若不在打字爆发中或仍在冷却期则返回 null（忽略）。
    /// </summary>
    public string? Consider(int vkCode, DateTime now)
    {
        // 1) 记录本次按键，丢弃超出统计窗口的旧记录
        _recent.Enqueue(now);
        while (_recent.Count > 0 && now - _recent.Peek() > _window)
            _recent.Dequeue();

        // 2) 冷却期内直接忽略
        if (now - _lastReaction < _cooldown)
            return null;

        // 3) 还没达到"正在打字"的阈值，忽略（单点散按不触发）
        if (_recent.Count < _minKeys)
            return null;

        // 4) 触发一次反应，并清空计数，避免紧接着又连发
        _lastReaction = now;
        _recent.Clear();
        return GroupFor(vkCode);
    }

    private static string GroupFor(int vkCode)
    {
        // 打字互动统一用轻量 Tap，角色只是小小回应一下，不会剧烈刷新动作
        return "Tap";
    }
}
