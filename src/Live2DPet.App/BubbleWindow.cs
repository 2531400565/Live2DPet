using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Live2DPet.Platform.Native;

namespace Live2DPet.App;

/// <summary>
/// 宠物头顶的透明文字气泡：圆角白底 + 底部小三角，跟随宠物位置。
/// 多条消息按队列逐条显示（每条约 2.6 秒），避免连发时后一条立刻覆盖前一条看不见。
/// 鼠标穿透、不抢焦点（不打断用户当前窗口）。
/// </summary>
public sealed class BubbleWindow : Form
{
    private string _text = "";
    private readonly System.Windows.Forms.Timer _nextTimer;
    private readonly Font _font = new("Microsoft YaHei UI", 12f);
    private const int MaxTextWidth = 360;   // 气泡文字最大宽度，超出自动换行
    private const int PerMessageMs = 2600;  // 每条消息停留时长
    private const int MaxQueue = 6;         // 队列上限，超限丢弃最旧消息，防止堆积刷屏

    // 待显示队列：消息连同它说话时的锚点一起入队，显示时用各自记录的锚点定位
    private readonly Queue<(string text, int cx, int by)> _queue = new();

    public BubbleWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        DoubleBuffered = true;

        _nextTimer = new System.Windows.Forms.Timer { Interval = PerMessageMs };
        _nextTimer.Tick += (_, _) => { _nextTimer.Stop(); ShowNext(); };
    }

    /// <summary>入队一条气泡消息（底部中心锚点 centerX, bottomY）。当前空闲则立即显示，否则排队等本条结束。</summary>
    public void ShowBubble(string text, int centerX, int bottomY)
    {
        _queue.Enqueue((text, centerX, bottomY));
        while (_queue.Count > MaxQueue) _queue.Dequeue();   // 超限丢最旧，防堆积
        if (!Visible) ShowNext();
    }

    /// <summary>取出队首显示，并启动"下一条"定时器；队列空则隐藏。</summary>
    private void ShowNext()
    {
        if (_queue.Count == 0) { Hide(); return; }
        var (text, cx, by) = _queue.Dequeue();
        Display(text, cx, by);
        _nextTimer.Stop();
        _nextTimer.Interval = PerMessageMs;
        _nextTimer.Start();
    }

    private void Display(string text, int centerX, int bottomY)
    {
        _text = text;
        SizeF textSize;
        using (var g = CreateGraphics())
            textSize = g.MeasureString(text, _font, MaxTextWidth);

        int w = (int)Math.Ceiling(textSize.Width) + 44;
        int h = (int)Math.Ceiling(textSize.Height) + 40;
        if (w < 80) w = 80;

        int x = centerX - w / 2;
        int y = bottomY - h;

        // 夹回虚拟屏幕可见区
        int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        if (x < vx) x = vx;
        if (x + w > vx + vw) x = Math.Max(vx, vx + vw - w);
        if (y < vy) y = vy;
        if (y + h > vy + vh) y = Math.Max(vy, vy + vh - h);

        Width = w;
        Height = h;
        Location = new Point(x, y);
        Show();
    }

    // 鼠标穿透 + 不抢焦点 + 不在任务栏
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT;
            return cp;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var body = new Rectangle(0, 0, Width - 1, Height - 14);   // 底部留三角
        using var brush = new SolidBrush(Color.White);
        using var pen = new Pen(Color.FromArgb(210, 210, 210));
        using var path = RoundedRect(body, 14);
        g.FillPath(brush, path);
        g.DrawPath(pen, path);

        // 底部指向宠物的小三角
        var tri = new PointF[]
        {
            new(Width / 2f - 9, Height - 14),
            new(Width / 2f + 9, Height - 14),
            new(Width / 2f, Height - 1)
        };
        g.FillPolygon(brush, tri);

        using var textBrush = new SolidBrush(Color.FromArgb(60, 60, 60));
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(_text, _font, textBrush, new RectangleF(22, 12, Width - 44, Height - 30), fmt);
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _nextTimer.Dispose();
            _font.Dispose();
        }
        base.Dispose(disposing);
    }
}
