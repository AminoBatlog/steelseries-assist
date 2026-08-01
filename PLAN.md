# SteelSeries Sonar 托盘助手开发计划

> 文档状态：初版可执行计划  
> 调研环境：Windows，SteelSeries GG 116.0.0  
> 调研日期：2026-08-01

## 1. 结论

项目可以实现，产品定位为 **SteelSeries GG / Sonar 的轻量级托盘控制面板**。它只负责快速读取和修改 GG 已有的 Sonar 设置，不创建虚拟声卡、不实现音频引擎，也不替代 GG。

程序常驻 Windows system tray，点击图标后在任务栏通知区域附近弹出一个轻量面板，用于：

- 调整 Game、Chat、Media、Aux、Mic、Master 的音量和静音状态；
- 为 Sonar 各通道切换物理输出/输入设备；
- 将当前正在发声的软件切换到指定 Sonar 虚拟通道；
- 保存和一键应用常用场景，例如“耳机游戏”“音箱影音”“会议”；
- 在 GG 主界面未打开时快速完成以上操作。

SteelSeries 的虚拟设备、DSP、混音和音频转发全部由 Sonar 后台负责。本程序只是 GG 设置的快捷入口：不安装驱动、不创建或删除声卡、不搬运音频数据，也不直接处理实时音频。

### 必须接受的边界

1. **GG/Sonar 后台仍须运行。** 本程序可以避免打开缓慢的 GG 完整界面，但不能取代 `SteelSeriesSonar.exe`、GG Core 或 Sonar 已创建的虚拟设备。
2. **Sonar 控制接口不是公开、稳定的第三方 SDK。** GameSense SDK 是设备灯效/屏幕事件接口，不是 Sonar 混音控制接口。GG 更新可能改变本地 API，项目必须有版本检测和兼容层。
3. **应用路由不是对所有软件都可靠。** 某些软件缓存音频端点，需要重启后生效；部分软件自行管理设备，不能被外部程序改路由；Discord 等程序还可能创建多个行为不同的音频会话。
4. **直播和推流配置完全不在本项目范围内。** 相关设置继续由 GG 和用户现有的推流软件负责，本程序不读取或修改直播混音设置，也不联动 OBS。

## 2. 已完成的可行性验证

本机只读探测已经确认：

- GG Core 地址可从 `C:\ProgramData\SteelSeries\GG\coreProps.json` 动态读取，不能硬编码端口；
- GG Core 的 `/subApps` 数据可用于发现 Sonar 当前本地服务地址；
- 当前 Sonar 独立进程监听 `127.0.0.1:17621`，即便不显示 GG 主窗口仍可工作；
- `GET /mode` 可用于判断 GG 当前工作模式；助手只据此找到正确的个人播放/监听音量，不改变模式或直播配置；
- `GET /volumeSettings/classic` 可读取日常使用的各通道音量和静音；
- `GET /audioDevices` 可列出物理设备、虚拟设备、输入/输出方向和 Sonar 角色；
- `GET /classicRedirections` 可读取 Game、Chat、Media、Aux、Mic 当前绑定的物理设备；
- `GET /AudioDeviceRouting` 可读取音频会话、进程、当前端点及路由状态；
- `GET /deviceOut` 可读取部分 SteelSeries 硬件输出状态；
- 本机安装包前端包含音量、重定向、应用路由、设备 fallback、link-all 和 WebSocket 事件相关调用，说明目标功能已经存在于 Sonar 本地服务层。

上述验证只证明当前 GG 116.0.0 的能力，不代表 SteelSeries 对第三方兼容性的承诺。

## 3. 推荐技术方案

### 技术栈

- **语言/运行时：** C# + 当前稳定版 .NET（开工时确认，最低目标 .NET 8）
- **桌面 UI：** WPF
- **托盘：** `NotifyIcon`（Windows Forms interop 或成熟的 WPF tray 组件）
- **音频枚举/兜底：** Windows Core Audio COM API；可使用 NAudio 封装只读枚举和端点音量能力
- **本地存储：** JSON，保存窗口设置、收藏设备、快捷场景和兼容数据
- **日志：** 结构化滚动文本日志，默认去除设备 GUID、进程完整路径等敏感信息
- **发布：** x64、自包含、单文件或精简目录包；提供开机启动选项

选择 WPF 的原因是本项目仅面向 Windows，需要极快启动、较低内存占用、可靠托盘行为和成熟的窗口定位能力。Electron/Tauri 的前端生态优势在这个小型控制面板中不如 Windows 原生集成重要。

### 逻辑架构

```text
Tray / Popup UI
       |
Application Service（场景、状态合并、命令节流、错误恢复）
       |
ISonarClient（稳定的项目内部接口）
       |
Sonar API Adapter v116 / Future Adapters
       |
GG Core discovery -> Sonar localhost HTTP/WebSocket

Windows Core Audio Adapter
       |
端点变更通知、设备友好名称、必要时的应用路由兜底
```

UI 不得直接拼接 Sonar URL。所有未公开接口都封装在 `ISonarClient` 后面，以便 GG 更新后只修改适配器。

### 建议的解决方案结构

```text
src/
  SteelSeriesAssist.App/             # WPF、托盘、弹窗、设置页
  SteelSeriesAssist.Application/     # 用例、状态、场景和命令调度
  SteelSeriesAssist.Sonar/           # 地址发现、本地 API、版本适配
  SteelSeriesAssist.WindowsAudio/    # Core Audio 枚举、通知、路由兜底
  SteelSeriesAssist.Domain/          # Channel、Device、Session、Scene 等模型
tests/
  SteelSeriesAssist.UnitTests/
  SteelSeriesAssist.IntegrationTests/
docs/
  api-observations/                   # 每个已验证 GG 版本的脱敏接口样本
```

## 4. 产品范围

### MVP（第一版必须有）

- 托盘常驻、单实例运行、左键打开/关闭弹窗；
- 弹窗失焦自动收起，可在设置中切换为固定显示；
- 显示 Sonar 在线、未启动、不兼容三种状态；
- 各通道音量、静音和 Master 控制；若 GG 已启用其他模式，仅控制用户实际听到的个人播放/监听音量，不显示或修改直播音量；
- 物理输出/输入设备列表及通道设备切换；
- 当前音频应用列表及切换到 Game/Chat/Media/Aux；
- 设备插拔、GG/Sonar 重启后自动重连；
- 本地设置、开机启动、诊断日志；
- 所有写操作均有超时、失败提示和状态回读确认。

### 第二阶段

- 自定义场景和一键切换；
- 全局快捷键；
- 鼠标滚轮调节托盘或当前选中通道；
- 收藏/隐藏设备和应用；
- 音量电平表；
- 设备优先级、fallback 与“所有通道使用同一设备”；
- 快速打开完整 GG Sonar 页面作为兜底。

### 暂不纳入

- 自研或安装虚拟声卡驱动；
- 创建、删除或重新配置 Sonar 虚拟声卡；
- 复制 Sonar 的 EQ、降噪、压缩器等全部 DSP 配置页面；
- 在助手进程内捕获、混合或转发实时音频；
- 直播混音、推流配置或 OBS 联动；
- macOS/Linux 支持。

## 5. 交互设计草案

弹窗建议宽 420–520 px、高度按内容自适应，默认分为三个标签：

1. **音量**：每行一个 Sonar 通道，包含名称、当前物理设备、音量滑块、百分比和静音按钮。
2. **应用**：显示活跃应用及当前通道，点击目标通道即可切换；无法路由时给出原因或“重启应用后生效”提示。
3. **场景**：显示收藏场景和当前状态，支持一键应用。

托盘右键菜单至少包含：

- 打开控制面板；
- Master 静音；
- 常用场景；
- 打开 SteelSeries GG；
- 设置；
- 退出。

交互要求：

- 打开弹窗后 150 ms 内出现骨架或缓存状态；
- 正常情况下 500 ms 内显示后台最新状态；
- 拖动音量时本地即时反馈，以 30–50 ms 节流发送，松开后回读确认；
- 设备和应用以友好名称展示，内部始终使用 endpoint/session ID；
- 所有切换都显示短暂 pending 状态，失败则回滚 UI；
- 不因一次 API 失败卡死 UI 线程。

## 6. 分阶段实施计划

### Phase 0：接口验证性原型（2–4 天）

这是整个项目的 Go/No-Go 门槛，应先于正式 UI。

- [ ] 创建一个只在 localhost 工作的控制台探针；
- [ ] 从 `coreProps.json` 读取 GG 地址，通过 `/subApps` 动态发现 Sonar 地址；
- [ ] 建立自签名证书的安全处理方案：仅允许 loopback、校验证书指纹或只对已知 GG 本地证书放行，禁止全局忽略 TLS；
- [ ] 记录 GG 和 Sonar 版本、能力列表，不硬编码当前 `17621`；
- [ ] 验证读取/设置 classic 音量与静音；
- [ ] 验证 GG 处于 streamer 模式时仍能只调整个人 monitoring 音量，且不会写入 streaming/推流分支；
- [ ] 验证每个 Sonar 通道切换物理设备；
- [ ] 验证活跃应用在 Game/Chat/Media/Aux 之间移动；
- [ ] 验证 Sonar 重启、端口变化和设备插拔后的恢复；
- [ ] 保存脱敏的请求/响应契约作为 integration fixture；
- [ ] 确认 WebSocket 事件格式；若不稳定，确定 500–1000 ms 自适应轮询方案。

**退出条件：** 音量、设备绑定/切换、应用路由三类写操作均至少成功验证一次，并且不需要打开 GG 主窗口。任何一类失败，都要在 MVP 范围中明确降级，而不是边做 UI 边猜接口。

### Phase 1：工程骨架和托盘体验（2–3 天）

- [ ] 创建解决方案、项目分层和依赖注入；
- [ ] 实现单实例与第二次启动唤起已有弹窗；
- [ ] 实现托盘图标、菜单、退出和开机启动；
- [ ] 实现任务栏附近弹窗定位、多显示器和 DPI 适配；
- [ ] 实现失焦隐藏、固定显示和键盘 Esc 关闭；
- [ ] 建立设置、日志、异常边界和崩溃恢复；
- [ ] 添加基础 CI：build、test、format。

**退出条件：** 冷启动可接受，托盘与弹窗在 100%、125%、150% DPI 以及多显示器下行为正确。

### Phase 2：Sonar 客户端和状态同步（3–5 天）

- [ ] 定义 `ISonarClient` 与领域模型；
- [ ] 实现 GG/Sonar 地址发现、健康检查、能力协商；
- [ ] 实现读取音量、模式、设备、重定向和音频会话；
- [ ] 实现 WebSocket/轮询状态源；
- [ ] 实现断线指数退避、进程重启重连和请求取消；
- [ ] 实现命令节流、去重、超时、回读确认；
- [ ] 建立 v116 适配器及未知版本的只读安全模式；
- [ ] 为 JSON 字段缺失、增加和类型异常编写契约测试。

**退出条件：** 连续运行 8 小时无持续轮询泄漏；重启 GG 后无需重启助手即可恢复。

### Phase 3：MVP 音量控制与设备切换（3–5 天）

- [ ] 完成各 Sonar 通道的音量控制 UI；
- [ ] 完成各通道和 Master 音量/静音；
- [ ] 完成物理输出/输入设备选择；
- [ ] 处理设备同名、离线、被排除和默认设备变化；
- [ ] 添加键盘操作、焦点顺序和基本无障碍标签；
- [ ] 添加缓存状态与加载/错误/不兼容状态页面。

**退出条件：** 日常音量与设备切换无需打开 GG；操作结果与 GG 页面重新打开后的状态一致。

### Phase 4：应用路由（3–5 天）

- [ ] 展示活跃/非活跃音频会话并按进程聚合；
- [ ] 区分 render/capture，会话只能移动到合法目标；
- [ ] 完成应用到 Sonar 虚拟通道的路由写操作；
- [ ] 对多会话程序提供“仅此会话/此应用全部会话”语义；
- [ ] 检测需重启应用、路由失败或应用自行管理设备的情况；
- [ ] 必要时评估 Windows 持久化应用端点策略作为兜底，但不得依赖未验证的全局注册表修改；
- [ ] 针对浏览器、游戏、Discord、Spotify/媒体播放器做兼容测试。

**退出条件：** 支持的应用可以在四个输出通道之间移动；不支持的应用得到明确提示，不出现“UI 显示成功但实际未切换”的静默失败。

### Phase 5：场景、打磨和发布（3–5 天）

- [ ] 定义场景 schema：设备绑定、音量和静音；
- [ ] 场景应用采用有序事务，并在部分失败时展示明细；
- [ ] 加入全局快捷键和托盘滚轮控制；
- [ ] 完成首次启动引导和功能边界说明；
- [ ] 对日志和诊断包做隐私清理；
- [ ] 测试升级安装、卸载、开机启动和 Windows 睡眠恢复；
- [ ] 生成签名安装包；若暂未购买代码签名证书，明确 SmartScreen 提示；
- [ ] 发布前建立 GG 版本兼容矩阵。

## 7. 测试计划

### 自动化测试

- 地址发现与端口变化；
- 所有 API DTO 的兼容反序列化；
- 音量范围、浮点精度、节流与最后写入胜出；
- 断线、超时、404、500、畸形 JSON；
- 未知 GG 版本进入安全模式；
- 场景应用顺序和部分失败；
- 日志脱敏；
- UI ViewModel 状态转换。

### 手动兼容矩阵

- Windows 11 当前稳定版本，后续视需求补 Windows 10；
- GG 自动启动、手动启动、运行中升级、崩溃重启；
- Sonar 各虚拟通道启用、禁用及后台重启；
- GG classic/streamer 两种状态下均只验证个人播放/监听音量，确认不修改直播分支；
- USB 耳机、3.5 mm、HDMI/DisplayPort、蓝牙、USB 麦克风；
- 设备插拔、改名、禁用、默认设备切换；
- 游戏独占模式与普通共享模式；
- 100%–200% DPI、多显示器、任务栏四个方向和自动隐藏；
- 非管理员账户运行。

### 性能目标

- 托盘空闲内存目标低于 100 MB；
- 空闲 CPU 接近 0%，无事件时平均低于 0.5%；
- 弹窗可见时间目标低于 150 ms；
- 控制操作感知反馈低于 100 ms；
- 不在 UI 线程进行网络、文件或 COM 阻塞调用。

## 8. 风险登记

| 风险 | 概率 | 影响 | 应对 |
|---|---:|---:|---|
| GG 更新导致内部 API 变更 | 高 | 高 | 版本适配器、契约 fixture、能力探测、未知版本只读模式、兼容矩阵 |
| Sonar 后台未运行或端口变化 | 中 | 高 | 动态发现、健康检查、自动重连、清晰状态提示 |
| 应用拒绝外部路由 | 中 | 中 | 回读确认、已知问题提示、引导用户在应用内切换、必要时重启应用 |
| TLS/本地证书处理不当 | 中 | 高 | 限定 loopback、精确验证，不全局关闭证书校验 |
| 设备 ID 在重装驱动后变化 | 中 | 中 | ID 优先，场景中同时保存友好名称和硬件属性用于重新匹配 |
| 多个音频会话同属一个应用 | 高 | 中 | 显示聚合状态，允许全部/单会话操作，逐项回读 |
| 音量拖动造成请求风暴 | 高 | 中 | UI 乐观更新、节流、取消旧请求、松开后最终提交 |
| 未签名程序被 SmartScreen 拦截 | 中 | 中 | 正式发布购买代码签名证书，测试版提供校验值和说明 |
| 修改内部接口可能违反使用条款 | 低/未知 | 高 | 发布前审阅 GG EULA；不绕过授权、不分发 SteelSeries 文件、不开放远程访问 |

## 9. 安全与隐私要求

- Sonar API 客户端只能连接 loopback 地址，拒绝任意远程 Host；
- 不对局域网暴露控制端口；
- 不以管理员权限常驻，除非某个经过验证的安装步骤确实需要；
- 不保存或上传用户音频；
- 日志不记录完整进程路径、用户名、设备 GUID、请求中的敏感字段；
- 不直接编辑 Sonar SQLite 数据库，不注入 GG 进程，不替换 GG 文件；
- 不把“忽略所有证书错误”作为实现方案；
- 自动更新必须校验签名和哈希。

## 10. 里程碑与工作量估算

单人开发、熟悉 C#/WPF 的前提下：

- 验证性原型：2–4 个工作日；
- 可日用 MVP：约 2–4 周；
- 含应用路由、场景、安装包和充分兼容测试的 v1.0：约 4–7 周。

最大变量不是 UI，而是未公开 Sonar API 在不同 GG 版本中的行为。必须先完成 Phase 0，再承诺 v1.0 日期。

## 11. MVP 验收标准

- [ ] 用户登录 Windows 后助手能自动进入托盘；
- [ ] 点击托盘图标可快速打开面板，不弹出 GG 主界面；
- [ ] 可以调整所有启用的 Sonar 通道音量和静音；
- [ ] 可以切换每个通道使用的物理设备；
- [ ] 可以将受支持的活跃应用切换到指定 Sonar 虚拟通道；
- [ ] 每次写操作均经过回读验证，失败不会伪装成成功；
- [ ] GG/Sonar 重启和音频设备插拔后能自动恢复；
- [ ] 未知 GG 版本不会执行可能破坏配置的盲目写入；
- [ ] 连续运行 24 小时无明显内存增长、CPU 异常或托盘失效。

## 12. 开工时的第一批任务

1. 初始化 .NET/WPF 解决方案和测试项目。
2. 实现 `GgDiscoveryService`，读取 `coreProps.json` 并查询 `/subApps`。
3. 实现只读 `SonarProbeClient`，输出模式、音量、设备、重定向和活跃会话的脱敏摘要。
4. 为本机 GG 116.0.0 保存第一套脱敏契约 fixture。
5. 逐项验证四类写接口，并将成功的请求形状固化为 integration test。
6. Phase 0 评审通过后，再开始托盘 UI。

## 13. 参考资料

- SteelSeries 官方：应用路由工作方式及已知限制  
  <https://support.steelseries.com/hc/en-us/articles/19318513397261-What-is-drag-drop-app-routing-and-how-does-it-work>
- SteelSeries 官方：Sound Device Manager、fallback 和快捷键  
  <https://support.steelseries.com/hc/en-us/articles/34430664112909-Sound-Device-Manager>
- Microsoft：Windows Core Audio APIs 概览  
  <https://learn.microsoft.com/en-us/windows/win32/coreaudio/about-the-windows-core-audio-apis>
- Microsoft：EndpointVolume API  
  <https://learn.microsoft.com/en-us/windows/win32/coreaudio/endpointvolume-api>
- 社区 Rust 客户端源码，仅用于交叉验证未公开 Sonar 本地接口，不视为官方规范  
  <https://docs.rs/steelseries-sonar/latest/src/steelseries_sonar/sonar.rs.html>
