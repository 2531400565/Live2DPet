using System;
using System.Collections.Generic;
using Live2DCSharpSDK.Framework.Motion;
using Live2DPet.Core.Live2D;
using Live2DPet.Core.Pet;

namespace Live2DPet.Rendering;

/// <summary>Live2D 业务门面：掩藏引擎细节，只暴露宠物逻辑需要的接口。</summary>
public interface ILive2DManager
{
    /// <summary>每帧渲染结果（BGRA 位图）就绪时触发。</summary>
    event Action<FrameData>? FrameAvailable;

    /// <summary>解析后的语义参数（模型加载完成后填充）。</summary>
    IReadOnlyDictionary<string, ResolvedParam> ResolvedParameters { get; }

    void Start(string modelDir, string modelName);
    void Stop();
    /// <summary>运行时切换模型：释放旧模型、加载新模型并重新解析参数。</summary>
    void SwitchModel(string modelDir, string modelName);

    /// <summary>设置鼠标跟随目标（归一化 -1..1）；传 null 关闭跟随。</summary>
    void SetMouseTarget(float? nx, float? ny);

    void PlayMotion(string group, int no, MotionPriority priority = MotionPriority.PriorityNormal);
    void PlayRandomMotion(string group, MotionPriority priority = MotionPriority.PriorityNormal);
    /// <summary>触发一次"互动反应"：用 PriorityForce 强制播放，可打断当前动作，每次按键必有可见反馈。</summary>
    void PlayReaction(string group);
    void PlayExpression(string id);
    /// <summary>当前模型可用的表情 ID 列表（无表情的模型返回空列表）。</summary>
    IReadOnlyList<string> AvailableExpressions { get; }
    /// <summary>清除当前表情，回到默认脸。</summary>
    void ResetExpression();
    /// <summary>调整渲染分辨率（缩放）。模型会随视口等比缩放，无裁切。</summary>
    void Resize(int width, int height);

    /// <summary>状态联动微表情：根据当前情绪对模型施加轻微身体/头部倾斜（开心/难过/受惊）。
    /// Neutral 时清除覆盖，交回动画系统控制。</summary>
    void ApplyMood(PetMood mood);
}
