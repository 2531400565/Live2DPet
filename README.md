# Live2DPet · Live2D 桌面宠物

[![build](https://github.com/2531400565/Live2DPet/actions/workflows/build.yml/badge.svg)](https://github.com/2531400565/Live2DPet/actions/workflows/build.yml)

一个运行在 Windows 上的 **Live2D 桌宠**：一只半透明、置顶、会跟着鼠标转头、能被摸头、有情绪和养成系统的二次元小伙伴。基于 .NET 8 + WinForms + OpenTK，渲染层使用 [Live2DCSharpSDK](thirdparty/Live2DCSharpSDK)。

---

## ✨ 功能特性

- **透明置顶窗口**：无边框分层窗口，桌宠“浮”在桌面最上层，可贴边、可拖动。
- **Live2D 渲染**：OpenTK（OpenGL）实时渲染模型，支持眨眼、呼吸、待机随机动作。
- **互动**：鼠标平滑跟随 / 眼神头部跟随、点击 / 拖拽（带惯性弹性）、双击、右键菜单、全局快捷键隐藏 / 显示。
- **表情与动作联动**：表情接入、心情驱动的待机行为、受惊吓 / 摸头 / 戳肚子不同反应。
- **养成系统**：好感度、经验、等级、统计面板；签到（连续天数 + 每日奖励）、离线补偿（按离开时长欢迎回来）。
- **成就系统**：14 项成就，解锁时发放好感 / 经验奖励，跨级自动播报升级。
- **节日彩蛋**：农历节日（春节 / 元宵 / 龙抬头 / 端午 / 七夕 / 中元 / 中秋 / 重阳 / 腊八）+ 更多公历节日（3·8 / 5·1 / 9·10 / 11·11 / 12·31）。
- **免打扰（专注）模式**：可设时间段（默认 23:00–08:00，支持跨午夜），期间抑制环境气泡。
- **离开检测**：用户闲置时自动“睡觉”待机，回归时唤醒。
- **一键截图分享**：托盘菜单截图，自动复制全屏 PNG 到剪贴板。
- **音效增强**：音量可调，按音量实时缩放 PCM 振幅（无需额外依赖）。
- **崩溃自启**：未捕获异常后自动拉起（最多 3 次），避免宠物“消失”。
- **数据容灾**：`petstate.json` 保存前生成快照与带时间戳备份，损坏时自动回退。
- **多模型**：内置 `default / Haru / Mao / Natori`，设置内可切换。
- **开机自启 + 多屏位置记忆**、**单实例**（重复启动自动激活已有进程）。
- **高 DPI 自适应**（v1.1）：拖到不同缩放的显示器或改系统缩放时，按新 DPI 重算渲染分辨率，不再被系统拉伸成模糊贴图。
- **休眠 / 唤醒自恢复**（v1.1）：进入休眠即暂停渲染并落盘进度，唤醒后重置时间基准（不把几小时算进一帧）、把桌宠拉回可见区域。
- **渲染故障自恢复**（v1.1）：GL 上下文丢失（驱动重置 / 独显切换 / 远程桌面）会连续渲染失败或持续空帧，此时自动保存状态并"接力重启"，不再黑屏装死。
- **配置备份 / 还原**（v1.1）：设置、养成进度、参数映射打包成一个 zip，换机或重装后一键还原。
- **运行日志**（v1.1）：统一写 `logs/app.log`（按大小滚动，保留 3 份），托盘菜单可直接打开日志目录。
- **自动更新**（v1.2）：启动后静默检查 GitHub Release，发现新版本弹气泡提示；托盘「检查更新…」可一键下载（SHA256 校验）并热替换重启，无需手动下 zip。

---

## 🧰 环境要求

- Windows 10 / 11（需要桌面合成 / DWM）
- [.NET 8 运行时](https://dotnet.microsoft.com/download/dotnet/8.0)（若选择自包含发布则无需安装）

---

## 📁 目录结构

```
Live2DPet/
├─ Live2DPet.sln
├─ src/
│  ├─ Live2DPet.App/        启动、窗口、托盘、设置窗、声音、养成接线（net8.0-windows）
│  ├─ Live2DPet.Core/       养成模型、成就、对话、设置、互动/鼠标逻辑（net8.0）
│  ├─ Live2DPet.Platform/   原生窗口、键盘、自动启动、托盘（net8.0-windows）
│  └─ Live2DPet.Rendering/  OpenTK + Live2D 渲染宿主（net8.0-windows）
├─ assets/
│  ├─ models/               Live2D 模型（default / Haru / Mao / Natori）
│  └─ sounds/               音效
├─ config/                  参数映射等配置模板
├─ thirdparty/
│  ├─ Live2DCSharpSDK/      Live2D C# SDK（含自身 LICENSE）
│  └─ Live2DCubismCore/     Cubism Core 原生库
└─ tools/                   图标 / 音效生成脚本
```

> 运行时用户数据位于程序所在目录的 `config/` 下（`settings.json`、`petstate.json`、`parameter-mapping.json`），养成备份在 `config/backups/`。

---

## 🔧 构建

使用 .NET 8 SDK：

```bash
dotnet publish src/Live2DPet.App/Live2DPet.App.csproj -c Release -r win-x64 --self-contained false
```

产物：

```
src/Live2DPet.App/bin/Release/net8.0-windows/win-x64/publish/Live2DPet.App.exe
```

> 若目标机未装 .NET 8 运行时，可去掉 `--self-contained false`（改为 `--self-contained true`）打包为自包含可执行。

---

## ▶️ 运行

- 双击 `publish/Live2DPet.App.exe`（或桌面快捷方式）。
- 托盘图标右键打开**设置**、**截图桌宠**等。
- 全局快捷键可隐藏 / 显示桌宠（在设置中可配置）。

---

## 🧑‍🎨 添加自己的模型

1. 在 `assets/models/` 下新建文件夹，放入模型文件（`*.model3.json` 及对应 `motions/`、`expressions/`、贴图等）。
2. 在 `config/parameter-mapping.json` 中按需补充参数映射（可选）。
3. 重新构建并在设置中切换模型即可。

切换模型时会自动释放旧 GL 纹理，避免显存泄漏。

---

## ⚠️ 许可与版权

- 本项目源码采用 MIT 许可证（见仓库 `LICENSE`）。
- 渲染依赖 [Live2DCSharpSDK](thirdparty/Live2DCSharpSDK)（遵循其自带 `LICENSE`）。
- **Live2D Cubism 运行时**（Cubism Core）归 Live2D Inc. 所有，须遵守 [Live2D 官方许可条款](https://www.live2d.com/eula/)；本项目仅用于学习 / 非商业用途，商业使用请自行向 Live2D 申请授权。

---

## 📝 提交说明

本仓库初始提交包含：透明置顶窗口、Live2D 渲染、鼠标 / 键盘互动、表情动作联动、养成系统、签到与离线补偿、节日彩蛋、免打扰、闲置睡觉、一键截图、崩溃自启、音效音量、设置持久化与 `petstate` 备份容灾。
