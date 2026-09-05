using System;
using System.Collections.Generic;
using Live2DPet.Core.Pet;
using Live2DPet.Core.Settings;
using Live2DPet.Rendering;

namespace Live2DPet.App;

/// <summary>
/// 宿主门面：<see cref="PetApplication"/> 实现，向交互/调度两个服务暴露它们需要的
/// 状态与表现能力。关键设计是"活引用"——State/Settings 每次 get 都返回宿主当前的
/// 字段值，因此重置养成 / 还原备份 / 切换设置后服务拿到的永远是同一份最新数据，
/// 无需在状态替换点同步更新服务内部缓存。
/// 约定：所有方法都必须在 UI 线程调用（服务方法由宿主在 UI 线程触发）。
/// </summary>
internal interface IPetHost
{
    /// <summary>当前养成状态（宿主字段的活引用）。</summary>
    PetState State { get; }

    /// <summary>当前设置（宿主字段的活引用）。</summary>
    AppSettings Settings { get; }

    /// <summary>Live2D 引擎门面（动作/表情播放），可能为空（初始化失败）。</summary>
    Live2DManager? Live2D { get; }

    /// <summary>音效管理器，可能为空。</summary>
    SoundManager? Sound { get; }

    /// <summary>宿主是否已进入释放流程（各周期任务据此提前退出）。</summary>
    bool IsDisposed { get; }

    /// <summary>最近一次真实互动时刻（UTC）。调度器据此判断"空闲多久"。</summary>
    DateTime LastInteraction { get; set; }

    /// <summary>当前模型适合待机的动作分组（模型切换后由宿主刷新）。</summary>
    IReadOnlyList<string> IdleMotionGroups { get; set; }

    /// <summary>桌宠当前是否处于"可见可互动"状态（未彻底隐藏、未被拖拽、未贴边离屏）。</summary>
    bool IsPetInteractive { get; }

    /// <summary>把养成状态落盘。</summary>
    void SavePetState();

    /// <summary>在角色头顶弹气泡（须在 UI 线程）。</summary>
    void Say(string text);

    /// <summary>环境气泡：免打扰时段内自动抑制。</summary>
    void SayAmbient(string text);

    /// <summary>设置临时情绪（覆盖由养成状态推导的基础情绪），seconds 秒后回落。</summary>
    void SetTransientMood(PetMood mood, double seconds);
}
