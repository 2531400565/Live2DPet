using System;

namespace Live2DPet.Core.Settings;

/// <summary>
/// 免打扰（专注）时段判定：纯函数，便于单元测试。
/// 规则：仅在启用且起止分钟数不同时生效；支持跨午夜区间（如 23:00 → 08:00，
/// 即 DndStart &gt; DndEnd 时"当前 &gt;= 起点 或 当前 &lt; 终点"）。
/// </summary>
public static class DndClock
{
    /// <summary>判断给定时刻是否落在免打扰时段内（now 为本地时间）。</summary>
    public static bool IsActive(AppSettings settings, DateTime now)
    {
        if (!settings.DndEnabled) return false;
        int current = now.Hour * 60 + now.Minute;
        int s = settings.DndStart, e = settings.DndEnd;
        if (s == e) return false;   // 起止相同视为未配置
        return s < e ? (current >= s && current < e) : (current >= s || current < e);
    }
}
