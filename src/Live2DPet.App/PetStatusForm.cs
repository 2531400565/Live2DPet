using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Live2DPet.Core.Pet;

namespace Live2DPet.App;

/// <summary>
/// 养成面板（WinForms）：Tab 分两页——「养成」展示等级/经验/好感/饱食/心情/清洁 + 操作按钮；
/// 「成就」展示成就列表（已解锁 ★ / 未解锁 ○）。
/// 内部定时器每秒刷新，状态变化即时可见。
/// </summary>
public sealed class PetStatusForm : Form
{
    private readonly PetState _state;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    // ---- 养成页控件 ----
    private readonly Label _headerLabel = new() { Left = 16, Top = 6, AutoSize = true };
    private readonly Label _levelLabel = new() { Left = 16, Top = 52, AutoSize = true };
    private readonly Label _streakLabel = new() { Left = 16, Top = 30, AutoSize = true, ForeColor = Color.Gray };
    private readonly ProgressBar _expBar = new() { Left = 16, Top = 78, Width = 200, Height = 18 };
    private readonly Label _expLabel = new() { Left = 224, Top = 78, AutoSize = true };
    private readonly Label _affectionLabel = new() { Left = 16, Top = 100, AutoSize = true };
    private readonly ProgressBar _affectionBar = new() { Left = 16, Top = 122, Width = 300, Height = 18 };
    private readonly ProgressBar _satietyBar = new() { Left = 80, Top = 150, Width = 240, Height = 18 };
    private readonly ProgressBar _moodBar = new() { Left = 80, Top = 184, Width = 240, Height = 18 };
    private readonly ProgressBar _cleanBar = new() { Left = 80, Top = 218, Width = 240, Height = 18 };
    private readonly Button _feedButton = new() { Text = "喂食", Left = 16, Top = 258, Width = 76, Height = 32 };
    private readonly Button _playButton = new() { Text = "玩耍", Left = 100, Top = 258, Width = 76, Height = 32 };
    private readonly Button _batheButton = new() { Text = "洗澡", Left = 184, Top = 258, Width = 76, Height = 32 };
    private readonly Button _closeButton = new() { Text = "关闭", Left = 268, Top = 258, Width = 68, Height = 32 };

    // ---- 成就页控件 ----
    private readonly Label _achieveCountLabel = new() { Left = 12, Top = 10, AutoSize = true, Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold) };
    private readonly ListBox _achieveList = new() { Left = 12, Top = 34, Width = 312, Height = 246, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };

    // ---- 统计页控件 ----
    private readonly Label _statsLabel = new() { Left = 16, Top = 12, AutoSize = true };

    public PetStatusForm(PetState state, Action onFeed, Action onPlay, Action onBathe, string? petName = null)
    {
        _state = state;

        Text = "养成面板";
        ClientSize = new Size(360, 344);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9f);

        var tabs = new TabControl { Location = new Point(4, 4), Size = new Size(352, 336) };
        Controls.Add(tabs);

        var carePage = new TabPage("养成");
        var achievePage = new TabPage("成就");
        var statsPage = new TabPage("统计");
        tabs.TabPages.Add(carePage);
        tabs.TabPages.Add(achievePage);
        tabs.TabPages.Add(statsPage);

        // ---- 养成页 ----
        _headerLabel.Font = new Font(Font, FontStyle.Bold);
        SetPetName(petName);
        carePage.Controls.Add(_headerLabel);
        carePage.Controls.Add(_streakLabel);
        carePage.Controls.Add(_levelLabel);
        carePage.Controls.Add(_expBar);
        carePage.Controls.Add(_expLabel);
        carePage.Controls.Add(_affectionLabel);
        carePage.Controls.Add(_affectionBar);
        carePage.Controls.Add(new Label { Text = "饱食", Left = 16, Top = 150, AutoSize = true });
        carePage.Controls.Add(_satietyBar);
        carePage.Controls.Add(new Label { Text = "心情", Left = 16, Top = 184, AutoSize = true });
        carePage.Controls.Add(_moodBar);
        carePage.Controls.Add(new Label { Text = "清洁", Left = 16, Top = 218, AutoSize = true });
        carePage.Controls.Add(_cleanBar);
        carePage.Controls.Add(_feedButton);
        carePage.Controls.Add(_playButton);
        carePage.Controls.Add(_batheButton);
        carePage.Controls.Add(_closeButton);

        // ---- 成就页 ----
        achievePage.Controls.Add(_achieveCountLabel);
        achievePage.Controls.Add(_achieveList);

        // ---- 统计页 ----
        statsPage.Controls.Add(_statsLabel);

        _feedButton.Click += (_, _) => { onFeed(); RefreshUi(); };
        _playButton.Click += (_, _) => { onPlay(); RefreshUi(); };
        _batheButton.Click += (_, _) => { onBathe(); RefreshUi(); };
        _closeButton.Click += (_, _) => Close();

        RefreshUi();

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _refreshTimer.Tick += (_, _) => RefreshUi();
        _refreshTimer.Start();
    }

    /// <summary>更新昵称显示（标题栏 + 养成页抬头）。改名字时宿主直接调用，无需重建窗口。</summary>
    public void SetPetName(string? petName)
    {
        string name = string.IsNullOrWhiteSpace(petName) ? PetDialogue.DefaultPetName : petName.Trim();
        Text = $"养成面板 · {name}";
        _headerLabel.Text = $"{name} 的养成面板";
    }

    private void RefreshUi()
    {
        _streakLabel.Text = $"连续陪伴 {_state.LoginStreak} 天 · 累计 {_state.TotalLogins} 天 · 最长 {_state.BestStreak} 天";
        _levelLabel.Text = _state.Level >= PetState.MaxLevel && _state.BondLevel > 0
            ? $"Lv.{_state.Level} · {_state.StageName} · {_state.BondName}"
            : $"Lv.{_state.Level} · {_state.StageName}";

        // 经验条：未满级显示升级进度；满级后显示羁绊进度（长期目标）
        if (_state.Level >= PetState.MaxLevel)
        {
            if (_state.BondLevel >= PetState.MaxBondLevel)
            {
                _expLabel.Text = $"羁绊圆满 · {_state.BondName}";
                _expBar.Maximum = 1;
                _expBar.Value = 1;
            }
            else
            {
                _expLabel.Text = $"羁绊 Lv.{_state.BondLevel} {_state.BondExp}/{_state.BondExpToNext}";
                _expBar.Maximum = Math.Max(1, _state.BondExpToNext);
                _expBar.Value = Math.Min(_expBar.Maximum, _state.BondExp);
            }
        }
        else
        {
            _expLabel.Text = $"{_state.Experience}/{_state.ExpToNext}";
            _expBar.Maximum = Math.Max(1, _state.ExpToNext);
            _expBar.Value = Math.Min(_expBar.Maximum, _state.Experience);
        }

        _affectionLabel.Text = $"好感度：{_state.AffectionName}（{_state.Affection}/1000）";
        _affectionBar.Maximum = 1000;
        _affectionBar.Value = _state.Affection;

        SetBar(_satietyBar, _state.Satiety);
        SetBar(_moodBar, _state.Mood);
        SetBar(_cleanBar, _state.Cleanliness);

        RefreshAchievements();
        RefreshStats();
    }

    private void RefreshStats()
    {
        var ts = TimeSpan.FromSeconds(_state.TotalOnlineSeconds);
        string bond = _state.Level >= PetState.MaxLevel
            ? (_state.BondLevel > 0 ? $"羁绊 Lv.{_state.BondLevel} · {_state.BondName}" : "羁绊未缔结（满级后互动开启）")
            : $"羁绊未开启（满级 Lv.{PetState.MaxLevel} 后开启）";
        _statsLabel.Text =
            $"羁绊：{bond}\n" +
            $"累计互动：{_state.TotalInteractions} 次\n" +
            $"累计喂食：{_state.TotalFeeds} 次\n" +
            $"累计玩耍：{_state.TotalPlays} 次\n" +
            $"累计洗澡：{_state.TotalBaths} 次\n" +
            $"累计启动：{_state.TotalLogins} 天\n" +
            $"累计陪伴：{(int)ts.TotalHours} 小时 {ts.Minutes} 分钟";
    }

    private void RefreshAchievements()
    {
        int unlocked = 0;
        _achieveList.BeginUpdate();
        _achieveList.Items.Clear();
        foreach (var a in AchievementCatalog.All)
        {
            bool done = _state.UnlockedAchievements.Contains(a.Id);
            if (done) unlocked++;
            _achieveList.Items.Add(done ? $"★ {a.Name}　{a.Desc}" : $"○ {a.Name}　{a.Desc}");
        }
        _achieveList.EndUpdate();
        _achieveCountLabel.Text = $"成就（{unlocked}/{AchievementCatalog.All.Count}）";
        var next = AchievementCatalog.All.FirstOrDefault(a => !_state.UnlockedAchievements.Contains(a.Id));
        if (next != null)
            _achieveCountLabel.Text += $"\n下个目标：{next.Name}（{next.Desc}）";
    }

    private static void SetBar(ProgressBar bar, int value)
    {
        bar.Maximum = 100;
        bar.Value = Math.Clamp(value, 0, 100);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _refreshTimer.Stop();
        base.OnFormClosing(e);
    }
}
