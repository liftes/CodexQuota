# 架构说明

## 数据来源

`Core.cs` 启动本机官方 `codex.exe app-server`，通过标准输入/输出交换 JSONL RPC：`initialize` → `initialized` → `account/read`（`refreshToken: false`）→ `account/rateLimits/read`。参考[官方 App Server 文档](https://developers.openai.com/codex/app-server/)。

登录由官方客户端管理。程序不直接读取认证文件、不收集提示词、不发起 `turn/start` 或模型推理。CLI 子进程可能按自身行为使用网络和本地认证状态。配置可保存用户指定的 CLI 位置，这是运行时设置，不是源码中的个人路径。

只将 300 分钟窗口映射为五小时、10080 分钟映射为周。额度剩余为 `100 - usedPercent`；时间细环为 `(resetsAt - 当前时间) / 窗口总时长`，限制在 0–100%。时刻缺失即未知，到时不自动补满额度。

## 模块与调度

| 模块 | 职责 |
| --- | --- |
| `Core.cs` | RPC 生命周期、路由、解析与异常 |
| `Settings.cs` / `HoverAppearance.cs` | 配置迁移、规范化、CLI 查找 |
| `QuotaPollScheduler.cs` | 查询间隔、自适应、错误退避 |
| `Native.cs` / `ShellWatcher.cs` | 任务栏、开始按钮、DPI 定位与 Shell 事件 |
| `TaskbarHost.cs` | 原生透明子窗口、Alpha 帧与鼠标消息 |
| `RingControl.cs` / `RingLayout.cs` | 圆环与共享几何 |
| `QuotaHoverPresenter.cs` / `HoverDesignControls.cs` | 悬停卡片、锚定、字号与示例预览 |
| `UiTheme.cs` | 深浅资源与动态切换 |
| `StartupRegistration.cs` | 当前用户自启登记与测试存储 |
| `App.cs` | 应用、托盘、设置窗口集成 |

位置约每 100 ms 检查并合并事件，无变化时不移动窗口；本地每秒刷新倒计时。额度基础间隔默认 60 秒，自适应时无变化逐步放缓，活动或额度变化后恢复；移动圆环或改字号不触发额度请求。

附着模式把原生透明子窗口放入 `Shell_TrayWnd`，显示 WPF 绘制的预乘 Alpha 帧。不直接给透明 WPF Window 改父窗口，也不修改 Explorer 图标布局。附着条件不满足时回退独立悬浮，Explorer 重建后尝试恢复。

参考 [TrafficMonitor 任务栏实现](https://github.com/zhongyang219/TrafficMonitor/blob/930f17533d6098989ebad62f210aa97d75ef174b/TrafficMonitor/TaskBarDlg.cpp) 的附着思路，代码独立实现。接口参考：[SetParent](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent)、[UpdateLayeredWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow)。兼容性仍需不同 Windows 与第三方任务栏环境实机验收。

## 悬浮窗定位

圆环、任务栏和显示器边界均使用物理屏幕像素。水平任务栏的卡片中心对齐当前悬停圆环中心，按任务栏所在边向屏幕内侧弹出；靠近屏幕边缘时优先完整可见。预留间距按任务栏 DPI 计算，卡片 DIP 尺寸则按其自身 `CompositionTarget.TransformToDevice` 转换，不假定两者缩放一致。

WPF `ToolTip` 使用 `PlacementMode.Absolute`，将计算出的物理左上角放入零尺寸 `PlacementRectangle`，两个 Offset 保持为零。避免 `AbsolutePoint` 受 `SystemParameters.MenuDropAlignment` 影响，在部分设备把右上角对到预期左上角、产生整窗宽的水平偏移。弹窗打开后和实际尺寸变化后重新测量，宽度、字号及 DPI 改变不需要关闭重开。实现依据见 [WPF Popup 源码](https://source.dot.net/PresentationFramework/System/Windows/Controls/Primitives/Popup.cs.html) 的 `GetPlacementTargetInterestPoints`、`GetPointCombination`。

## 状态与隐私

偏好在 `%LOCALAPPDATA%\CodexQuota\settings.json`。自启只在用户操作时写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `CodexQuota` 值，登记当前 EXE 实际路径，不管理其他启动项。没有遥测与远程更新服务。

真实配置、探测报告、临时图片和安装备份不进入公开仓库；公共截图和测试使用明确标注的示例数据。
