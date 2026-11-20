# 面板创建与定位

本次调整修复了一个导致程序整体退出的缺陷，不改变面板外观、尺寸和音频功能。

## 1. 问题

点击托盘图标时，部分机器会弹出未处理异常对话框并结束进程：

```
System.DllNotFoundException: Dll was not found.
   at MS.Win32.Penimc.UnsafeNativeMethods.CreateResetEvent(IntPtr& handle)
   at System.Windows.Input.PenThreadWorker..ctor()
   at System.Windows.Input.StylusWisp.WispTabletDeviceCollection.UpdateTablets()
   at System.Windows.Input.StylusWisp.WispLogic.RegisterHwndForInput(InputManager, PresentationSource)
   at System.Windows.Interop.HwndSource.Initialize(HwndSourceParameters)
   at System.Windows.Interop.WindowInteropHelper.EnsureHandle()
   at SteelSeriesAssist.App.PanelPlacement.Place(Window, Boolean)
   at SteelSeriesAssist.App.App.ShowPanel()
```

原实现中 `ShowPanel()` 先调用 `PanelPlacement.Place()`，而 `Place()` 的第一行是
`new WindowInteropHelper(window).EnsureHandle()`。这个 API 会在 `Show()` 之前强制创建
HWND，走的是非正常窗口启动路径；`HwndSource.Initialize` 随后会初始化 WPF 的笔/触控
输入子系统（WISP），该原生组件在部分系统版本上不可用或不兼容，异常一路未捕获，
终结整个进程。

正常的 `Window.Show()` 会创建同一个句柄，但处于窗口启动流程中，WPF 对输入子系统的
初始化是防御式的，因此不会走到这条故障路径。

## 2. 方案

定位与显示分离，并且**定位永远不创建句柄**：

- `IShellWindow`：面板定位需要的最小窗口能力（`Handle`、`IsVisible`、`ShowSafely`、
  `ActivateSafely`、`ApplyPanelBounds`）；
- `WpfShellWindow`：WPF 实现，`ApplyPanelBounds` 同时写入 DIP 尺寸（`Width`/`Height`）
  和像素位置（`SetWindowPos`，带 `SWP_NOZORDER | SWP_NOACTIVATE`）；
- `PanelGeometry`：纯函数，`(工作区像素矩形, DPI) → PanelBounds`，不依赖 WPF 或 Win32；
- `PanelPlacement`：枚举目标显示器并读取有效 DPI，交给 `PanelGeometry` 计算，
  `Handle` 为 `IntPtr.Zero` 时直接跳过；
- `PanelPresenter`：`Show()` = 先显示、后定位；`Reposition()` 只处理已可见的窗口，
  供 `DpiChanged` 使用。

`App.ShowPanel()` 用 try/catch 包住显示流程：失败时弹出托盘气泡说明原因，程序继续
驻留托盘；托盘图标双击与单击都切换面板，避免与系统双击手势冲突时面板不出现。

## 3. 验证

- `PanelGeometry` 覆盖 100%/150% DPI、右下角边距、小工作区和退化工作区；
- `PanelPresenter` 用可记录的假窗口验证「显示前不读取句柄」「只显示一次」「显示后
  才定位」「隐藏窗口不重定位」；
- `--smoke-test --smoke-result` 在无交互环境下输出 `panel visible = True/False`，用于
  发布前的机器验证。

## 4. 后续

`UseWpfStylusPointerStack` 与 .NET 版本升级不在本次范围内；若后续升级目标框架，
需重新验证笔/触控设备与高 DPI 多屏下的定位行为。
