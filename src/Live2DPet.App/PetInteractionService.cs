using System;
using Live2DPet.Core.Pet;

namespace Live2DPet.App;

/// <summary>
/// 互动服务：把"一次用户交互"翻译成宠物的完整反应链——播动作 → 计好感/经验 →
/// 弹气泡（普通回应 / 亲密度提升 / 升级 / 羁绊提升 / 里程碑解锁，按优先级补句）→
/// 记账成就 → 落盘。输入采集（窗口点击区域、键盘钩子上下文、UI 线程 marshal）
/// 由宿主负责，本服务只做"给定一次互动，如何反应"。
/// 注意：本类不持有任何需要随状态替换而同步更新的缓存——一切状态都经
/// <see cref="IPetHost.State"/> 活引用读取；方法须在 UI 线程调用。
/// </summary>
internal sealed class PetInteractionService
{
    private readonly IPetHost _host;

    public PetInteractionService(IPetHost host) => _host = host;

    /// <summary>
    /// 一次互动：播动作 + 计好感/经验 + 弹气泡（升级/亲密度提升/里程碑解锁优先显示）。
    /// </summary>
    public void Interact(string group, int affection, int exp, string[] replies)
    {
        _host.LastInteraction = DateTime.UtcNow;
        _host.SetTransientMood(PetMood.Happy, 1.5);   // 被摸/被戳 → 开心一会儿
        _host.Live2D?.PlayReaction(group);
        _host.Sound?.Play(group.Equals("Flick", StringComparison.OrdinalIgnoreCase) ? "pop" : "tap");
        int levelBefore = _host.State.Level;
        bool affectionUp = _host.State.AddAffection(affection);
        bool leveled = _host.State.AddExperience(exp);
        _host.Say(PetDialogue.PickReaction(replies, _host.State.Level));
        if (affectionUp) _host.Say(PetDialogue.AffectionUp(_host.State.AffectionName));
        if (leveled)
        {
            if (_host.State.Level > levelBefore)
            {
                _host.Say(PetDialogue.LevelUp(_host.State.Level, _host.State.StageName));
                _host.Sound?.Play("levelup");
            }
            else   // 满级后：羁绊等级提升
            {
                _host.Say(PetDialogue.BondUp(_host.State.BondLevel, _host.State.BondName));
                _host.Sound?.Play("levelup");
            }
        }
        SayLevelupUnlocks(levelBefore);
        AfterInteraction();
        _host.SavePetState();
    }

    /// <summary>
    /// 键盘互动（按键命中反应组）：播动作 + 短暂开心 + 微量好感/经验。
    /// 宿主已在调用前做过全屏静默/开关判断，本方法不弹气泡（避免刷屏）。
    /// </summary>
    public void KeyboardReaction(string group)
    {
        _host.LastInteraction = DateTime.UtcNow;
        _host.SetTransientMood(PetMood.Happy, 1.0);
        _host.Live2D?.PlayReaction(group);
        _host.State.AddAffection(1);
        _host.State.AddExperience(1);
        AfterInteraction();
        _host.SavePetState();
    }

    /// <summary>拖拽开始（被拎起来瞬间）：受惊吓动作 + 惊吓台词 + 短暂"受惊"情绪。</summary>
    public void DragStart()
    {
        _host.LastInteraction = DateTime.UtcNow;
        _host.SetTransientMood(PetMood.Surprised, 1.3);
        _host.Live2D?.PlayReaction("Flick");   // 受惊吓动作
        _host.Sound?.Play("startle");
        _host.Say(PetDialogue.Pick(PetDialogue.StartleLines));
    }

    /// <summary>喂食：成功 → 开心动作/音效/回应；已饱 → 委婉拒绝。</summary>
    public void Feed()
    {
        _host.LastInteraction = DateTime.UtcNow;
        int affBefore = _host.State.AffectionLevel;
        int levelBefore = _host.State.Level;
        var r = _host.State.Feed();
        if (r == CareResult.Success)
        {
            _host.SetTransientMood(PetMood.Happy, 2.0);
            _host.Live2D?.PlayReaction("Tap");
            _host.Sound?.Play("eat");
            _host.Say(PetDialogue.PickReaction(PetDialogue.FeedReplies, _host.State.Level));
            if (_host.State.AffectionLevel > affBefore)
                _host.Say(PetDialogue.AffectionUp(_host.State.AffectionName));
            if (_host.State.Level > levelBefore)
            { _host.Say(PetDialogue.LevelUp(_host.State.Level, _host.State.StageName)); _host.Sound?.Play("levelup"); }
            SayLevelupUnlocks(levelBefore);
            _host.State.TotalFeeds++;
            AfterInteraction();
            _host.SavePetState();
        }
        else // CareResult.Full
        {
            _host.Live2D?.PlayReaction("Tap");
            _host.Say(PetDialogue.Pick(PetDialogue.FullLines));
        }
    }

    /// <summary>陪玩：成功 → 开心反应；太饿/玩够了 → 对应安抚台词。</summary>
    public void Play()
    {
        _host.LastInteraction = DateTime.UtcNow;
        int affBefore = _host.State.AffectionLevel;
        int levelBefore = _host.State.Level;
        var r = _host.State.Play();
        if (r == CareResult.Success)
        {
            _host.SetTransientMood(PetMood.Happy, 2.0);
            _host.Live2D?.PlayReaction("Flick");
            _host.Sound?.Play("play");
            _host.Say(PetDialogue.PickReaction(PetDialogue.PlayReplies, _host.State.Level));
            if (_host.State.AffectionLevel > affBefore)
                _host.Say(PetDialogue.AffectionUp(_host.State.AffectionName));
            if (_host.State.Level > levelBefore)
            { _host.Say(PetDialogue.LevelUp(_host.State.Level, _host.State.StageName)); _host.Sound?.Play("levelup"); }
            SayLevelupUnlocks(levelBefore);
            _host.State.TotalPlays++;
            AfterInteraction();
            _host.SavePetState();
        }
        else if (r == CareResult.Hungry)
        {
            _host.Live2D?.PlayReaction("Tap");
            _host.Say(PetDialogue.Pick(PetDialogue.TooHungryToPlayLines));
        }
        else // CareResult.Tired
        {
            _host.Live2D?.PlayReaction("Tap");
            _host.Say(PetDialogue.Pick(PetDialogue.PlayEnoughLines));
        }
    }

    /// <summary>洗澡：成功 → 干净反应；已经很干净 → 委婉说明。</summary>
    public void Bathe()
    {
        _host.LastInteraction = DateTime.UtcNow;
        int affBefore = _host.State.AffectionLevel;
        int levelBefore = _host.State.Level;
        var r = _host.State.Bathe();
        if (r == CareResult.Success)
        {
            _host.SetTransientMood(PetMood.Happy, 2.0);
            _host.Live2D?.PlayReaction("Tap@Body");
            _host.Sound?.Play("tap");
            _host.Say(PetDialogue.PickReaction(PetDialogue.BatheReplies, _host.State.Level));
            if (_host.State.AffectionLevel > affBefore)
                _host.Say(PetDialogue.AffectionUp(_host.State.AffectionName));
            if (_host.State.Level > levelBefore)
            { _host.Say(PetDialogue.LevelUp(_host.State.Level, _host.State.StageName)); _host.Sound?.Play("levelup"); }
            SayLevelupUnlocks(levelBefore);
            _host.State.TotalBaths++;
            AfterInteraction();
            _host.SavePetState();
        }
        else // CareResult.Clean
        {
            _host.Live2D?.PlayReaction("Tap");
            _host.Say(PetDialogue.Pick(PetDialogue.CleanEnoughLines));
        }
    }

    /// <summary>若签到/离线补偿/成就奖励后发生升级或羁绊提升，补弹对应提示（含音效）。</summary>
    public void AnnounceLevelUp(int levelBefore, int bondBefore)
    {
        if (_host.State.Level > levelBefore)
        {
            _host.Say(PetDialogue.LevelUp(_host.State.Level, _host.State.StageName));
            _host.Sound?.Play("levelup");
            SayLevelupUnlocks(levelBefore);
        }
        else if (_host.State.BondLevel > bondBefore)
        {
            _host.Say(PetDialogue.BondUp(_host.State.BondLevel, _host.State.BondName));
            _host.Sound?.Play("levelup");
        }
    }

    /// <summary>
    /// 检测并播报新解锁的成就（弹气泡 + 音效 + 发放奖励 + 保存）。
    /// 不计入用户互动次数（供启动期签到/离线补偿复用）。
    /// </summary>
    public void CheckAndAnnounceAchievements()
    {
        var newly = _host.State.CheckAchievements();
        int lvBefore = _host.State.Level;
        int bondBefore = _host.State.BondLevel;
        int totalAff = 0, totalExp = 0;
        foreach (var a in newly)
        {
            if (a.RewardAffection > 0) totalAff += a.RewardAffection;
            if (a.RewardExp > 0) totalExp += a.RewardExp;
            _host.Say($"成就解锁「{a.Name}」：{a.Desc}{a.RewardText}");
            _host.Sound?.Play("levelup");
        }
        if (newly.Count > 0)
        {
            if (totalAff > 0) _host.State.AddAffection(totalAff);
            if (totalExp > 0) _host.State.AddExperience(totalExp);
            AnnounceLevelUp(lvBefore, bondBefore);  // 奖励可能触发升级或羁绊提升
            SayLevelupUnlocks(lvBefore);
            _host.SavePetState();
        }
    }

    /// <summary>升级后若跨过里程碑等级（3/5/7/10），补一条"解锁"提示。</summary>
    private void SayLevelupUnlocks(int levelBefore)
    {
        for (int lv = levelBefore + 1; lv <= _host.State.Level; lv++)
        {
            if (PetState.IsMilestoneLevel(lv))
                _host.Say(PetDialogue.MilestoneUnlock(lv));
        }
    }

    /// <summary>互动后统一记账：累计互动次数 + 检测解锁成就（新解锁弹气泡 + 音效 + 保存）。</summary>
    private void AfterInteraction()
    {
        _host.State.TotalInteractions++;
        CheckAndAnnounceAchievements();
    }
}
