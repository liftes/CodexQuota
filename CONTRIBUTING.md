# 贡献说明

先阅读[编译指南](docs/BUILDING.md)与[架构说明](docs/ARCHITECTURE.md)。保持 C# 5 / .NET Framework 4.8 / x64 兼容，避免不必要的运行依赖。

类和方法用 PascalCase，变量用 camelCase，局部临时变量沿用 `_` 后缀。定位与悬停复用共享几何和模板。UI 更新不能额外触发额度请求，未知和失败不能显示为满额。

提交行为变更前执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -RunTests
```

为核心行为变更增加有意义的检查。说明具体问题、最终行为、验证方法与未覆盖场景。纯文档变更无需重复全部 UI 测试。

不要提交真实配置、认证文件、原始 RPC 响应、探测报告、账户截图、安装备份和二进制。截图使用带示例标记的数据。忽略规则不能替代提交前的暂存差异检查。

问题报告提供 Windows 版本、DPI、任务栏位置、版本和复现步骤。安全敏感材料不要放公开 Issue；仓库启用 Private vulnerability reporting 时，请通过 Security 页的私密入口报告。
