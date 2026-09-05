using System;
using System.Collections.Generic;
using System.IO;
using Live2DCSharpSDK.Framework.Motion;
using Live2DPet.Core;
using Live2DPet.Core.Live2D;
using Live2DPet.Core.Pet;

namespace Live2DPet.Rendering;

/// <summary>
/// Live2D 业务门面实现：拥有 PetGlHost（渲染宿主），
/// 把帧事件、参数解析、动作/表情控制暴露给 PetController。
/// </summary>
public sealed class Live2DManager : ILive2DManager, IDisposable
{
    private PetGlHost? _host;
    private Dictionary<string, ResolvedParam> _resolved = new();
    private bool _stopped;
    private static readonly Random _rng = new();

    public event Action<FrameData>? FrameAvailable;

    /// <summary>渲染连续失败（多半是 GL 上下文丢失）达到阈值时触发，业务层据此恢复。</summary>
    public event Action<Exception>? RenderFaulted;

    /// <summary>当前是否处于"渲染故障"状态（连续失败已达阈值）。</summary>
    public bool IsRenderFaulted => _host?.IsFaulted ?? false;

    /// <summary>重置故障计数（休眠唤醒等场景主动调用，避免旧计数误导恢复逻辑）。</summary>
    public void ResetFaultCount() => _host?.ResetFaults();

    public IReadOnlyDictionary<string, ResolvedParam> ResolvedParameters => _resolved;

    /// <summary>启动渲染宿主并加载指定模型。必须在主线程调用。</summary>
    public void Start(string modelDir, string modelName)
    {
        const int w = 420;
        const int h = 680;

        _host = new PetGlHost(w, h, modelDir, modelName);
        _host.FrameReady += f => FrameAvailable?.Invoke(f);
        _host.ModelLoaded += OnModelLoaded;
        _host.RenderFaulted += ex => RenderFaulted?.Invoke(ex);
        _host.Start();
    }

    /// <summary>运行时切换模型：释放旧模型、加载新模型并重新解析参数。</summary>
    public void SwitchModel(string modelDir, string modelName)
    {
        if (_host == null) return;
        _host.LoadModel(modelDir, modelName);
        OnModelLoaded();
    }

    /// <summary>每帧推进（由 WPF DispatcherTimer 在主线程调用）。</summary>
    public void Tick(float dt)
    {
        if (_stopped) return;
        _host?.Tick(dt);
    }

    private void OnModelLoaded()
    {
        if (_host == null) return;
        var ids = _host.AvailableParameterIds;
        var mapPath = Path.Combine(AppContext.BaseDirectory, "config", "parameter-mapping.json");
        var map = ModelLoader.LoadOrCreate(mapPath);
        _resolved = ParameterMapping.Resolve(map, ids);

        AppLog.Info("[Live2D] resolved parameters:");
        foreach (var kv in _resolved)
            AppLog.Info($"  {kv.Key} -> {(kv.Value.Present ? kv.Value.ActualId : "NOT FOUND (模型无此参数)")}");
    }

    public void Stop()
    {
        _stopped = true;
    }

    public void SetMouseTarget(float? nx, float? ny) => _host?.SetMouseTarget(nx, ny);

    public void PlayMotion(string group, int no, MotionPriority priority = MotionPriority.PriorityNormal)
        => _host?.StartMotion(group, no, priority);

    public void PlayRandomMotion(string group, MotionPriority priority = MotionPriority.PriorityNormal)
        => _host?.StartRandomMotion(group, priority);

    /// <summary>互动反应：强制优先级，可打断当前动作，保证每次触发都有可见反馈。
    /// 不同模型的动作用了不同分组名（Hiyori 是 Tap/Flick/Tap@Body，Natori/Haru/Mao 只有 TapBody），
    /// 这里做"语义回退"：精确分组不存在时按相近语义依次降级，保证换模型后互动不至于失效。</summary>
    public void PlayReaction(string group)
    {
        if (_host == null) return;
        var groups = _host.AvailableMotionGroups;
        if (groups.Count == 0) return;
        var has = new HashSet<string>(groups, StringComparer.OrdinalIgnoreCase);
        string resolved = ResolveReactionGroup(has, group);
        _host.StartRandomMotion(resolved, MotionPriority.PriorityForce);
    }

    /// <summary>把语义动作分组解析为当前模型真实存在的分组（无精确匹配则按相近语义回退）。</summary>
    private static string ResolveReactionGroup(HashSet<string> has, string group)
    {
        if (has.Contains(group)) return group;
        // 回退链：身体/轻点 → 弹动 → 待机（所有模型都至少有 Idle）
        string[] fallback = { "TapBody", "Tap@Body", "Tap", "Flick", "Idle" };
        foreach (var f in fallback)
            if (has.Contains(f)) return f;
        return group;   // 都没有则原样交给引擎（安全无反应，不抛异常）
    }

    /// <summary>当前模型可用的动作分组名（用于待机随机动作枚举）。</summary>
    public IReadOnlyList<string> AvailableMotionGroups
        => _host?.AvailableMotionGroups ?? Array.Empty<string>();

    /// <summary>待机随机动作：从给定的分组里随机挑一个播放（普通优先级，不抢用户的互动反应）。
    /// 分组列表为空时回退到 SDK 默认的 "Idle" 分组。</summary>
    public void PlayIdleMotion(IEnumerable<string> groups)
    {
        var list = groups as IReadOnlyList<string> ?? groups.ToList();
        string group = list.Count > 0 ? list[_rng.Next(list.Count)] : "Idle";
        _host?.StartRandomMotion(group, MotionPriority.PriorityNormal);
    }

    public void PlayExpression(string id) => _host?.SetExpression(id);

    /// <summary>当前模型可用的表情 ID 列表（无表情的模型返回空列表）。</summary>
    public IReadOnlyList<string> AvailableExpressions
        => _host?.AvailableExpressions ?? Array.Empty<string>();

    /// <summary>清除当前表情，回到默认脸。</summary>
    public void ResetExpression() => _host?.ResetExpression();

    public void Resize(int width, int height) => _host?.Resize(width, height);

    /// <summary>状态联动微表情：把情绪翻译成身体/头部倾斜角度交给渲染宿主施加。</summary>
    public void ApplyMood(PetMood mood)
    {
        if (_host == null) return;
        switch (mood)
        {
            case PetMood.Happy:     _host.SetMoodLean(7f, 4f); break;   // 身体右倾 + 头部微侧（俏皮）
            case PetMood.Sad:       _host.SetMoodLean(-5f, -3f); break; // 身体左倾 + 低头（委屈）
            case PetMood.Surprised: _host.SetMoodLean(11f, 0f); break; // 身体后仰（受惊）
            default:                _host.SetMoodLean(null, null); break; // Neutral：清除覆盖
        }
    }

    public void Dispose()
    {
        _stopped = true;
        _host?.Dispose();
        _host = null;
    }
}
