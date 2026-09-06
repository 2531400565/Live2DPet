using Xunit;

// PetDialogue 是静态可变文案库，部分测试会热应用（ApplyOverrides）并还原全局状态。
// 关闭并行执行，避免与其它读取 PetDialogue 的测试类产生竞态导致的偶发失败。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
