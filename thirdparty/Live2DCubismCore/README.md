# 放置 Live2DCubismCore.dll 的目录

Live2D Cubism 的原生运行时 **`Live2DCubismCore.dll`** 按许可协议**不在 GitHub 公开**，
只能从 Live2D 官网下载的 Cubism SDK for Native 包里取得。

## 获取步骤（免费，需 Live2D 账号）
1. 打开 https://www.live2d.com/en/sdk/download/native/ （中文：https://www.live2d.com/zh-CHS/sdk/download/native/ ）
2. 用免费 Live2D 账号登录后下载 **Cubism SDK for Native**（例如 `CubismSdkForNative-5-r.1`）。
3. 解压后进入 `Core/dll/windows/x86_64/`，复制 `Live2DCubismCore.dll`。
4. 把该 DLL 放到本目录（`thirdparty/Live2DCubismCore/`），即与此 README 同级。

放置后重新生成/运行，构建脚本会自动把它拷贝到输出目录（缺失也不会导致编译失败，只是运行时会因加载不到模型而报错）。

> 该 DLL 是官方再发行文件，仅用于本地运行本项目，请遵守 Live2D 许可协议。
