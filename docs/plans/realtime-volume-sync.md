# SteelSeries Assist 实时音量同步实施计划

更新时间：2026-08-01  
目标版本：0.0.3（暂定）  
状态：已实施并验证

## 1. 目标

让 SteelSeries Assist、SteelSeries GG / Sonar 和 Windows 音量快捷键之间实现接近实时的双向音量与静音同步：

- 在 Assist 中拖动音量时，GG 界面能够跟随变化；
- 在 GG 中调整音量或静音时，Assist 界面能够自动更新；
- 使用键盘媒体键改变 Sonar 虚拟端点音量时，Assist 能够自动更新；
- Sonar 重启或端口改变后，Assist 能够恢复同步；
- 实时同步不得造成写入回环、界面抖动、滑块抢夺或请求洪泛。

## 2. 已确认的技术事实

当前验证环境：

- SteelSeries GG：116.0.0；
- SteelSeries Sonar：1.99.0；
- Sonar HTTP 服务：动态 loopback 端口；
- Sonar WebSocket：`ws://127.0.0.1:<动态端口>/sock`。

已确认行为：

- `GET /volumeSettings/classic` 可以读取完整 Classic 音量和静音状态；
- `PUT /volumeSettings/classic/{channel}/Volume/{value}` 可以设置通道音量，但外部 PUT 不会触发 GG 页面刷新所需的广播；
- `PUT /volumeSettings/classic/{channel}/Mute/{value}` 可以设置静音；
- Sonar 会通过 WebSocket 广播 `SONAR_EVENT_VOLUME_DATA`；
- WebSocket 音量事件可能只包含发生变化的通道，不能当作完整快照使用；
- 实测外部音量变化能够在几十毫秒内到达 WebSocket；
- 当前 Assist 只在松开滑块时发送音量，所以 GG 无法在拖动过程中跟随；
- 当前 2 秒轮询会读取并重建全部音量、设备和路由 UI，不适合作为实时音量同步主路径。

## 3. 目标架构

```text
首次打开 / 重连
    │
    ├─ HTTP GET 完整快照
    │      └─ 建立本地通道状态
    │
    └─ WebSocket /sock
           └─ 接收增量音量事件
                    └─ 合并到现有 ChannelRow

Assist 滑块拖动
    │
    ├─ 更新本地预览
    ├─ 50–100ms 节流写入 Sonar Windows 虚拟端点
    ├─ Sonar 广播事件给 GG 和 Assist
    └─ 松开时发送最终值并 HTTP 回读确认
```

HTTP 负责初始化、Master 写入、最终确认和故障兜底；Windows 虚拟端点负责需要同步到 GG 的通道写入；WebSocket 负责实时增量同步。

## 4. 实施步骤

### 阶段一：拆分状态读取

1. 在 `ISonarClient` 和 `SonarClient` 中增加只读取 Classic 音量的方法，避免音量刷新同时请求设备、绑定和应用路由；
2. 保留现有完整 `GetSnapshotAsync`，用于首次加载、设备页刷新和重连；
3. 将 `MainWindow` 中的通道行保存为按 channel 索引的稳定集合；
4. 后续更新已有 `ChannelRow`，不再因为音量变化替换整个 `ChannelList.ItemsSource`。

验收点：HTTP 音量刷新不会重建下拉框、路由卡片或滑块对象。

### 阶段二：接入 Sonar WebSocket

1. 新建 `ISonarEventClient` 和 `SonarEventClient`；
2. 根据发现到的 HTTP 地址生成同端口的 `ws://.../sock` 地址；
3. 使用 `ClientWebSocket` 建立连接并持续接收消息；
4. 解析事件信封：
   - `event`；
   - `data.masters.classic`；
   - `data.devices.<channel>.classic`；
5. 只处理 `SONAR_EVENT_VOLUME_DATA`，未知事件安全忽略；
6. 将部分事件合并进当前状态，不清空事件中未出现的通道；
7. 通过 WPF Dispatcher 将变化应用到 UI 线程；
8. 在窗口关闭和应用退出时正确取消接收循环并释放连接。

验收点：在 GG 或键盘中调节音量后，Assist 在 250ms 内更新对应通道。

### 阶段三：滑块实时写入

1. 将当前只监听 `PreviewMouseLeftButtonUp` 的逻辑拆分为：
   - 拖动开始；
   - 拖动中；
   - 拖动结束；
2. 拖动中立即更新本地百分比文本；
3. 为每个通道建立独立的节流/防抖写入状态；
4. 拖动期间最多每 75ms 发送一次最新值；
5. 如果前一次请求仍在进行，只保留最新待发送值，不并发堆积请求；
6. 松开滑块时立即发送最终值，不等待下一次节流周期；
7. 最终写入后执行通道级 HTTP 回读确认；
8. 写入失败时恢复服务端确认值，并在底部状态栏显示错误。

验收点：连续拖动 3 秒时 GG 能够流畅跟随，最终数值与 Assist 一致，且不存在大量积压请求。

### 阶段四：防回环与交互保护

1. 区分三类状态来源：
   - 用户在 Assist 中拖动；
   - WebSocket 远程更新；
   - HTTP 初始化或回读确认；
2. WebSocket 更新 UI 时不得触发新的 PUT；
3. 用户正在拖动某个通道时：
   - 本地拖动值具有临时显示优先级；
   - 对同一通道收到的事件用于确认，不抢夺鼠标控制；
   - 其他通道仍可正常实时更新；
4. 松开后以最终 PUT 和服务端事件/回读结果为准；
5. 使用容差比较避免浮点数微小误差造成 79%/80% 往返抖动。

验收点：Assist 不会因接收到自身写入产生重复 PUT，拖动手柄不会跳动或被抢回。

### 阶段五：重连与降级

1. WebSocket 意外断开后使用带上限的退避策略重连，例如 1s、2s、5s、10s；
2. 重连前重新验证 Sonar 端口；
3. Sonar 进程重启、端口变化时重新执行发现流程；
4. WebSocket 重连成功后立即通过 HTTP 读取一次完整音量状态，弥补断线期间遗漏的事件；
5. WebSocket 不可用时启用低频音量专用 HTTP 轮询，例如每 2 秒一次；
6. 设备、绑定和应用路由继续使用较低频率或按需完整刷新；
7. 连接状态变化只更新状态栏，不清空当前可用 UI。

验收点：关闭再启动 Sonar 后，无需重启 Assist 即可恢复实时同步。

### 阶段六：调整现有刷新机制

1. 移除实时场景中的 2 秒全量 `GetSnapshotAsync`；
2. 将刷新分为：
   - 音量：WebSocket 主动推送；
   - 设备与绑定：打开页面、切换设备后或低频刷新；
   - 应用路由：打开路由页、拖放后或独立低频刷新；
3. 保留设备下拉框打开、应用拖放和写入期间的交互保护；
4. 避免任何后台刷新替换正在交互的控件实例。

验收点：打开设备下拉框或拖动应用时，不会因后台同步导致控件消失或操作中断。

## 5. 数据模型建议

新增或调整以下模型：

- `SonarEventEnvelope`：事件名称和原始/类型化 data；
- `VolumeEventData`：可选的 master 和 device 字典；
- `PartialVolumeState`：可空的音量与静音字段；
- `ChannelVolumeUpdate`：标准化后的 channel、volume、muted；
- `VolumeWriteCoordinator`：每通道节流、最终提交和取消控制；
- `SonarConnectionCoordinator`：HTTP 客户端、事件客户端、重连和端口迁移。

事件合并必须遵守：缺失字段表示“未更新”，不表示 0、false 或删除。

## 6. 测试计划

### 自动测试

1. 正确解析完整 `SONAR_EVENT_VOLUME_DATA`；
2. 正确解析只包含单一设备的部分事件；
3. 未出现的通道状态保持不变；
4. 未知事件不会造成异常或状态清空；
5. 无效 JSON 只影响当前消息，接收循环继续运行；
6. 节流期间多个值只发送必要的中间值和最终值；
7. 最终值不会被较早请求的响应覆盖；
8. WebSocket 更新不会触发写入回环；
9. 断线重连后执行 HTTP 补偿读取；
10. 取消和退出不会遗留后台任务或抛出未观察异常。

### 手动集成测试

| 场景 | 预期结果 |
|---|---|
| 在 Assist 连续拖动 Game 音量 | GG 在拖动过程中跟随，松开后数值一致 |
| 在 GG 连续拖动 Media 音量 | Assist 在 250ms 内跟随 |
| 按键盘音量加减键 | 对应 Sonar 通道在 Assist 和 GG 中同步 |
| 在 GG 切换静音 | Assist 图标及时变为红色斜线状态 |
| 在 Assist 点击静音 | GG 及时显示静音状态 |
| 同时观察其他通道 | 未操作通道不跳动、不归零 |
| 拖动时 Sonar 重启 | 显示错误、停止写入，重连后恢复并同步最终服务端状态 |
| WebSocket 被阻断 | 自动降级为 HTTP 音量轮询 |
| 设备下拉框保持展开 | 音量事件不会关闭下拉框 |
| OBS 多路由存在 | 音量同步不影响应用路由显示 |

### 性能检查

- 滑块拖动期间每通道 PUT 不超过约 14 次/秒；
- WebSocket 空闲时无轮询 CPU 占用；
- 不因每条音量消息重建整个设备或路由列表；
- 连续运行 30 分钟无连接、任务或内存持续增长。

## 7. 验收标准

必须同时满足：

1. Assist、GG、键盘媒体键三方音量可以双向同步；
2. 外部变化到 Assist 的正常延迟不超过 250ms；
3. Assist 拖动到 GG 的正常延迟不超过 250ms；
4. 滑块松开后双方最终百分比一致，允许显示取整误差不超过 1%；
5. 不出现请求回环、滑块抢夺、闪烁或下拉框被刷新关闭；
6. Sonar 重启后能够自动恢复；
7. WebSocket 不可用时仍可通过 HTTP 降级同步；
8. Release 构建 0 警告、0 错误，自动测试和 GUI 冒烟测试全部通过。

## 8. 风险与边界

- `/sock` 是 GG 116 / Sonar 1.99 中确认存在的隐藏接口，未来版本可能改变路径或事件结构；
- 实现时必须进行能力探测，不能因为 WebSocket 不可用导致主界面无法使用；
- GG 版本变化后应保留 HTTP 降级路径；
- Windows 媒体键具体影响哪个通道取决于当前默认音频端点；
- 当前目标只包含 Classic 模式，不扩展 Stream 模式和直播推流配置；
- 本阶段只优化音量和静音同步，不改变设备绑定与应用路由 API。

## 9. 预计交付顺序

1. WebSocket 事件客户端与解析测试；
2. 稳定的通道状态集合和增量 UI 更新；
3. 滑块节流写入协调器；
4. 双向同步与防回环；
5. 重连、HTTP 降级和 Sonar 端口迁移；
6. 集成测试、文档更新和 0.0.3 候选版本。

## 10. 实施结果

已于 2026-08-01 完成：

- 接入 `/sock` WebSocket，并解析部分 `SONAR_EVENT_VOLUME_DATA`；
- 通道行改为稳定对象，远程音量与静音变化原位更新；
- 增加每通道 75ms 节流、仅保留最新值及松开最终提交；
- 拖动期间保护本地手柄，服务端事件不会造成写入回环；
- WebSocket 自动退避重连，断线时启用音量专用 HTTP 轮询；
- 全量设备与路由刷新降至 5 秒，并且不再重建已有音量控件；
- 自动测试由 4 项扩展至 8 项，覆盖事件地址、部分事件解析和写入合并；
- 已通过 Assist 自动拖动 Game 音量、Sonar HTTP 实时变化及原值恢复验证。
- 后续实机反馈确认外部 HTTP PUT 不会让已打开的 GG 页面刷新；现已改为通过 Windows Sonar 虚拟端点写入 Game、Chat、Media、Aux、Mic，并保留 Master HTTP 写入。

目标版本仍暂定为 0.0.3；本次实施不主动修改程序集版本或生成发布包。
