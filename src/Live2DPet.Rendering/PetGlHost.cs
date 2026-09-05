using System;
using Live2DCSharpSDK.App;
using Live2DCSharpSDK.Framework;
using Live2DCSharpSDK.Framework.Motion;
using Live2DCSharpSDK.Framework.Rendering;
using Live2DCSharpSDK.OpenTK;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace Live2DPet.Rendering;

/// <summary>
/// 一帧渲染结果（已转为 BGRA、自上而下、预乘 Alpha 的字节数组）。
/// </summary>
public sealed class FrameData
{
    public byte[] Pixels = Array.Empty<byte>();
    public int Width;
    public int Height;
}

/// <summary>
/// Live2D 渲染宿主：在一个隐藏的 OpenTK GameWindow 上驱动官方 Cubism 引擎，
/// 把模型渲染到离屏画布，再 ReadPixels 合成成带 Alpha 的位图，通过 FrameReady 抛出。
///
/// 重要：OpenTK/GLFW 要求所有调用都在同一个线程（本程序即 WPF 主线程）。
/// 因此本类**不在后台线程跑循环**——由调用方（WPF DispatcherTimer）每帧调用 Tick(dt)。
/// </summary>
public sealed class PetGlHost : IDisposable
{
    private int _width;              // 当前渲染宽度（随缩放变化）
    private int _height;             // 当前渲染高度（随缩放变化）
    private readonly string _modelDir;
    private readonly string _modelName;

    private GameWindow? _window;
    private LAppDelegate? _lapp;
    private LAppModel? _model;

    // 鼠标目标（归一化 -1..1），由外部（MouseReaction）写入；null 表示不跟随。
    private float? _mouseX;
    private float? _mouseY;

    // 状态联动微表情：身体倾斜（ParamBodyAngleX）与头部侧倾（ParamAngleZ），由 ApplyMood 设置，
    // 在每帧动画更新之后、读回像素之前施加，覆盖动画对该参数的本帧设定。
    private float? _moodBodyLean;
    private float? _moodHeadRoll;

    private byte[] _readBuffer = Array.Empty<byte>();
    private FrameData _frame = new();
    private bool _disposed;

    public event Action<FrameData>? FrameReady;

    /// <summary>模型加载完成（在主线程触发），业务层据此读取真实参数并生成映射。</summary>
    public event Action? ModelLoaded;

    /// <summary>
    /// 连续渲染失败达到阈值（GL 上下文丢失 / 驱动重置 / 显卡切换等）时触发，附带最后一次异常。
    /// 只会在"刚跨过阈值"时触发一次；恢复正常（成功渲染一帧）后计数清零，可再次触发。
    /// 业务层据此做恢复（本程序采用保存状态后优雅重启），避免桌宠一直黑屏却毫无反应。
    /// </summary>
    public event Action<Exception>? RenderFaulted;

    /// <summary>连续渲染失败次数（成功一帧即清零）。</summary>
    public int ConsecutiveFaults { get; private set; }

    /// <summary>是否已判定为渲染故障（连续失败达到阈值）。</summary>
    public bool IsFaulted => ConsecutiveFaults >= FaultThreshold;

    private const int FaultThreshold = 5;

    /// <summary>清零故障计数（休眠唤醒后主动调用，避免把唤醒瞬间的一次抖动误判为故障）。</summary>
    public void ResetFaults() => ConsecutiveFaults = 0;

    public PetGlHost(int width, int height, string modelDir, string modelName)
    {
        _width = width;
        _height = height;
        _modelDir = modelDir;
        _modelName = modelName;
    }

    /// <summary>
    /// 必须在 GLFW 主线程（本程序即 WPF 主线程）调用：创建隐藏 GameWindow、初始化 GL、加载模型。
    /// </summary>
    public void Start()
    {
        var nativeSettings = new NativeWindowSettings
        {
            ClientSize = new Vector2i(_width, _height),
            Title = "Live2DPetGL",
            WindowBorder = WindowBorder.Hidden,
            StartVisible = false,
            StartFocused = false,
            Vsync = VSyncMode.Off,
            Flags = ContextFlags.ForwardCompatible
        };

        _window = new GameWindow(GameWindowSettings.Default, nativeSettings);
        GL.Viewport(0, 0, _width, _height);

        // 注意：不能用 Console.WriteLine —— WinExe（双击 exe 无控制台）下写无效句柄会抛 IOException，
        // 导致渲染循环每帧崩溃。这里丢弃 SDK 日志（桌宠无需），如需排查可改为写文件。
        _lapp = new LAppDelegate(new OpenTKApi(_window), _ => { })
        {
            BGColor = new CubismTextureColor(0, 0, 0, 0)
        };

        _model = _lapp.Live2dManager.LoadModel(_modelDir, _modelName);

        _readBuffer = new byte[_width * _height * 4];
        _frame = new FrameData { Pixels = new byte[_width * _height * 4], Width = _width, Height = _height };

        ModelLoaded?.Invoke();
    }

    /// <summary>每帧调用（主线程）：推进引擎、读回像素并抛出帧。</summary>
    public unsafe void Tick(float dt)
    {
        if (_disposed || _window == null || _model == null || _lapp == null) return;

        try
        {
            TickCore(dt);
            ConsecutiveFaults = 0;   // 只要成功渲染一帧，就认为 GL 恢复正常
        }
        catch (Exception ex)
        {
            // GL 上下文丢失（驱动重置 / 休眠唤醒 / 远程桌面 / 独显切换）会让后续所有 GL 调用失败。
            // 这里不直接重建（Cubism SDK 的全局状态重入风险高），而是上报给业务层做进程级恢复。
            ConsecutiveFaults++;
            if (ConsecutiveFaults == FaultThreshold)
                RenderFaulted?.Invoke(ex);
        }
    }

    /// <summary>一帧的真实流程：推引擎 → 施加微表情 → 读回像素 → 抛帧。异常交由 Tick 统计。</summary>
    private unsafe void TickCore(float dt)
    {
        if (_disposed || _window == null || _model == null || _lapp == null) return;

        // 鼠标跟随（引擎内部的 CubismTargetPoint 负责平滑）
        if (_mouseX.HasValue && _mouseY.HasValue)
            _model.SetDragging(_mouseX.Value, _mouseY.Value);

        // 引擎每帧：透明清屏 + 更新 + 绘制
        _lapp.Run(dt);

        // 状态联动微表情：在动画更新之后施加（覆盖本帧身体/头部角度），仅当模型真的有该参数才写，
        // 避免对无此参数的模型抛异常；中性情绪(_moodBodyLean==null)则跳过，交回动画控制。
        if (_model?.Model != null)
        {
            var ps = _model.Parameters;
            if (_moodBodyLean.HasValue && ps.Contains("ParamBodyAngleX"))
                _model.Model.SetParameterValue("ParamBodyAngleX", _moodBodyLean.Value);
            if (_moodHeadRoll.HasValue && ps.Contains("ParamAngleZ"))
                _model.Model.SetParameterValue("ParamAngleZ", _moodHeadRoll.Value);
        }

        // 从默认帧缓冲读回 RGBA（自下而上）
        fixed (byte* p = _readBuffer)
        {
            GL.ReadPixels(0, 0, _width, _height, PixelFormat.Rgba, PixelType.UnsignedByte, (IntPtr)p);
        }

        ConvertToBgraPremultiplied(_readBuffer, _width, _height, _frame.Pixels);

        FrameReady?.Invoke(_frame);

        _window.ProcessEvents(0.0); // ~30fps 由调用方节奏控制（Phase 6 再精细化限帧）
    }

    public void SetMouseTarget(float? nx, float? ny)
    {
        _mouseX = nx;
        _mouseY = ny;
    }

    /// <summary>设置情绪微表情的身体倾斜与头部侧倾角度（度）；传 null 清除覆盖回归动画控制。</summary>
    public void SetMoodLean(float? bodyLeanDeg, float? headRollDeg)
    {
        _moodBodyLean = bodyLeanDeg;
        _moodHeadRoll = headRollDeg;
    }

    /// <summary>模型是否含有指定参数（用于安全施加微表情，避免对无此参数的模型写值抛异常）。</summary>
    public bool HasParameter(string id)
        => _model?.Parameters?.Contains(id) ?? false;

    public LAppModel? Model => _model;

    public System.Collections.Generic.IReadOnlyList<string> AvailableParameterIds
        => _model?.Parameters ?? new System.Collections.Generic.List<string>();

    public void StartMotion(string group, int no, MotionPriority priority = MotionPriority.PriorityNormal)
        => _model?.StartMotion(group, no, priority);

    public void StartRandomMotion(string group, MotionPriority priority = MotionPriority.PriorityNormal)
        => _model?.StartRandomMotion(group, priority);

    public void SetExpression(string expressionId)
        => _model?.SetExpression(expressionId);

    /// <summary>当前模型可用的表情 ID 列表（无表情的模型返回空列表）。</summary>
    public System.Collections.Generic.IReadOnlyList<string> AvailableExpressions
        => _model?.Expressions ?? new System.Collections.Generic.List<string>();

    /// <summary>当前模型可用的动作分组名（如 "Idle"、"TapBody"、"Flick" 等），用于待机随机动作枚举。</summary>
    public System.Collections.Generic.IReadOnlyList<string> AvailableMotionGroups
        => _model?.Motions ?? new System.Collections.Generic.List<string>();

    /// <summary>清除当前表情，回到默认脸。</summary>
    public void ResetExpression()
        => _model?.ResetExpression();

    /// <summary>切换模型：释放当前模型并加载新模型。</summary>
    public void LoadModel(string dir, string name)
    {
        if (_lapp == null) return;
        _lapp.Live2dManager.ReleaseAllModel();
        _model = _lapp.Live2dManager.LoadModel(dir, name);
    }

    /// <summary>
    /// 调整渲染分辨率（缩放）。重设 GameWindow 视口、GL.Viewport 与读回缓冲。
    /// 引擎 LAppView 的投影用"固定 Y 逻辑范围 + ±ratio X 范围"，因此保持宽高比缩放时，
    /// 视口变大/变小后模型会自动等比缩放，无需动模型矩阵，也不会再被 GL 视口裁切。
    /// </summary>
    public void Resize(int w, int h)
    {
        if (_disposed || _window == null) return;
        if (w <= 0 || h <= 0) return;
        if (_width == w && _height == h) return;

        _width = w;
        _height = h;
        _window.ClientSize = new Vector2i(w, h);
        _window.ProcessEvents(0.0);   // 立即应用尺寸变化，确保帧缓冲已按新尺寸重设
        GL.Viewport(0, 0, w, h);
        _readBuffer = new byte[w * h * 4];
        _frame = new FrameData { Pixels = new byte[w * h * 4], Width = w, Height = h };
    }

    /// <summary>
    /// 把 GL 读回的 RGBA（自下而上）转成 DIB 需要的 BGRA（自上而下、预乘 Alpha）。
    /// 由于引擎以透明清屏后用 SRC_ALPHA 混合，读回的 RGB 已近似预乘；
    /// 这里再做一次预乘以保证分层窗口合成正确（半透明边缘可接受）。
    /// </summary>
    private static void ConvertToBgraPremultiplied(byte[] src, int w, int h, byte[] dst)
    {
        int stride = w * 4;
        for (int y = 0; y < h; y++)
        {
            int srcRow = (h - 1 - y) * stride;   // 翻转垂直
            int dstRow = y * stride;
            for (int x = 0; x < w; x++)
            {
                int s = srcRow + x * 4;
                int d = dstRow + x * 4;
                byte r = src[s], g = src[s + 1], b = src[s + 2], a = src[s + 3];
                // 预乘（已是预乘时再乘一次仅轻微压暗边缘，可接受）
                dst[d]     = (byte)(b * a / 255);   // B
                dst[d + 1] = (byte)(g * a / 255);   // G
                dst[d + 2] = (byte)(r * a / 255);   // R
                dst[d + 3] = a;                      // A
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _lapp?.Dispose(); } catch { }
        try { _window?.Dispose(); } catch { }
        GC.SuppressFinalize(this);
    }
}
