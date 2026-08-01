# SteelSeries Assist

SteelSeries Assist 是 SteelSeries GG / Sonar 的轻量级 Windows 托盘控制面板。

当前版本：`0.0.3`

当前已经完成第一轮可运行 MVP：程序单实例常驻托盘，点击图标可快速调整 Sonar 各通道音量、静音、物理输入/输出设备绑定，并可将当前活跃的播放应用路由到 Game、Chat、Media 或 Aux。

应用路由采用与 GG 相近的声卡分区布局：Game、Chat、Media、Aux 分别作为路由卡片，每张卡片列出当前实际连接到该声卡的应用。可直接把应用拖到另一张卡片来切换该进程的默认输出；拖动时会显示应用浮层、来源与目标声卡提示。OBS 这类同时建立多条音频会话的应用会出现在所有实际连接的声卡下，不会被错误折叠成单一路由。

音量和静音状态通过 Sonar WebSocket 实时同步。键盘媒体键或 GG 中的变化会自动更新 Assist；Assist 调整 Game、Chat、Media、Aux、Mic 时通过对应的 Windows Sonar 虚拟端点写入，使 GG 也能及时跟随。连接异常时会自动重连并降级为低频 HTTP 轮询。

项目不创建虚拟声卡、不处理音频流，也不修改直播推流配置。

## 运行发布版

发布包为 Windows x64 自包含便携版本，无需另行安装 .NET。启动前请先安装 SteelSeries GG 并启用 Sonar，解压后运行 `SteelSeriesAssist.exe`。

## 构建与运行

```powershell
dotnet build SteelSeriesAssist.sln
dotnet run --project src/SteelSeriesAssist.Probe
dotnet run --project tests/SteelSeriesAssist.Tests
dotnet run --project src/SteelSeriesAssist.App
```

创建 Windows x64 发布包：

```powershell
dotnet publish src/SteelSeriesAssist.App -p:PublishProfile=win-x64
```

运行探针前需要启动 SteelSeries GG 和 Sonar 后台。

如需执行不会改变现有配置的写接口回环验证，可运行：

```powershell
dotnet run --project src/SteelSeriesAssist.Probe -- --verify-current-writes
```

开发时可运行完整面板冒烟测试；它会打开面板、加载真实 Sonar 数据，并在 5 秒后自动退出：

```powershell
dotnet run --project src/SteelSeriesAssist.App -- --smoke-test
```
