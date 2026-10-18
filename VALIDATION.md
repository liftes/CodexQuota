# 验证范围

在 Windows x64、具备 .NET Framework 4.8/WPF 的环境执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -RunTests
```

v0.7.1 最近一次本地完整检查共 674 项通过：

| 测试组 | 检查数 | 主要内容 |
| --- | ---: | --- |
| CoreTests | 30 | 解析、模拟 JSONL 子进程、握手、乱序响应、超时 |
| AppearanceTests | 41 | 配置迁移、颜色、尺寸、间距、悬浮窗设计 |
| ResetRingTests | 29 | 服务端重置时间、缺失与过期数据 |
| PositionTests | 84 | X/Y、任务栏四边、屏幕边界、全屏分类 |
| TaskbarHostTests | 92 | 原生透明宿主、跨进程隐藏父窗口与坐标 |
| ApplicationTests | 100 | 实际 Release 程序控件、设置与显示集成 |
| ThemeTests | 65 | 深浅主题、动态资源、文字对比度 |
| HoverTests | 161 | 悬停内容、字号、预览、真实 HWND 坐标与菜单对齐 |
| StartupTests | 30 | 内存存储中的自启登记与路径引用 |
| SchedulerTests | 42 | 自适应查询、错误退避与调度 |

这些检查不要求真实账户、不写真实自启登记、不移动 Explorer 任务栏。隐藏测试窗口通过不代表所有交互桌面场景均已验证。GitHub Actions 应以对应提交的运行记录为准，本地通过不能代替远程 CI 成功。

悬浮窗定位回归会在测试进程内分别设置 WPF 的菜单对齐缓存为左对齐、右对齐，读取实际弹窗 HWND 的 `GetWindowRect`，检查圆环和弹窗中心的偏差不超过 1.5 个物理像素。不会修改 Windows 的菜单设置或注册表。任务栏缩放输入覆盖 100%、125%、150%、200%，并与当前运行环境的真实弹窗 DPI 分开处理；这些输入用于模拟不同坐标换算条件，不代表在四种物理屏幕配置上逐一实测。几何检查另覆盖四边任务栏、负坐标屏幕和小屏约束。混合 DPI 多屏之间的实际迁移仍需实机验收。

## 示例界面渲染

先创建 `artifacts` 目录，再运行：

```powershell
.\Release\CodexQuota.exe --render-hover-design .\artifacts\hover-light.png
.\Release\CodexQuota.exe --render-hover-design-dark .\artifacts\hover-dark.png
```

渲染使用带示例标记的数据，不读取真实额度。文档截图来自此类渲染，已检查深浅主题与预览排版。`--probe` 和 `--smoke-ui` 属于本地诊断，会尝试调用真实 CLI，不在公共 CI 中运行。诊断报告和用户截图不要未经检查提交。

## 发行前实机清单

- 与官方用量界面对照两个额度窗口、剩余比例、重置时刻与当前账户。
- 验证不同任务栏边缘、正负 X/Y、间距与 DPI 下的位置。
- 检查开始菜单、任务增减、自动隐藏、全屏、Explorer 重启后的恢复。
- 检查主题切换、悬浮窗字号即时预览、真实卡片与预览一致性。
- 实际启用/取消自启并在下次登录确认；移动程序后重新登记路径。
- 在干净目录解压发行 ZIP，启动、升级，确认配置保留和校验清单一致。

发行说明应记录已验证的 Windows 版本、任务栏环境、DPI 和尚未验证的项目。
