using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Live2DPet.Core.Pet;

namespace Live2DPet.App;

/// <summary>
/// 关于面板：显示程序版本、作者、GitHub 链接与许可声明。
/// 纯展示（无业务逻辑），由托盘菜单"关于"触发。
/// </summary>
public sealed class AboutForm : Form
{
    private const string RepoUrl = "https://github.com/2531400565/Live2DPet";

    public AboutForm(string? petName = null)
    {
        Text = "关于 Live2DPet";
        ClientSize = new Size(360, 268);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9f);
        ShowInTaskbar = false;

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        var icon = LoadAppIcon();
        if (icon != null)
        {
            var pic = new PictureBox
            {
                Image = icon.ToBitmap(),
                Size = new Size(48, 48),
                Location = new Point(16, 14),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            Controls.Add(pic);
        }

        Controls.Add(new Label
        {
            Text = "Live2D 桌宠",
            Location = new Point(76, 16),
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        });
        Controls.Add(new Label
        {
            Text = $"版本 {version}  ·  .NET 8 / WinForms / OpenTK",
            Location = new Point(76, 42),
            AutoSize = true,
            ForeColor = Color.Gray
        });
        Controls.Add(new Label
        {
            Text = "透明置顶实时渲染的 Live2D 桌面宠物：互动养成、\n成就签到、节日彩蛋、免打扰、一键截图与崩溃自启。",
            Location = new Point(16, 78),
            AutoSize = true
        });

        // 当前陪伴的宠物昵称（与设置里的名字保持同步；未起名时显示默认昵称）
        var name = string.IsNullOrWhiteSpace(petName) ? PetDialogue.DefaultPetName : petName.Trim();
        Controls.Add(new Label
        {
            Text = $"当前陪伴：{name}",
            Location = new Point(16, 120),
            AutoSize = true,
            ForeColor = Color.FromArgb(0x2B, 0x57, 0x9A)
        });

        var link = new LinkLabel
        {
            Text = RepoUrl,
            Location = new Point(16, 140),
            AutoSize = true
        };
        link.LinkClicked += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true }); }
            catch { /* 打开浏览器失败忽略 */ }
        };
        Controls.Add(link);

        Controls.Add(new Label
        {
            Text = "MIT License · © 2026 时可凡\nLive2D Cubism 运行时归 Live2D Inc. 所有，仅限学习/非商业使用。",
            Location = new Point(16, 176),
            AutoSize = true,
            ForeColor = Color.Gray
        });

        var close = new Button { Text = "关闭", Location = new Point(272, 226), Size = new Size(72, 30) };
        close.Click += (_, _) => Close();
        Controls.Add(close);
    }

    private static Icon? LoadAppIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                return Icon.ExtractAssociatedIcon(path);
        }
        catch { /* 回退到默认 */ }
        return null;
    }
}
