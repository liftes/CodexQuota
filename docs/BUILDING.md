# 编译与打包

## 前置条件

- Windows x64；主要面向 Windows 11。
- Windows PowerShell 5.1、.NET Framework 4.8 或兼容的更新版本，以及 WPF 组件。
- `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` 和系统 GAC 的 WPF/UI Automation 程序集。这是系统通用位置，不包含开发者个人目录。
- 运行软件需要安装、登录官方 Codex CLI，编译和自动测试不需要。

不需要额外安装 Visual Studio、Node.js、.NET SDK 或还原 NuGet 包。`CodexQuota.csproj` 可用于 IDE 浏览及具备 .NET Framework 开发工具的环境；`Build.ps1` 是本文与 CI 的编译入口。代码需兼容 C# 5。

## 编译和测试

在仓库根目录，即 `Build.ps1` 所在目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -RunTests
```

输出 `Release\CodexQuota.exe` 和 `Release\CodexQuota.exe.config`。去掉 `-RunTests` 可仅编译。失败返回非零退出码。`ExecutionPolicy Bypass` 只作用于此次进程，不修改系统永久策略。

```powershell
.\Release\CodexQuota.exe
```

找不到编译器或程序集时，检查 Windows 的 .NET Framework/WPF 安装，不要从不明网站单独下载 DLL。[Microsoft 安装说明](https://learn.microsoft.com/en-us/dotnet/framework/install/on-windows-and-server)。

## 生成发行文件

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Package.ps1 -RunTests
```

已完成当前源码编译和测试时，可用 `-SkipBuild` 避免重复。默认输出到 `dist`，版本从 EXE 读取：

| 文件 | 用途 |
| --- | --- |
| `CodexQuota-v0.7.0-win-x64.zip` | 推荐用户下载的便携发行包 |
| `CodexQuota.exe` | 可直接下载的应用程序 |
| `CodexQuota.exe.config` | 运行配置，建议与 EXE 放在同一目录 |
| `SHA256SUMS.txt` | 下载文件的 SHA256 校验清单 |

打包采用明确文件清单，不收集用户配置、日志、诊断报告、旧备份或整个工作目录。ZIP 附运行说明与可选安装脚本，已有 `LICENSE` 时一并包含。仓库只跟踪源码和文档，生成的二进制放入 Releases，不提交 `Release` 或 `dist`。

首次上传与自动发行见 [RELEASING.md](RELEASING.md)。
