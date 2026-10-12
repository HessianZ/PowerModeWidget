# PowerModeWidget

免费的 Windows 11 桌面电源模式小组件。用三个明显的按钮切换 **最佳能效 / 平衡 / 最佳性能**，并显示系统当前模式。

[下载最新版](https://github.com/HessianZ/PowerModeWidget/releases/latest)

![三态界面预览（离屏渲染）](docs/screenshot.png)

## 使用

1. 下载 `PowerModeWidget-win-x64.zip` 并解压。
2. 保留 `PowerModeWidget.exe.config` 与 EXE 在同一目录，双击 `PowerModeWidget.exe`，点击想要的模式。
3. 拖动顶部标题区域移动小组件；点击右上角 × 退出。

小组件设计为由 Explorer 桌面拥有的小窗口，保持不置顶，不显示任务栏图标，也不出现在 Alt+Tab 列表中。普通应用窗口可以遮住它；点击任务栏“显示桌面”或按 Win+D 返回桌面使用。程序在后台运行，点击 × 即可结束。

v0.3.0 修复了 v0.2.0 在部分桌面上无法显示的问题。用户已在真实桌面确认完整画面、三态点击、标题拖动、普通应用覆盖、Win+D 返回及无任务栏图标均正常。

切换后会读取系统状态确认结果；在 Windows 设置中切换模式，小组件也会同步显示。

## 兼容性与权限

- 面向 Windows 11 x64，使用系统内置 .NET Framework 4.8 / 4.8.1，无需下载 .NET 10 运行时。v0.3.0 的 EXE 约 21 KB，ZIP 不附带运行时。
- 默认以普通用户权限运行，不需要管理员权限。
- 桌面附着使用 Explorer 内部窗口结构；要求使用 Windows 默认 Explorer 桌面。找不到可用桌面宿主时会提示失败并退出。
- 切换的是 Windows **电源模式**，不是控制面板的传统电源计划。
- 设备和当前电源计划必须支持电源模式；通常需要使用“平衡”基础电源计划。程序不会自动改变基础电源计划，不支持的情况会显示错误。
- 没有开机自启、托盘或后台计划任务；不联网、不收集使用数据。

底层调用 Windows 的电源 overlay 接口 `PowerGetActualOverlayScheme` / `PowerSetActiveOverlayScheme`；入口点不可用或系统拒绝切换时会提示失败。模式含义可参考 [Microsoft 电源 overlay 文档](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options#overlay-scheme-and-ppm-profile-support)，接口调用示例见 [Microsoft Learn 社区问答](https://learn.microsoft.com/en-gb/answers/questions/1194268/edit-a-posh-script-to-adjust-the-power-mode-slider)。

## 开发

界面为 WPF `UserControl`，由 `HwndSource` 创建 `WS_POPUP` 窗口，owner 设置为 Progman / WorkerW 桌面宿主。窗口采用 `NOACTIVATE` / `TOOLWINDOW` 样式、逐像素透明和软件绘制；使用非置顶的 `HWND_TOP` 顺序，普通应用激活后可以覆盖。

开发需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 和 [.NET Framework 4.8 Developer Pack](https://dotnet.microsoft.com/download/dotnet-framework/net48)。SDK 用于编译，应用目标为 .NET Framework 4.8。在 Windows 上运行：

```powershell
dotnet build src/PowerModeWidget/PowerModeWidget.csproj -c Release
dotnet run --project tests/PowerModeWidget.Tests/PowerModeWidget.Tests.csproj -c Release
dotnet run --project src/PowerModeWidget/PowerModeWidget.csproj
```

离线测试不改变系统电源模式。真实 API 验证使用下面的命令，会临时切换模式并恢复原始模式：

```powershell
dotnet run --project tests/PowerModeWidget.Tests/PowerModeWidget.Tests.csproj -c Release -- --live
```

程序化检查桌面 owner、按钮事件、外部同步及错误反馈：

```powershell
dotnet run --project tests/PowerModeWidget.Tests/PowerModeWidget.Tests.csproj -c Release -- --ui
```

该命令会临时切换并恢复原始电源模式；截图和报告位于 `artifacts/`。原生可见标志、命中检查及 `RenderTargetBitmap` 预览只能验证各自的状态和布局；实际桌面显示、鼠标点击及 Win+D 行为还需要用户在桌面上验证。

发布便携 ZIP：执行 Release 构建后，将 `src/PowerModeWidget/bin/Release/net48/` 下的 `PowerModeWidget.exe`、`PowerModeWidget.exe.config` 与仓库的 `README.md`、`LICENSE` 复制到新的空目录，再压缩为 `PowerModeWidget-win-x64.zip`。不要把旧版自包含运行时目录混入下载包。

GitHub Actions 在提交和 PR 上构建、运行离线测试并生成下载包；推送 `v*` 标签会发布 GitHub Release。

开发计划与验收标准见 [docs/PLAN.md](docs/PLAN.md)。MIT 许可证。
