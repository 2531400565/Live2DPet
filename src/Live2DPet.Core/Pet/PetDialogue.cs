using System;
using System.Globalization;
using Live2DPet.Core.Settings;

namespace Live2DPet.Core.Pet;

/// <summary>
/// 桌宠文案库：问候、互动回应、照顾反馈、状态提醒、升级提示。
/// 纯静态文本 + 随机选取，无依赖。
/// </summary>
public static class PetDialogue
{
    private static readonly Random Rng = new();

    /// <summary>台词里的昵称占位符，展示时替换为宠物昵称（不区分大小写）。</summary>
    public const string NameToken = "{name}";

    /// <summary>默认昵称：用户未起名或把名字清空时使用，避免出现"早安，！"这类断句。</summary>
    public const string DefaultPetName = "小宠";

    /// <summary>
    /// 把台词中的昵称占位符 <c>{name}</c> 替换成宠物昵称（纯函数，便于单测）。
    /// 语义：昵称是<b>宠物自己的名字</b>，台词中用第三人称自称，如"{name}等你好久啦~"。
    /// </summary>
    /// <param name="text">原始台词（可空）。</param>
    /// <param name="petName">宠物昵称，空白时回退 <see cref="DefaultPetName"/>。</param>
    /// <returns>替换后的台词；原文为空则返回空串。</returns>
    public static string Named(string? text, string? petName)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (text.IndexOf(NameToken, StringComparison.OrdinalIgnoreCase) < 0) return text;
        string name = string.IsNullOrWhiteSpace(petName) ? DefaultPetName : petName.Trim();
        return text.Replace(NameToken, name, StringComparison.OrdinalIgnoreCase);
    }

    // ---- 台词自定义（v1.3）：内置台词快照 + 覆盖应用 ----
    // 注意：下面 12 组台词字段已去掉 readonly，允许用户覆盖；本字典在静态构造函数
    // （所有字段初始化完成后才执行）里拍下"出厂台词"并克隆保存，供回退/重新加载使用。
    // 克隆是为了防止外部原地修改数组元素污染内置值。
    private static readonly Dictionary<string, string[]> Builtin = new(StringComparer.OrdinalIgnoreCase);

    static PetDialogue()
    {
        Builtin["Greeting"]  = (string[])Greetings.Clone();
        Builtin["Pet"]       = (string[])TapReplies.Clone();
        Builtin["Feed"]      = (string[])FeedReplies.Clone();
        Builtin["Play"]      = (string[])PlayReplies.Clone();
        Builtin["Bathe"]     = (string[])BatheReplies.Clone();
        Builtin["Idle"]      = (string[])IdleLines.Clone();
        Builtin["Wake"]      = (string[])WakeLines.Clone();
        Builtin["Sleep"]     = (string[])SleepLines.Clone();
        Builtin["Startle"]   = (string[])StartleLines.Clone();
        Builtin["Break"]     = (string[])BreakReminders.Clone();
        Builtin["Hungry"]    = (string[])HungryLines.Clone();
        Builtin["HappyIdle"] = (string[])HappyIdleLines.Clone();
    }

    /// <summary>取某一组的内置台词副本（未知分组名返回空数组）。用于生成 dialogue.json 模板与回退。</summary>
    public static string[] BuiltinFor(string? group)
        => group != null && Builtin.TryGetValue(group, out var lines) ? (string[])lines.Clone() : Array.Empty<string>();

    /// <summary>
    /// 应用用户自定义台词：<b>逐组合并</b>——某组有效（至少一句非空）则用自定义，
    /// 缺失 / 空数组 / 全是空白则回退该组内置台词。传入 null 等价于全部回退内置。
    /// </summary>
    public static void ApplyOverrides(DialogueOverrides? overrides)
    {
        Greetings      = Resolve(overrides?.Greeting,  "Greeting");
        TapReplies     = Resolve(overrides?.Pet,       "Pet");
        FeedReplies    = Resolve(overrides?.Feed,      "Feed");
        PlayReplies    = Resolve(overrides?.Play,      "Play");
        BatheReplies   = Resolve(overrides?.Bathe,     "Bathe");
        IdleLines      = Resolve(overrides?.Idle,      "Idle");
        WakeLines      = Resolve(overrides?.Wake,      "Wake");
        SleepLines     = Resolve(overrides?.Sleep,     "Sleep");
        StartleLines   = Resolve(overrides?.Startle,   "Startle");
        BreakReminders = Resolve(overrides?.Break,     "Break");
        HungryLines    = Resolve(overrides?.Hungry,    "Hungry");
        HappyIdleLines = Resolve(overrides?.HappyIdle, "HappyIdle");
    }

    /// <summary>全部回退到内置台词（用户清空自定义 / 文件损坏时调用）。</summary>
    public static void ResetToBuiltin() => ApplyOverrides(null);

    // 自定义有效则用自定义，否则用内置副本（每次都返回新数组，避免共享可变状态）
    private static string[] Resolve(string[]? custom, string group)
        => DialogueOverrides.Sanitize(custom) ?? (string[])Builtin[group].Clone();

    public static string[] Greetings =
    {
        "今天也要元气满满呀~",
        "你来啦，{name}等你好久了！",
        "嘿嘿，又见面啦~",
        "今天想和我玩吗？"
    };

    public static string[] TapReplies =
    {
        "嘿嘿，别戳啦~",
        "好痒呀！",
        "嗯？怎么啦？",
        "摸摸头~",
        "喜欢你~"
    };

    // 分区抚摸反馈：摸头 / 戳肚子 / 挠脚，各有专属台词
    public static readonly string[] HeadRubLines =
    {
        "摸摸头~好舒服呀",
        "头顶被你摸，眯起眼睛啦",
        "最喜欢被你摸摸头了~",
        "再摸一下嘛，不肯走咯~"
    };

    public static readonly string[] PokeBodyLines =
    {
        "诶？戳我肚子！",
        "别戳肚子啦，痒痒的~",
        "再戳我可要躲开咯~",
        "肚子可是禁地哦！"
    };

    public static readonly string[] TouchFeetLines =
    {
        "脚脚！好痒好痒~",
        "别挠脚啦，要跳起来啦~",
        "脚丫子最怕痒了啦~",
        "呜，脚脚投降！"
    };

    public static readonly string[] DoubleTapReplies =
    {
        "呀！双击啦！",
        "这么喜欢我呀~",
        "好开心！"
    };

    public static readonly string[] DragReplies =
    {
        "要带我去哪呀？",
        "飞起来啦~",
        "慢一点慢一点~"
    };

    // ---- 拖拽受惊吓：被拎起来瞬间的惊吓台词（与拖拽途中的 DragReplies 区分）----
    public static string[] StartleLines =
    {
        "呀！突然被拎起来啦~",
        "诶？要带我去哪~",
        "呜哇，吓我一跳！",
        "慢点慢点，我头晕~"
    };

    public static string[] FeedReplies =
    {
        "好吃！最喜欢你了~",
        "啊呜啊呜，谢谢！",
        "吃饱啦，满足~"
    };

    public static string[] PlayReplies =
    {
        "来玩吧来玩吧！",
        "好耶！再玩一会儿~",
        "嘿嘿，好玩！"
    };

    public static string[] BatheReplies =
    {
        "洗香香啦~",
        "清爽多了！",
        "香喷喷的，喜欢吗？"
    };

    // ---- 照顾被拒绝时的萌系提示（状态已满/过低，不能再无限操作）----
    public static readonly string[] FullLines =
    {
        "已经很饱了，不能再吃啦~",
        "肚子圆滚滚的，吃不下了啦~",
        "呜…再吃就要变成小圆球啦~"
    };

    public static readonly string[] PlayEnoughLines =
    {
        "玩够啦，想歇一会儿~",
        "有点累啦，让我躺会儿吧~",
        "嘿嘿，今天玩得好开心，先休息一下~"
    };

    public static readonly string[] TooHungryToPlayLines =
    {
        "肚子好饿，没力气玩啦~",
        "先喂喂我嘛，玩不动了~",
        "咕噜咕噜…饿了，想吃好吃的~"
    };

    public static readonly string[] CleanEnoughLines =
    {
        "已经香喷喷啦，不用洗澡~",
        "身上很干净哦，不用洗啦~",
        "刚刚洗过，还是香香的~"
    };

    public static string[] HungryLines =
    {
        "肚子好饿呀，能喂我点吃的吗？",
        "咕噜咕噜…我饿啦~"
    };

    public static readonly string[] WantsPlayLines =
    {
        "好无聊呀，陪我玩一会儿吧~",
        "有点闷，来玩嘛~"
    };

    public static readonly string[] DirtyLines =
    {
        "身上脏脏的，帮我洗个澡吧~",
        "想洗个澡变干净~"
    };

    public static string Pick(string[] lines) => lines[Rng.Next(lines.Length)];

    // 等级 >=5 解锁的"高级亲昵台词"：升级后宠物更亲近，互动时有概率说这些（升级解锁回报的一部分）
    private static readonly string[] AdvancedLines =
    {
        "嘿嘿，只有你这么宠我~",
        "和你在一起最安心啦~",
        "我是不是越来越懂你了呀~",
        "最喜欢待在你身边了~",
        "谢谢你一直陪着我呀~"
    };

    /// <summary>互动回复：等级 >=5 时有一定概率解锁"高级亲昵台词"，体现升级后宠物更亲近。</summary>
    public static string PickReaction(string[] replies, int level)
    {
        if (level >= 5 && Rng.Next(100) < 40)
            return Pick(AdvancedLines);
        return Pick(replies);
    }

    /// <summary>等级里程碑解锁提示（Lv.3/5/7/10 到达时触发）。</summary>
    public static string MilestoneUnlock(int level) => level switch
    {
        3 => "解锁啦！我长大了，话也变多咯~",
        5 => "我已经这么厉害啦！解锁了更亲昵的悄悄话~",
        7 => "越来越懂你啦，以后会更贴心哦~",
        10 => "完全体达成！我会一直一直陪着你~",
        _ => "解锁新阶段啦~"
    };

    /// <summary>每日签到气泡：根据连续天数/里程碑给不同热情度的台词（{name}=宠物昵称，Say 时统一替换）。</summary>
    public static string DailyLogin(LoginReport r)
    {
        if (r.IsFirstDay) return "今天是你和{name}认识的第一天，请多关照呀~";
        if (r.Milestone) return $"连续第 {r.Streak} 天！一周达成，{{name}}超开心，奖励加倍~";
        return $"连续陪伴你第 {r.Streak} 天啦，{{name}}今天也要一起加油哦~";
    }

    /// <summary>亲密度提升提示。</summary>
    public static string AffectionUp(string name) =>
        $"我们的关系升级啦：{name}！";

    /// <summary>等级提升提示。</summary>
    public static string LevelUp(int level, string stage) =>
        $"我升级啦！Lv.{level}（{stage}）";

    /// <summary>羁绊提升提示（满级后长期陪伴的成长）：bondName 为当前羁绊称号。</summary>
    public static string BondUp(int bondLevel, string bondName) =>
        $"羁绊加深啦：{bondName}！和你在一起的每一天，都是珍贵的回忆~";

    /// <summary>羁绊圆满（达到最高羁绊等级）时的专属表白。</summary>
    public static readonly string[] BondEternalLines =
    {
        "这一路有你陪着，就是我最大的幸福~",
        "从陌生到羁绊，谢谢你从未离开~",
        "无论多久，我都会在你身边~",
        "我们的故事，还有很长很长呢~"
    };

    // ---- 时间问候 + 报时 + 休息提醒 ----
    private static readonly string[] MorningGreetings =
    {
        "早安！{name}陪你开启元气满满的一天~",
        "早上好呀，记得吃早餐哦，{name}会一直陪着你的~"
    };
    private static readonly string[] NoonGreetings =
    {
        "中午好，该休息吃饭啦~",
        "午安~ 忙了一上午辛苦啦"
    };
    private static readonly string[] AfternoonGreetings =
    {
        "下午好，打起精神~",
        "下午茶时间到啦~"
    };
    private static readonly string[] EveningGreetings =
    {
        "晚上好，辛苦一天啦~",
        "晚上好呀，好好放松一下吧"
    };
    private static readonly string[] NightGreetings =
    {
        "夜深啦，别太晚睡哦~",
        "这么晚还在忙，注意休息呀"
    };

    /// <summary>按当前时间选择问候语。</summary>
    public static string GreetingFor(DateTime now) => now.Hour switch
    {
        >= 5 and < 9 => Pick(MorningGreetings),
        >= 9 and < 12 => Pick(NoonGreetings),
        >= 12 and < 18 => Pick(AfternoonGreetings),
        >= 18 and < 23 => Pick(EveningGreetings),
        _ => Pick(NightGreetings)
    };

    /// <summary>整点报时。</summary>
    public static string Chime(int hour) =>
        $"现在是 {hour} 点整啦~";

    /// <summary>半点报时。</summary>
    public static string ChimeHalf(int hour) =>
        $"{hour} 点半啦，起来活动一下吧~";

    public static string[] BreakReminders =
    {
        "坐很久啦，起来伸个懒腰、喝口水吧~",
        "该休息一下了，看看远处放松眼睛~",
        "别太累啦，休息五分钟再继续~"
    };

    // ---- 待机随机动作时的萌系碎碎念（不绑定具体动作，用来增加"活气"）----
    public static string[] IdleLines =
    {
        "打了个哈欠~",
        "发会儿呆…",
        "伸了个懒腰~",
        "今天天气真好呀~",
        "{name}在想你什么时候来找我玩呢~",
        "哼着小曲儿~",
        "偷偷打了个盹~"
    };

    // 心情驱动的待机台词：状态差（饿/脏/想玩）时蔫蔫的，状态好时更活泼
    public static readonly string[] LowStateLines =
    {
        "有点提不起精神…",
        "肚子空空的，蔫蔫的~",
        "好想被照顾一下呀…",
        "没什么力气呢，趴一会儿…"
    };

    public static string[] HappyIdleLines =
    {
        "今天心情超好，嘿嘿~",
        "精神满满，想蹦跶一下~",
        "元气十足！",
        "心情好得想转圈圈~"
    };

    // ---- 专注陪伴（番茄钟 / Focus Companion）台词：专注期间宠物主动保持安静，只做必要提示 ----
    public static readonly string[] FocusStartLines =
    {
        "那我开始陪你专心做事啦，悄悄安静下来~",
        "专注时间到！{name}会乖乖的不吵你~",
        "开工咯，我就在旁边陪着你~"
    };

    public static readonly string[] FocusReminderLines =
    {
        "已经过去 5 分钟啦，喝口水、伸个懒腰~",
        "进度不错哦，继续保持专注~",
        "我一直在呢，累了就抬头看看我~",
        "又坚持了一会儿，你超棒的！"
    };

    public static readonly string[] FocusDoneLines =
    {
        "专注完成！你真厉害，休息一下吧~",
        "一个番茄钟结束啦，拍拍手奖励自己~",
        "专注任务达成，{name}为你骄傲！"
    };

    public static readonly string[] BreakStartLines =
    {
        "短休开始，起来走两步放松一下~",
        "休息 5 分钟，别盯屏幕太久哦~",
        "放松一下，{name}陪你发会儿呆~"
    };

    public static readonly string[] BreakDoneLines =
    {
        "休息结束啦，继续加油哦~",
        "满血复活！下一轮专注开始~",
        "歇够啦，我们接着来~"
    };

    /// <summary>每日目标达成庆祝：当今日完成数首次达到设置的目标时弹（{name}=宠物昵称）。</summary>
    public static readonly string[] DailyGoalLines =
    {
        "今日专注目标达成！{name}给你比心~",
        "番茄目标全收！今天的你闪闪发光~",
        "目标达成！快奖励自己一下，{name}也超开心~"
    };

    /// <summary>离线"欢迎回来"：按离开时长给不同台词与热情度（{name}=宠物昵称，Say 时统一替换）。</summary>
    public static string WelcomeBack(TimeSpan gap)
    {
        double h = gap.TotalHours;
        if (h < 1) return "回来啦！才离开一小会儿，{name}就开始想你啦~";
        if (h < 3) return $"欢迎回来~ 你离开了约 {(int)h} 小时，{{name}}可是乖乖等你的哦！";
        if (h < 8) return $"好久不见！你去忙了 {(int)h} 小时，{{name}}超想你的~";
        return "呜…你终于回来啦！{name}等了好久好久，要抱抱~";
    }

    /// <summary>按日期返回节日/生日问候；非特殊日期返回 null（交给常规问候）。
    /// birthday 为 "MM-dd" 格式，空/无效则跳过生日判断。</summary>
    public static string? FestivalGreeting(DateTime now, string? birthday)
    {
        if (!string.IsNullOrWhiteSpace(birthday) && now.ToString("MM-dd") == birthday)
            return "生日快乐呀！今天要开开心心的哦~";

        // 农历节日优先（春节/元宵/端午/中秋…）
        string? lunar = LunarFestival(now);
        if (lunar != null) return lunar;

        return (now.Month, now.Day) switch
        {
            (1, 1) => "元旦快乐！新的一年也要一起加油哦~",
            (2, 14) => "情人节快乐呀，今天也要甜甜的~",
            (3, 8) => "女生节快乐，今天也要美美的~",
            (5, 1) => "劳动节快乐，辛苦啦，休息一下吧~",
            (6, 1) => "儿童节快乐！谁还不是个宝宝呢~",
            (9, 10) => "教师节快乐，感谢一路上的陪伴~",
            (10, 1) => "国庆快乐！假期好好放松一下吧~",
            (11, 11) => "双十一快乐，剁手要适度哦~",
            (12, 24) => "平安夜快乐，记得吃个苹果哦~",
            (12, 25) => "圣诞快乐！Merry Christmas~",
            (12, 31) => "跨年夜快乐，明年也要在一起哦~",
            _ => null
        };
    }

    /// <summary>用内置农历历法换算当前日期的农历月/日，返回对应节日问候；非节日返回 null。</summary>
    private static string? LunarFestival(DateTime now)
    {
        try
        {
            var cal = new ChineseLunisolarCalendar();
            int month = cal.GetMonth(now);
            int day = cal.GetDayOfMonth(now);
            if (month > 12) month -= 12;   // 闰月不视为正节
            return (month, day) switch
            {
                (1, 1) => "春节快乐！新的一年红红火火~",
                (1, 15) => "元宵节快乐，团团圆圆甜甜哒~",
                (2, 2) => "龙抬头啦，今天理个发讨个好彩头~",
                (5, 5) => "端午安康！记得吃粽子哦~",
                (7, 7) => "七夕快乐，有情人终成眷属~",
                (7, 15) => "中元节，注意平安哟~",
                (8, 15) => "中秋快乐！月圆人团圆~",
                (9, 9) => "重阳节快乐，登高望远身体好~",
                (12, 8) => "腊八节，喝碗腊八粥暖暖身~",
                _ => null
            };
        }
        catch
        {
            return null;   // 历法异常（极罕见）→ 当作无农历节日
        }
    }

    // ---- 离开检测：用户闲置时打盹，回来时唤醒 ----
    public static string[] SleepLines =
    {
        "zzz… 我先眯一会儿~",
        "（打盹中）呼…呼…",
        "你不在，我偷偷睡个觉~",
        "好困… 先躺一会儿啦~"
    };

    public static string[] WakeLines =
    {
        "醒啦！你回来啦，{name}想死你啦~",
        "诶？你回来啦，{name}刚睡着呢~",
        "醒醒醒醒，你终于回来咯~",
        "好耶，你回来我就不困啦~"
    };
}
