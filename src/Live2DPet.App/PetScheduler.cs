using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Live2DPet.Core.Pet;
using Live2DPet.Platform.Native;

namespace Live2DPet.App;

/// <summary>
/// 调度服务：承载所有"由时间驱动的低频行为"——每分钟的状态衰减与在线时长记账、
/// 整点/半点报时、久坐休息提醒、20~45s 随机间隔的待机动作、以及长时间无键鼠操作
/// 时的"打盹"进出。高频引擎推进（渲染帧）不在此列，仍由宿主 <c>RenderTick</c> 负责。
/// 注意：构造函数必须在 UI 线程调用（WinForms Timer 依赖消息循环），
/// 所有状态经 <see cref="IPetHost"/> 活引用读取，不缓存可替换对象。
/// </summary>
internal sealed class PetScheduler : IDisposable
{
    private static readonly Random Rnd = new();

    private readonly IPetHost _host;

    private System.Windows.Forms.Timer? _decayTimer;
    private System.Windows.Forms.Timer? _idleTimer;
    private System.Windows.Forms.Timer? _sleepTimer;

    // 在线时长精确累计：记录上次记账时刻，按真实经过秒数累加（避免崩溃丢整分钟）
    private DateTime _onlineStamp = DateTime.UtcNow;

    // 打盹状态（长时间无键鼠操作进入睡觉待机）
    private bool _sleeping;

    private DateTime _lastChime = DateTime.MinValue;
    private int _breakTickCount;
    private const int BreakEveryTicks = 45;   // 约每 45 分钟提醒一次

    public PetScheduler(IPetHost host)
    {
        _host = host;

        // 状态衰减定时器（每分钟）：衰减 + 低状态提醒 + 报时 + 休息提醒
        _decayTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _decayTimer.Tick += (_, _) => OnDecayTick();
        _decayTimer.Start();

        // 待机随机动作调度器（低频，仅在空闲时偶发）
        _idleTimer = new System.Windows.Forms.Timer { Interval = 20_000 };
        _idleTimer.Tick += (_, _) => OnIdleTick();
        _idleTimer.Start();

        // 离开检测：用户长时间无键鼠操作 → 进入"打盹"待机（每 10s 轮询一次空闲时长）
        _sleepTimer = new System.Windows.Forms.Timer { Interval = 10_000 };
        _sleepTimer.Tick += (_, _) => OnSleepCheck();
        _sleepTimer.Start();
    }

    /// <summary>重置在线时长记账基准（启动 / 系统唤醒 / 校时 / 重置养成 / 还原备份时调用）。</summary>
    public void ResetOnlineStamp() => _onlineStamp = DateTime.UtcNow;

    /// <summary>把"距上次记账"的在线秒数补进状态（退出前调用，避免丢最后一段）。</summary>
    public void FlushOnline()
    {
        var now = DateTime.UtcNow;
        long fd = (long)(now - _onlineStamp).TotalSeconds;
        if (fd > 0) _host.State.TotalOnlineSeconds += fd;
    }

    /// <summary>每分钟状态衰减 + 低状态提醒 + 整点/半点报时 + 休息提醒。</summary>
    private void OnDecayTick()
    {
        if (_host.IsDisposed) return;
        bool wasHungry = _host.State.IsHungry, wasPlay = _host.State.WantsPlay, wasDirty = _host.State.IsDirty;
        _host.State.Decay(1.0);
        // 在线时长按真实经过秒数累加（精确，避免崩溃丢整分钟）
        var now = DateTime.UtcNow;
        long delta = (long)(now - _onlineStamp).TotalSeconds;
        if (delta > 0) _host.State.TotalOnlineSeconds += delta;
        _onlineStamp = now;

        if (!wasHungry && _host.State.IsHungry) _host.SayAmbient(PetDialogue.Pick(PetDialogue.HungryLines));
        else if (!wasPlay && _host.State.WantsPlay) _host.SayAmbient(PetDialogue.Pick(PetDialogue.WantsPlayLines));
        else if (!wasDirty && _host.State.IsDirty) _host.SayAmbient(PetDialogue.Pick(PetDialogue.DirtyLines));
        _host.State.LastSeen = DateTime.UtcNow;   // 周期刷新，保证下次启动计算离线时长准确
        _host.SavePetState();

        ChimeIfDue(DateTime.Now);
        UpdateBreakReminder();
    }

    /// <summary>整点/半点报时（tick 约每分钟一次，命中 minute==0/30 时弹气泡）。</summary>
    private void ChimeIfDue(DateTime now)
    {
        if (!_host.Settings.ChimeEnabled) return;
        if (now.Minute != 0 && now.Minute != 30) return;
        if ((now - _lastChime).TotalMinutes < 50) return;
        _lastChime = now;
        if (now.Minute == 0) _host.SayAmbient(PetDialogue.Chime(now.Hour));
        else _host.SayAmbient(PetDialogue.ChimeHalf(now.Hour));
    }

    private void UpdateBreakReminder()
    {
        if (!_host.Settings.BreakReminder) { _breakTickCount = 0; return; }
        if (++_breakTickCount < BreakEveryTicks) return;
        _breakTickCount = 0;
        _host.SayAmbient(PetDialogue.Pick(PetDialogue.BreakReminders));
    }

    /// <summary>待机随机动作：低频触发（每次重新随机间隔，避免规律感）。
    /// 仅在空闲（近期无互动、未在拖拽、非半隐藏离屏）时偶发播放一个待机动作，并小概率配一句萌系碎碎念。
    /// 普通优先级，绝不打断用户的互动反应。</summary>
    private void OnIdleTick()
    {
        if (_host.IsDisposed || _host.Live2D == null || _idleTimer == null) return;
        _idleTimer.Interval = 20_000 + Rnd.Next(25_000);   // 下次 20~45s

        // 打盹中：偶尔冒一句睡意，不做随机动作，避免吵到用户
        if (_sleeping)
        {
            if (Rnd.Next(100) < 25) _host.SayAmbient(PetDialogue.Pick(PetDialogue.SleepLines));
            return;
        }

        bool idle = (DateTime.UtcNow - _host.LastInteraction) > TimeSpan.FromSeconds(6)
                    && _host.IsPetInteractive;
        if (!idle || _host.IdleMotionGroups.Count == 0) return;

        if (Rnd.Next(100) < 60)   // 60% 概率真的做待机动作，其余时间安静歇着
        {
            _host.Live2D.PlayIdleMotion(_host.IdleMotionGroups);
            if (Rnd.Next(100) < 35)   // 偶尔碎碎念，按状态"蔫/活泼"更有人味
                _host.SayAmbient(PickIdleLine());
        }
    }

    /// <summary>按宠物当前状态选待机碎碎念：状态差→蔫，状态好→活泼，中等→普通。</summary>
    private string PickIdleLine()
    {
        if (_host.State.IsHungry || _host.State.WantsPlay || _host.State.IsDirty)
            return PetDialogue.Pick(PetDialogue.LowStateLines);
        if (_host.State.Satiety >= 70 && _host.State.Mood >= 70 && _host.State.Cleanliness >= 70)
            return PetDialogue.Pick(PetDialogue.HappyIdleLines);
        return PetDialogue.Pick(PetDialogue.IdleLines);
    }

    // ---- 离开检测（打盹待机）----

    private void OnSleepCheck()
    {
        if (_host.IsDisposed) return;
        if (_host.Settings.IdleSleepMinutes <= 0) { if (_sleeping) WakeUp(); return; }
        bool idle = GetIdleMinutes() >= _host.Settings.IdleSleepMinutes;
        if (idle && !_sleeping) EnterSleep();
        else if (!idle && _sleeping) WakeUp();
    }

    /// <summary>取系统空闲分钟数（距上次键鼠输入）。</summary>
    private int GetIdleMinutes()
    {
        try
        {
            var li = new NativeMethods.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.LASTINPUTINFO>() };
            if (NativeMethods.GetLastInputInfo(ref li))
            {
                uint now = unchecked((uint)Environment.TickCount);
                uint idle = now - li.dwTime;   // 两者同属 GetTickCount 体系，减法天然处理回绕
                return (int)(idle / 60000);
            }
        }
        catch { }
        return 0;
    }

    private void EnterSleep()
    {
        _sleeping = true;
        _host.LastInteraction = DateTime.UtcNow;   // 睡觉期间不触发"近期互动"满帧
        if (_host.IdleMotionGroups.Count > 0) _host.Live2D?.PlayIdleMotion(_host.IdleMotionGroups);
        _host.SayAmbient(PetDialogue.Pick(PetDialogue.SleepLines));
    }

    private void WakeUp()
    {
        _sleeping = false;
        _host.LastInteraction = DateTime.UtcNow;
        _host.SetTransientMood(PetMood.Happy, 1.5);
        _host.Live2D?.PlayReaction("Tap");
        _host.Say(PetDialogue.Pick(PetDialogue.WakeLines));   // 醒来提示不受免打扰抑制（用户回来后的主动反馈）
    }

    public void Dispose()
    {
        _decayTimer?.Stop();
        _decayTimer?.Dispose();
        _idleTimer?.Stop();
        _idleTimer?.Dispose();
        _sleepTimer?.Stop();
        _sleepTimer?.Dispose();
    }
}
