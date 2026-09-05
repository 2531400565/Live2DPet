using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Live2DPet.Core.Models;
using Live2DPet.Core.Settings;
using Live2DPet.Platform.Native;

namespace Live2DPet.App;

/// <summary>
/// 设置窗口（WinForms）：用 TabControl 分「外观 / 互动 / 养成 / 提醒」四页，
/// 避免控件过多时窗口过高溢出小屏。改动即时通过回调交给 App 应用并持久化。
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly Action _apply;
    private readonly Action<ModelInfo> _onModelSelected;
    private readonly Action<string> _onExpressionSelected;
    private readonly Action _onReset;
    private readonly Action _onBackup;
    private readonly Action _onRestore;
    private bool _loading = true;

    // ---- 外观 ----
    private readonly ComboBox _modelCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Left = 110, Top = 4, Width = 230 };
    private readonly TrackBar _scaleTrack = new() { Minimum = 50, Maximum = 200, TickFrequency = 5, Left = 110, Top = 36, Width = 170 };
    private readonly Label _scaleLabel = new() { Left = 290, Top = 40, Width = 44, TextAlign = ContentAlignment.MiddleRight };
    private readonly TrackBar _opacityTrack = new() { Minimum = 10, Maximum = 100, TickFrequency = 5, Left = 110, Top = 100, Width = 170 };
    private readonly Label _opacityLabel = new() { Left = 290, Top = 104, Width = 44, TextAlign = ContentAlignment.MiddleRight };
    private readonly ComboBox _expressionCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Left = 110, Top = 162, Width = 230 };
    private readonly ComboBox _fpsCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Left = 110, Top = 226, Width = 120 };

    // ---- 互动 ----
    private readonly CheckBox _clickThroughCheck = new() { Left = 12, Top = 8, AutoSize = true, Text = "鼠标穿透（完全穿透，不接收鼠标）" };
    private readonly CheckBox _draggableCheck = new() { Left = 12, Top = 36, AutoSize = true, Text = "允许拖动宠物" };
    private readonly CheckBox _keyboardCheck = new() { Left = 12, Top = 64, AutoSize = true, Text = "键盘互动（按键触发反应）" };
    private readonly CheckBox _gazeCheck = new() { Left = 12, Top = 92, AutoSize = true, Text = "眼神跟随鼠标（眼珠/头朝光标）" };
    private readonly CheckBox _moodCheck = new() { Left = 12, Top = 120, AutoSize = true, Text = "状态联动微表情（情绪时身体/头部倾斜）" };
    private readonly CheckBox _fullscreenCheck = new() { Left = 12, Top = 148, AutoSize = true, Text = "全屏/游戏时自动暂停键盘回应" };
    private readonly ComboBox _hotkeyCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Left = 110, Top = 180, Width = 180 };
    private readonly CheckBox _soundCheck = new() { Left = 12, Top = 212, AutoSize = true, Text = "音效（互动/照顾/升级提示音）" };
    private readonly TrackBar _volumeTrack = new() { Minimum = 0, Maximum = 100, TickFrequency = 5, Left = 110, Top = 244, Width = 170 };
    private readonly Label _volumeLabel = new() { Left = 290, Top = 248, Width = 44, TextAlign = ContentAlignment.MiddleRight };

    // ---- 养成 ----
    private readonly TextBox _birthdayBox = new() { Left = 110, Top = 4, Width = 100, MaxLength = 5 };
    private readonly CheckBox _dndCheck = new() { Left = 12, Top = 38, AutoSize = true, Text = "免打扰（专注模式：抑制环境气泡）" };
    private readonly NumericUpDown _dndStartH = new() { Left = 110, Top = 70, Width = 48, Minimum = 0, Maximum = 23 };
    private readonly NumericUpDown _dndStartM = new() { Left = 164, Top = 70, Width = 48, Minimum = 0, Maximum = 45, Increment = 15 };
    private readonly NumericUpDown _dndEndH = new() { Left = 110, Top = 102, Width = 48, Minimum = 0, Maximum = 23 };
    private readonly NumericUpDown _dndEndM = new() { Left = 164, Top = 102, Width = 48, Minimum = 0, Maximum = 45, Increment = 15 };
    private readonly Button _resetButton = new() { Text = "重置养成数据…", Left = 12, Top = 140, Width = 150, Height = 32 };
    private readonly Button _backupButton = new() { Text = "备份配置与养成数据…", Left = 12, Top = 186, Width = 180, Height = 30 };
    private readonly Button _restoreButton = new() { Text = "从备份还原…", Left = 12, Top = 224, Width = 180, Height = 30 };

    // ---- 提醒 ----
    private readonly CheckBox _chimeCheck = new() { Left = 12, Top = 8, AutoSize = true, Text = "整点/半点报时" };
    private readonly CheckBox _breakCheck = new() { Left = 12, Top = 36, AutoSize = true, Text = "休息/喝水提醒" };
    private readonly CheckBox _snapCheck = new() { Left = 12, Top = 64, AutoSize = true, Text = "拖到边缘自动贴边" };
    private readonly CheckBox _autoHideCheck = new() { Left = 12, Top = 92, AutoSize = true, Text = "贴边后自动半隐藏" };
    private readonly CheckBox _updateCheck = new() { Left = 12, Top = 120, AutoSize = true, Text = "启动时自动检查更新" };

    private readonly Button _closeButton = new() { Text = "关闭", Left = 300, Top = 440, Width = 80, Height = 32 };

    // 隐藏/显示快捷键预设：(显示名, 修饰键, 虚拟键码)。修饰键=MOD_CONTROL/MOD_ALT/MOD_SHIFT 组合，key=0 表示禁用。
    private static readonly (string name, int mods, int key)[] HotkeyPresets =
    {
        ("Ctrl + `（默认）", (int)NativeMethods.MOD_CONTROL, NativeMethods.VK_OEM_3),
        ("Ctrl + 空格", (int)NativeMethods.MOD_CONTROL, NativeMethods.VK_SPACE),
        ("Alt + `", (int)NativeMethods.MOD_ALT, NativeMethods.VK_OEM_3),
        ("Ctrl + Shift + H", (int)(NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT), NativeMethods.VK_H),
        ("禁用", 0, 0),
    };

    public SettingsForm(AppSettings settings, IReadOnlyList<ModelInfo> models, string currentModelId,
                        IReadOnlyList<string> expressions, string currentExpression,
                        Action apply, Action<ModelInfo> onModelSelected,
                        Action<string> onExpressionSelected, Action onReset,
                        Action onBackup, Action onRestore)
    {
        _settings = settings;
        _apply = apply;
        _onModelSelected = onModelSelected;
        _onExpressionSelected = onExpressionSelected;
        _onReset = onReset;
        _onBackup = onBackup;
        _onRestore = onRestore;

        Text = "桌宠设置";
        ClientSize = new Size(396, 480);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9f);

        Controls.Add(new Label { Text = "桌宠设置", Left = 12, Top = 10, AutoSize = true, Font = new Font(Font, FontStyle.Bold) });

        var tabs = new TabControl { Left = 12, Top = 34, Width = 372, Height = 396 };
        var pageAppearance = new TabPage("外观");
        var pageInteraction = new TabPage("互动");
        var pageCare = new TabPage("养成");
        var pageRemind = new TabPage("提醒");
        tabs.TabPages.AddRange(new[] { pageAppearance, pageInteraction, pageCare, pageRemind });
        Controls.Add(tabs);

        // ===== 外观页 =====
        pageAppearance.Controls.Add(new Label { Text = "模型", Left = 8, Top = 8, AutoSize = true });
        pageAppearance.Controls.Add(_modelCombo);
        pageAppearance.Controls.Add(new Label { Text = "缩放", Left = 8, Top = 42, AutoSize = true });
        pageAppearance.Controls.Add(_scaleTrack);
        pageAppearance.Controls.Add(_scaleLabel);
        pageAppearance.Controls.Add(new Label { Text = "透明度", Left = 8, Top = 106, AutoSize = true });
        pageAppearance.Controls.Add(_opacityTrack);
        pageAppearance.Controls.Add(_opacityLabel);
        pageAppearance.Controls.Add(new Label { Text = "表情", Left = 8, Top = 168, AutoSize = true });
        pageAppearance.Controls.Add(_expressionCombo);
        pageAppearance.Controls.Add(new Label { Text = "帧率", Left = 8, Top = 232, AutoSize = true });
        pageAppearance.Controls.Add(_fpsCombo);

        // ===== 互动页 =====
        pageInteraction.Controls.Add(_clickThroughCheck);
        pageInteraction.Controls.Add(_draggableCheck);
        pageInteraction.Controls.Add(_keyboardCheck);
        pageInteraction.Controls.Add(_gazeCheck);
        pageInteraction.Controls.Add(_moodCheck);
        pageInteraction.Controls.Add(_fullscreenCheck);
        pageInteraction.Controls.Add(new Label { Text = "隐藏快捷键", Left = 8, Top = 184, AutoSize = true });
        pageInteraction.Controls.Add(_hotkeyCombo);
        pageInteraction.Controls.Add(_soundCheck);
        pageInteraction.Controls.Add(new Label { Text = "音量", Left = 8, Top = 248, AutoSize = true });
        pageInteraction.Controls.Add(_volumeTrack);
        pageInteraction.Controls.Add(_volumeLabel);

        // ===== 养成页 =====
        pageCare.Controls.Add(new Label { Text = "生日（MM-dd）", Left = 8, Top = 8, AutoSize = true });
        pageCare.Controls.Add(_birthdayBox);
        pageCare.Controls.Add(_dndCheck);
        pageCare.Controls.Add(new Label { Text = "开始", Left = 8, Top = 72, AutoSize = true });
        pageCare.Controls.Add(_dndStartH);
        pageCare.Controls.Add(_dndStartM);
        pageCare.Controls.Add(new Label { Text = "结束", Left = 8, Top = 104, AutoSize = true });
        pageCare.Controls.Add(_dndEndH);
        pageCare.Controls.Add(_dndEndM);
        pageCare.Controls.Add(new Label { Text = "（支持跨午夜，如 23:00 → 08:00）", Left = 220, Top = 72, AutoSize = true, ForeColor = Color.Gray });
        pageCare.Controls.Add(_resetButton);
        pageCare.Controls.Add(_backupButton);
        pageCare.Controls.Add(_restoreButton);
        pageCare.Controls.Add(new Label
        {
            Text = "备份包含设置、养成进度与参数映射，换机/重装后可一键还原。",
            Left = 12, Top = 262, Width = 340, Height = 32, ForeColor = Color.Gray
        });

        // ===== 提醒页 =====
        pageRemind.Controls.Add(_chimeCheck);
        pageRemind.Controls.Add(_breakCheck);
        pageRemind.Controls.Add(_snapCheck);
        pageRemind.Controls.Add(_autoHideCheck);
        pageRemind.Controls.Add(_updateCheck);

        Controls.Add(new Label
        {
            Text = "提示：改动即时生效并自动保存。",
            Left = 12, Top = 444, AutoSize = true,
            ForeColor = Color.Gray
        });
        Controls.Add(_closeButton);

        // 模型下拉
        _modelCombo.Items.AddRange(models.Select(m => (object)new ComboItem<ModelInfo>(m.DisplayName, m)).ToArray());
        _modelCombo.SelectedItem = _modelCombo.Items.Cast<ComboItem<ModelInfo>>()
            .FirstOrDefault(i => i.Value.Id == currentModelId) ?? _modelCombo.Items[0];

        // 表情下拉（无表情时禁用）
        _expressionCombo.Items.Add(new ComboItem<string>("(默认)", ""));
        foreach (var exp in expressions) _expressionCombo.Items.Add(new ComboItem<string>(exp, exp));
        _expressionCombo.Enabled = expressions.Count > 0;
        var cur = _expressionCombo.Items.Cast<ComboItem<string>>().FirstOrDefault(i => i.Value == currentExpression);
        _expressionCombo.SelectedItem = cur ?? _expressionCombo.Items[0];

        // 初始化其余控件
        _scaleTrack.Value = (int)Math.Round(settings.Scale * 100);
        _opacityTrack.Value = (int)Math.Round(settings.Opacity * 100);
        _clickThroughCheck.Checked = settings.ClickThrough;
        _draggableCheck.Checked = settings.Draggable;
        _keyboardCheck.Checked = settings.KeyboardInteraction;
        _gazeCheck.Checked = settings.GazeFollow;
        _moodCheck.Checked = settings.MoodExpression;
        _fullscreenCheck.Checked = settings.SuppressOnFullscreen;

        // 帧率下拉
        _fpsCombo.Items.AddRange(new object[]
        {
            new ComboItem<int>("30 FPS", 30),
            new ComboItem<int>("60 FPS", 60),
            new ComboItem<int>("120 FPS", 120)
        });
        _fpsCombo.SelectedItem = _fpsCombo.Items.Cast<ComboItem<int>>().FirstOrDefault(i => i.Value == settings.Fps)
            ?? _fpsCombo.Items[1];

        _soundCheck.Checked = settings.SoundEnabled;
        _volumeTrack.Value = settings.Volume;

        _birthdayBox.Text = settings.Birthday;
        _dndCheck.Checked = settings.DndEnabled;
        _dndStartH.Value = settings.DndStart / 60;
        _dndStartM.Value = settings.DndStart % 60;
        _dndEndH.Value = settings.DndEnd / 60;
        _dndEndM.Value = settings.DndEnd % 60;

        // 快捷键下拉
        foreach (var (name, mods, key) in HotkeyPresets)
            _hotkeyCombo.Items.Add(new ComboItem<(int mods, int key)>(name, (mods, key)));
        var curHot = _hotkeyCombo.Items.Cast<ComboItem<(int mods, int key)>>()
            .FirstOrDefault(i => i.Value.mods == settings.HotkeyModifiers && i.Value.key == settings.HotkeyKey)
            ?? _hotkeyCombo.Items[0];
        _hotkeyCombo.SelectedItem = curHot;

        _chimeCheck.Checked = settings.ChimeEnabled;
        _breakCheck.Checked = settings.BreakReminder;
        _snapCheck.Checked = settings.SnapToEdge;
        _autoHideCheck.Checked = settings.AutoHide;
        _updateCheck.Checked = settings.CheckUpdateOnStartup;

        UpdateLabels();

        // 事件（_loading 守卫避免初始化即写回）
        _modelCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            if (_modelCombo.SelectedItem is ComboItem<ModelInfo> m) _onModelSelected(m.Value);
        };
        _scaleTrack.ValueChanged += (_, _) => { if (_loading) return; _settings.Scale = _scaleTrack.Value / 100.0; UpdateLabels(); _apply(); };
        _opacityTrack.ValueChanged += (_, _) => { if (_loading) return; _settings.Opacity = _opacityTrack.Value / 100.0; UpdateLabels(); _apply(); };
        _expressionCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            if (_expressionCombo.SelectedItem is ComboItem<string> e) _onExpressionSelected(e.Value);
        };
        _clickThroughCheck.CheckedChanged += (_, _) => { if (_loading) return; _settings.ClickThrough = _clickThroughCheck.Checked; _apply(); };
        _draggableCheck.CheckedChanged += (_, _) => { if (_loading) return; _settings.Draggable = _draggableCheck.Checked; _apply(); };
        _keyboardCheck.CheckedChanged += (_, _) => { if (_loading) return; _settings.KeyboardInteraction = _keyboardCheck.Checked; _apply(); };
        _gazeCheck.CheckedChanged += (_, _) => { if (_loading) return; _settings.GazeFollow = _gazeCheck.Checked; _apply(); };
        _moodCheck.CheckedChanged += (_, _) => { if (_loading) return; _settings.MoodExpression = _moodCheck.Checked; _apply(); };
        _fullscreenCheck.CheckedChanged += (_, _) => { if (_loading) return; _settings.SuppressOnFullscreen = _fullscreenCheck.Checked; _apply(); };
        _fpsCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            if (_fpsCombo.SelectedItem is ComboItem<int> f) { _settings.Fps = f.Value; _apply(); }
        };
        _soundCheck.CheckedChanged += (_, _) => { if (_loading) return; _settings.SoundEnabled = _soundCheck.Checked; _apply(); };
        _volumeTrack.ValueChanged += (_, _) => { if (_loading) return; _settings.Volume = _volumeTrack.Value; UpdateLabels(); _apply(); };
        _birthdayBox.Leave += (_, _) =>
        {
            if (_loading) return;
            _settings.Birthday = _birthdayBox.Text.Trim();
            _apply();
        };
        _dndCheck.CheckedChanged += (_, _) => { if (_loading) return; _settings.DndEnabled = _dndCheck.Checked; _apply(); };
        _dndStartH.ValueChanged += (_, _) => { if (_loading) return; _settings.DndStart = (int)_dndStartH.Value * 60 + (int)_dndStartM.Value; _apply(); };
        _dndStartM.ValueChanged += (_, _) => { if (_loading) return; _settings.DndStart = (int)_dndStartH.Value * 60 + (int)_dndStartM.Value; _apply(); };
        _dndEndH.ValueChanged += (_, _) => { if (_loading) return; _settings.DndEnd = (int)_dndEndH.Value * 60 + (int)_dndEndM.Value; _apply(); };
        _dndEndM.ValueChanged += (_, _) => { if (_loading) return; _settings.DndEnd = (int)_dndEndH.Value * 60 + (int)_dndEndM.Value; _apply(); };
        _hotkeyCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            if (_hotkeyCombo.SelectedItem is ComboItem<(int mods, int key)> h)
            {
                _settings.HotkeyModifiers = h.Value.mods;
                _settings.HotkeyKey = h.Value.key;
                _apply();
            }
        };
        _chimeCheck.CheckedChanged += (_, _) => { if (_loading) return; _settings.ChimeEnabled = _chimeCheck.Checked; _apply(); };
        _breakCheck.CheckedChanged += (_, _) => { if (_loading) return; _settings.BreakReminder = _breakCheck.Checked; _apply(); };
        _snapCheck.CheckedChanged += (_, _) => { if (_loading) return; _settings.SnapToEdge = _snapCheck.Checked; _apply(); };
        _autoHideCheck.CheckedChanged += (_, _) => { if (_loading) return; _settings.AutoHide = _autoHideCheck.Checked; _apply(); };
        _updateCheck.CheckedChanged += (_, _) => { if (_loading) return; _settings.CheckUpdateOnStartup = _updateCheck.Checked; _apply(); };
        _resetButton.Click += (_, _) =>
        {
            if (_loading) return;
            if (MessageBox.Show("确定要重置全部养成数据吗？\n（好感/等级/统计/成就/连续天数都清空，且备份一并删除，无法撤销）",
                    "重置养成", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                _onReset?.Invoke();
        };
        _backupButton.Click += (_, _) => { if (!_loading) _onBackup?.Invoke(); };
        _restoreButton.Click += (_, _) => { if (!_loading) _onRestore?.Invoke(); };
        _closeButton.Click += (_, _) => Close();

        _loading = false;
    }

    private void UpdateLabels()
    {
        _scaleLabel.Text = $"{_scaleTrack.Value / 100.0:P0}";
        _opacityLabel.Text = $"{_opacityTrack.Value / 100.0:P0}";
        _volumeLabel.Text = $"{_volumeTrack.Value}%";
    }

    /// <summary>ComboBox 显示文本与底层值的绑定。</summary>
    private sealed class ComboItem<T>
    {
        public string Display { get; }
        public T Value { get; }
        public ComboItem(string display, T value) { Display = display; Value = value; }
        public override string ToString() => Display;
    }
}
