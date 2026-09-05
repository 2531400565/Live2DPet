using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Live2DPet.Core.Update;

namespace Live2DPet.App.Update;

/// <summary>
/// 更新对话框：展示新版本号与更新日志（Release 说明），提供"下载并安装"按钮与进度条。
/// 下载在校验完成后会触发宿主退出并由外部脚本完成热替换重启，因此本窗体无需自行复制文件。
/// </summary>
public sealed class UpdateForm : Form
{
    private readonly UpdateService _service;
    private readonly UpdateInfo _info;
    private readonly Label _statusLabel;
    private readonly TextBox _logBox;
    private readonly ProgressBar _bar;
    private readonly Button _installButton;
    private CancellationTokenSource? _cts;

    public UpdateForm(UpdateService service, UpdateInfo info)
    {
        _service = service;
        _info = info;

        Text = "发现新版本";
        ClientSize = new Size(460, 360);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9f);

        _statusLabel = new Label
        {
            Left = 12, Top = 10, Width = 436, AutoSize = true,
            Text = $"当前 {service.CurrentVersion} → 最新 {info.Version}（{info.Name}）"
        };
        Controls.Add(_statusLabel);

        _logBox = new TextBox
        {
            Left = 12, Top = 36, Width = 436, Height = 230,
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Text = string.IsNullOrWhiteSpace(info.Body) ? "(无更新说明)" : info.Body
        };
        Controls.Add(_logBox);

        _bar = new ProgressBar { Left = 12, Top = 274, Width = 436, Height = 18, Visible = false, Maximum = 100 };
        Controls.Add(_bar);

        _installButton = new Button { Text = "下载并安装", Left = 280, Top = 302, Width = 168, Height = 34 };
        _installButton.Click += async (_, _) => await StartInstall();
        Controls.Add(_installButton);

        var later = new Button { Text = "稍后", Left = 12, Top = 302, Width = 100, Height = 34 };
        later.Click += (_, _) => Close();
        Controls.Add(later);
    }

    private async Task StartInstall()
    {
        _installButton.Enabled = false;
        _bar.Visible = true;
        _bar.Value = 0;
        _statusLabel.Text = "正在下载…";
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<(long Received, long Total)>(p =>
            {
                if (p.Total > 0)
                {
                    _bar.Value = (int)(p.Received * 100 / p.Total);
                    _statusLabel.Text = $"正在下载… {p.Received / 1_048_576} / {p.Total / 1_048_576} MB";
                }
            });
            await _service.DownloadAndApplyAsync(_info, progress, _cts.Token);
            _statusLabel.Text = "下载完成，即将重启以完成更新…";
        }
        catch (OperationCanceledException)
        {
            _installButton.Enabled = true;
            _bar.Visible = false;
            _statusLabel.Text = "已取消";
        }
        catch (Exception ex)
        {
            _bar.Visible = false;
            _installButton.Enabled = true;
            _statusLabel.Text = "更新失败";
            MessageBox.Show($"更新失败：{ex.Message}", "Live2DPet", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts?.Cancel();
        base.OnFormClosing(e);
    }
}
