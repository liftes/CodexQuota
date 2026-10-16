# 安装、升级与故障排查

## 便携运行

1. 从仓库 **Releases** 下载 `CodexQuota-v版本号-win-x64.zip`。GitHub 自动生成的 Source code ZIP 不包含编译成品。
2. 解压到长期保留的位置，双击 `CodexQuota.exe`，不要直接在压缩包内运行。
3. 在官方 Codex CLI 中完成 ChatGPT 登录；找不到 CLI 时，从软件设置中选择官方 `codex.exe`。
4. 调整外观和 X/Y；需要时勾选开机自启，默认不启用。

建议 EXE 和 `.exe.config` 始终放在一起。当前是 x64 版本，不是 ARM64 原生构建。

## 安装到当前用户目录

在解压目录打开 PowerShell：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1 -Launch
```

默认安装到 `%LOCALAPPDATA%\Programs\CodexQuota`，无需管理员权限；也可指定其他目录：

```powershell
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\CodexQuota'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1 -Destination $installDir -Launch
```

脚本只安装运行文件，不复制开发工程、不自动开启自启。从源码目录执行时需先编译。

## 升级

便携版可从托盘退出，解压新版到原目录替换运行文件，再启动。也可从新版包运行 `Install.ps1`，将 `-Destination` 指向原来的运行目录；脚本备份旧文件，仅停止该目标路径的 CodexQuota。

设置保存在 `%LOCALAPPDATA%\CodexQuota\settings.json`，升级脚本不改写它。自启要求 EXE 路径不变；移动程序时先关闭旧自启，再在新位置开启一次。

### 早期完整工程安装

早期版本把源码和 `Release` 放在同一安装目录。新版保留整体工程脚本，不再预设个人目录。从新版源码根目录运行，并将示例目标替换为自己的原工程目录：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\UpdateInstalledVersion.ps1 -Destination '..\ExistingCodexQuota' -Launch
```

首次复制整个工程使用 `CopyToRequestedFolder.ps1 -Destination '..\MyCodexQuota' -Launch`，它拒绝覆盖已有同名项目内容。更新脚本备份到目标 `_Backups`。两者需要已构建的 `Release`，不适用于便携 ZIP。

PowerShell 中请使用实际路径或相对路径。`%~dp0` 只在 CMD 批处理文件中展开，不能直接粘贴到 PowerShell 命令里。

## 卸载

先在软件中关闭开机自启，再从托盘退出，删除程序目录。没有后台服务或驱动。不保留偏好时可另行删除 `%LOCALAPPDATA%\CodexQuota`，它与程序目录不同。

## 常见问题

| 现象 | 处理 |
| --- | --- |
| 找不到 Codex / 读取失败 | 检查官方 CLI 能否运行、登录账户和网络；在设置中选择 `codex.exe` |
| 显示问号 | 接口缺少该额度、尚未读取成功或窗口类型未知；不代表零额度 |
| 数据停留不变 | 检查过期标记和下次查询时间；UI 倒计时不等同于每秒查询额度 |
| 开始按钮旁空间不足 | 调整 X/Y、尺寸和间距，或切换独立悬浮/仅托盘；软件不为系统图标预留空间 |
| 偏移被限制 | 检查可见圆环是否触及屏幕边界，对照设置中的请求偏移和实际坐标 |
| 移动后自启失败 | 在新位置重新开关自启，核对 Windows 启动应用是否另行禁用 |
| 似乎没有启动 | 查看托盘折叠菜单；程序是单实例，已有实例时不再创建一份 |

反馈时提供 Windows 版本、DPI、任务栏位置、软件版本和复现步骤。截图先遮盖账户信息，不上传认证文件或完整个人配置。
