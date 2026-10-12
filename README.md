# PowerModeWidget

免费的 Windows 11 桌面电源模式小组件。用三个明显的按钮切换 **最佳能效 / 平衡 / 最佳性能**，并显示系统当前模式。

[下载最新版](https://github.com/HessianZ/PowerModeWidget/releases/latest)

![三态桌面小组件](docs/screenshot.png)

## 使用

1. 下载 `PowerModeWidget-win-x64.zip` 并解压。
2. 双击 `PowerModeWidget.exe`，点击想要的模式。
3. 拖动窗口顶部区域移动小组件；使用置顶开关控制它是否覆盖其他窗口。

切换后会读取系统状态确认结果；在 Windows 设置中切换模式，小组件也会同步显示。关闭窗口即可退出。

## 兼容性与权限

- 面向 Windows 11 x64；发布包内含 .NET 运行时，无需另外安装 .NET。
- 默认以普通用户权限运行，不需要管理员权限。
- 切换的是 Windows **电源模式**，不是控制面板的传统电源计划。
- 设备和当前电源计划必须支持电源模式；通常需要使用“平衡”基础电源计划。程序不会自动改变基础电源计划，不支持的情况会显示错误。
- 单文件程序首次启动会将原生运行时文件解压到用户的临时目录。
- 没有开机自启、托盘或后台计划任务；不联网、不收集使用数据。

底层调用 Windows 的电源 overlay 接口 `PowerGetActualOverlayScheme` / `PowerSetActiveOverlayScheme`；入口点不可用或系统拒绝切换时会提示失败。模式含义可参考 [Microsoft 电源 overlay 文档](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options#overlay-scheme-and-ppm-profile-support)，接口调用示例见 [Microsoft Learn 社区问答](https://learn.microsoft.com/en-gb/answers/questions/1194268/edit-a-posh-script-to-adjust-the-power-mode-slider)。

## 开发

安装 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)，在 Windows 上运行：

```powershell
dotnet build src/PowerModeWidget/PowerModeWidget.csproj -c Release
dotnet run --project tests/PowerModeWidget.Tests/PowerModeWidget.Tests.csproj -c Release
dotnet run --project src/PowerModeWidget/PowerModeWidget.csproj
```

离线测试不改变系统电源模式。真实 API 验证使用下面的命令，会临时切换模式并恢复原始模式：

```powershell
dotnet run --project tests/PowerModeWidget.Tests/PowerModeWidget.Tests.csproj -c Release -- --live
```

实际界面按钮、置顶、外部同步及错误反馈验收：

```powershell
dotnet run --project tests/PowerModeWidget.Tests/PowerModeWidget.Tests.csproj -c Release -- --ui
```

该命令也会临时切换并恢复原始电源模式，截图和报告位于 `artifacts/`。

发布自包含单文件：

```powershell
dotnet publish src/PowerModeWidget/PowerModeWidget.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/publish
```

GitHub Actions 在提交和 PR 上构建、运行离线测试并生成下载包；推送 `v*` 标签会发布 GitHub Release。

开发计划与验收标准见 [docs/PLAN.md](docs/PLAN.md)。MIT 许可证。
