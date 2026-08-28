namespace Live2DPet.Core.Mouse;

/// <summary>
/// 把「全局光标位置」映射成 Live2D 引擎需要的归一化跟随目标（x,y ∈ [-1, 1]）。
///
/// 纯函数：不依赖 Win32、不依赖 Live2D 引擎，便于单元测试与后续在设置里调灵敏度。
///
/// 设计要点：
/// - 以「宠物中心到光标」的偏移量除以一个参考距离（默认 = 宠物较大边的一半）得到归一化目标；
///   光标离宠物约一个宠物身位即达到满幅（±1），再远也不会超过 ±1（天然裁剪）。
/// - 屏幕坐标系 Y 轴向下，Live2D 的 ParamEyeBallY / ParamAngleY「向上为正」，故此处把 dy 翻转
///   （光标在宠物上方 → ny 为正 → 模型抬头/眼珠上转）。
/// </summary>
public static class MouseFollow
{
    /// <summary>
    /// 计算“桌宠看向光标”的归一化目标。
    /// </summary>
    /// <param name="cursorX">光标屏幕 X（左=0）。</param>
    /// <param name="cursorY">光标屏幕 Y（上=0）。</param>
    /// <param name="petX">桌宠窗口左上角屏幕 X。</param>
    /// <param name="petY">桌宠窗口左上角屏幕 Y。</param>
    /// <param name="petW">桌宠窗口宽。</param>
    /// <param name="petH">桌宠窗口高。</param>
    /// <param name="referencePx">归一化参考距离（px）。为 null 时取宠物较大边的一半。</param>
    /// <returns>(Nx, Ny)，范围 [-1, 1]。</returns>
    public static (float Nx, float Ny) ComputeTarget(
        int cursorX, int cursorY,
        int petX, int petY, int petW, int petH,
        float? referencePx = null)
    {
        float petCx = petX + petW / 2f;
        float petCy = petY + petH / 2f;

        float reference = referencePx ?? System.Math.Max(petW, petH) * 0.5f;
        if (reference <= 0f) reference = 1f;

        float dx = cursorX - petCx;
        float dy = cursorY - petCy;

        float nx = Clamp(dx / reference, -1f, 1f);
        float ny = Clamp(-dy / reference, -1f, 1f); // 屏幕 Y 向下 → 翻转为“向上为正”
        return (nx, ny);
    }

    private static float Clamp(float v, float min, float max)
        => v < min ? min : (v > max ? max : v);
}
