# 上传 GitHub 与发布版本

源码保存在 GitHub 仓库；可直接运行的程序放在 **Releases**。仓库的绿色 **Code → Download ZIP** 下载的是源码，不等于已经编译的发行版。

## 首次上传前

1. 确定仓库所属账户、名称及公开范围。下文使用 `OWNER/CodexQuota` 作为占位符，请替换为自己的仓库。
2. 由作者决定许可证，并在根目录添加对应的 `LICENSE`。未提供许可证时，不应宣称项目已经采用 MIT 或其他开源许可；公开可见本身不授予通用的复制、修改、再分发许可。
3. 使用干净的源码副本上传。它应包含 `src/`、`tests/` 中的 `.cs`、`assets/`、`docs/`、`.github/`、根目录的项目文件、脚本和 Markdown 文档。不要包含 `artifacts/`、`Release/`、`dist/`、`obj/`、`bin/`、`_Backups/`、生成的测试 `.exe`、实际 `settings.json`、登录文件或本机截图。
4. 检查说明文档、截图、提交作者信息是否适合公开。项目 `.gitignore` 已排除常见构建和本地文件，但忽略规则不能清除已经提交进 Git 的内容。

这里的命令都在源码根目录执行，不需要特定磁盘或用户名。

## 方式一：使用 Git 上传（推荐）

先在 GitHub 网站点击 **New repository**，填写仓库名并选择 Public 或 Private。已有本地源码时，不要额外勾选初始化 README、`.gitignore` 或许可证，以免产生两套初始提交。许可证可以先在本地补齐后一起上传。

安装 Git for Windows 后，在干净源码目录打开 PowerShell：

```powershell
git init -b main
git status --short --ignored
```

先检查状态列表，确认私人文件位于忽略范围，公开副本中没有不需要的内容，再暂存：

```powershell
git add .
git diff --cached --stat
git diff --cached
```

确认暂存内容后，提交并上传。把 URL 的 `OWNER` 和仓库名替换为实际值：

```powershell
git commit -m "Initial release of CodexQuota"
git remote add origin https://github.com/OWNER/CodexQuota.git
git push -u origin main
```

首次提交若提示缺少身份，请按 Git 的提示设置自己的提交姓名和邮箱。可以使用 GitHub 设置中提供的隐私邮箱。登录使用 Git 的交互认证，不要把访问令牌写进脚本、仓库 URL 或文档。

上传后，在仓库 **Actions → Build and test** 检查运行结果。工作流使用 `windows-2022` 执行 `Build.ps1 -RunTests`，通过后打包；结果作为 Actions artifact 保留 14 天。首次推送前，不能视为已经通过 GitHub runner 验证。

## 方式二：通过网页上传源码

不安装 Git 也可以先创建仓库，再通过 **Add file → Upload files** 上传干净源码目录中的文件及子目录，然后提交。上传的是展开的文件，不能只上传一个源码 ZIP，否则 GitHub 无法识别其中的 README 或工作流。

确认 `.github/workflows/build.yml` 与 `release.yml` 一并上传。Windows 文件管理器可能隐藏以点开头的项目；若网页上传遗漏 `.github`，可以使用 **Add file → Create new file** 按原路径逐个创建工作流。文件过多时建议改用 Git。

网页上传不会自动清理私人文件，所以仍然需要完成“首次上传前”的检查。二进制程序请上传到 Releases，不要反复提交到源码目录。

## 本地构建发行包

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -RunTests
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Package.ps1 -SkipBuild
```

发行资产输出到 `dist/`：

| 文件 | 用途 |
| --- | --- |
| `CodexQuota-v0.7.0-win-x64.zip` | 推荐下载；包含程序、配置及安装说明等运行所需文件 |
| `CodexQuota.exe` | 单独的可执行程序 |
| `CodexQuota.exe.config` | 与 EXE 放在同一目录 |
| `SHA256SUMS.txt` | 上述发行文件的 SHA-256 校验值 |

包名从程序版本生成；后续版本对应改变。打包脚本采用明确的文件清单，不把源码目录中的账户配置或调试产物一起压缩。当前程序没有代码签名；哈希用于核对下载文件完整性，不等于发布者的数字签名。

## 使用标签自动创建发行草稿

`.github/workflows/release.yml` 监听 `v*` 标签。它重新编译、运行测试、验证标签与实际 EXE 版本一致，再上传 ZIP、EXE、配置和校验文件到一个 **Draft release**。它使用 GitHub 给当前仓库提供的 `GITHUB_TOKEN`，不需要个人访问令牌或 Codex 登录信息。

当前 EXE 的完整版本是 `0.7.0.0`，对应标签必须是 `v0.7.0`。`v0.7` 或与实际二进制版本不一致的标签会被拒绝。

准备下一个版本时，先更新 `src/App.cs` 中的程序集版本、`app.manifest` 的版本，以及 `CHANGELOG.md` 和文档中的相关说明。检查、构建并提交全部改动后，再创建和推送标签：

```powershell
git tag -a v0.7.0 -m "CodexQuota v0.7.0"
git push origin v0.7.0
```

如果首次源码通过网页上传，可以在本地克隆仓库后运行上面的标签命令。若希望全程使用网页，请采用下一节的手动发布方式；不要同时让手动和自动流程创建同一标签的 Release。

Actions 成功后，打开 **Releases** 的草稿，检查版本说明，下载 ZIP 解压运行，并核对附带校验文件。补充实际变化与已知问题后，最后点击 **Publish release**。确认前草稿仅供有权限的维护者查看。

校验示例：

```powershell
Get-FileHash -Algorithm SHA256 .\CodexQuota-v0.7.0-win-x64.zip
```

如果仓库规则要求在工作流之外授权写入标签，先由仓库维护者配置规则；本项目不会绕过规则。如果 Actions 提示工作流权限不足，检查仓库及组织是否允许工作流所声明的 `contents: write`，它只用于创建发行草稿和上传资产。

## 不用 Actions 手动发布

在本机完成构建、测试与打包后，在 GitHub **Releases → Draft a new release** 选择正确标签，填写版本说明，并将 `dist/` 中上表列出的四种资产附加到草稿。下载测试通过后再发布。这样也能直接提供 EXE，不需要用户安装编译器。

## 常见发布问题

| 情况 | 处理方法 |
| --- | --- |
| 测试在 GitHub runner 失败 | 打开失败步骤的日志定位原因；不要跳过测试后声称构建已验证。本地桌面与托管 runner 的窗口环境可能不同。 |
| 已有同名 Release，重新运行创建失败 | 先检查已有草稿及资产；确认是否已经完整生成。工作流不会覆盖已发布的版本。 |
| 标签与 EXE 版本不一致 | 核对标签指向的提交及程序集版本，准备一个正确的新版本提交和标签；不要默默移动已经发布的标签。 |
| 源码 ZIP 中没有 EXE | 正常。请在 Releases 的 Assets 下载 `win-x64.zip` 或 EXE 与配置。 |
| 普通用户无法看到草稿 | 在完成审阅后发布正式 Release。 |

## 工作流维护

工作流中的第三方 Action 固定到完整提交 SHA，并在注释中记录版本。升级时核对官方发布记录和提交，更新 SHA，而非改成不固定的 `main`。CI 的代码检出不持久化 Git 凭据；PR 流程只有读取仓库内容的权限。

官方参考：

- [GitHub：上传现有项目](https://docs.github.com/en/get-started/start-your-journey/uploading-a-project-to-github)
- [GitHub：管理 Releases](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository)
- [GitHub CLI：gh release create](https://cli.github.com/manual/gh_release_create)
- [actions/checkout 官方发布记录](https://github.com/actions/checkout/releases)
- [actions/upload-artifact 官方发布记录](https://github.com/actions/upload-artifact/releases)
