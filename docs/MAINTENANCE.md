# 维护者手册

这份文档只记录**项目制作过程**相关的内容（推送、版本号、发布流程等）。
面向使用者和普通开发者的说明在 [`../README.md`](../README.md)。

---

## 1. 推送代码

代码在本地提交，推送需要访问 GitHub 并完成认证。

### 一键推送

在**普通 PowerShell 窗口**里运行：

```powershell
cd D:\Projects\DshProjects\GraphEditor
powershell -NoProfile -ExecutionPolicy Bypass -File tools\push.ps1
```

脚本会：

1. 探测本机代理（`127.0.0.1:7890`）是否在监听，在就用上
2. 显示待推提交数
3. 执行 `git push -u origin main`
4. 失败时给出针对性提示（连接重置 → 起代理；要凭据 → 用 PAT）

### 为什么 AI 沙箱里推不上去

逐层排查过的结论（不是猜的）：

| 层 | 现象 | 结论 |
| --- | --- | --- |
| TLS | `schannel: AcquireCredentialsHandle failed: SEC_E_NO_CREDENTIALS` | 沙箱访问不到 Windows 凭据存储 |
| 换 TLS 后端 | `http.sslBackend=openssl` → `ls-remote` 成功 | 网络通，TLS 可绕过 |
| 推送 | `Recv failure: Connection was reset` | 直连 GitHub 推流被重置 |
| 走代理 | `-c http.proxy=http://127.0.0.1:7890` → 连接正常 | 网络通道打通 |
| 认证 | `could not read Username`；GCM 起不来（`sh.exe: couldn't create signal pipe, Win32 error 5`） | **凭据拿不到** |
| SSH 备选 | msys2 `ssh.exe` 被禁创建信号管道；Windows 原生 `ssh.exe` 挂住 | 沙箱里 SSH 也不通 |

所以：**网络没问题，认证在这一步永远过不去**。必须由维护者在本机会话里执行推送。

### 可选：让 AI 也能推（PAT）

如果希望自动化推送，可以创建一个 GitHub **fine-grained Personal Access Token**
（只勾 `Contents: Read and write`），保存到：

```
.git/github-token        ← 只在 .git 里，不会被提交
```

然后 AI 可以用「token 写进 URL」的方式推送（`credential.helper=` 清空，不碰凭据存储）：

```
git -c http.sslBackend=openssl -c http.proxy=http://127.0.0.1:7890 -c credential.helper= \
    push https://x-access-token:<token>@github.com/I0me0I/GraphEditor.git main
```

注意：

- token 是明文放在 `.git/` 里，**不要提交**，也不要放进共享目录
- 随时可以在 GitHub 设置里撤销
- 不设置也完全没关系，用 `tools\push.ps1` 手动推即可

---

## 2. 版本号

版本号写在两个地方，**必须同步**：

- `src/GraphEditor/AssemblyInfo.cs` → `AssemblyVersion` / `AssemblyFileVersion` / `AssemblyInformationalVersion`
- `src/GraphEditor/app.manifest` → `assemblyIdentity` 的 `version`

约定：

| 改动性质 | 版本位 | 例子 |
| --- | --- | --- |
| Bug 修复、视觉微调 | patch | 1.3.8 → 1.3.9 |
| 功能添加、行为变更 | minor | 1.3.9 → 1.4.0 |
| 不兼容变更、整体重构 | major | 1.x → 2.0 |

纯文档、注释、测试清理不改版本号。

---

## 3. 断言数量

自检断言数出现在 README 的徽章、正文和示例输出中，改动测试后要同步：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests\run-tests.ps1
```

以输出的 `RESULT passed=N failed=M` 为准，更新 README 里所有 `N`。

---

## 4. 构建产物

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
```

- 输出到 `build\`（已 gitignore）
- 构建过程会先编译到 `build\.stage\`，再移动到 `build\GraphEditor.exe`
  （避免杀毒软件锁文件导致 `CS0016`；文件名不能改，否则程序集名会跟着变）
- `build\` 里同时复制 README、需求文档、LICENSE、docs 和 `.exe.config`

---

## 5. 文档分工

| 文件 | 放什么 |
| --- | --- |
| `README.md` | **只放**使用者和普通开发者需要的信息：功能、操作方式、构建、测试、文件格式、常见问题 |
| `docs/TROUBLESHOOTING.md` | 深入说明：实现要点、诊断开关、排错步骤 |
| `docs/MAINTENANCE.md`（本文） | 制作过程：推送、版本号、发布、产物管理 |

**不要**把推送流程、凭据配置、AI 工作流这类内容写进 README。

---

## 6. 提交信息

- 用中文写清楚「改了什么、为什么改」
- 多行提交信息用文件传入，避免 PowerShell 把中文参数拆坏：

```powershell
$msg = @'
第一行标题
正文……
'@
[System.IO.File]::WriteAllText("$env:TEMP\msg.txt", $msg, (New-Object System.Text.UTF8Encoding($false)))
git -c i18n.commitEncoding=UTF-8 commit -F "$env:TEMP\msg.txt"
```

---

## 7. PowerShell 脚本必须是纯 ASCII

`tests\run-tests.ps1`、`build.ps1`、`tools\*.ps1` 都会在 Windows PowerShell 5.1 下执行，
而 5.1 读取**无 BOM** 的脚本时按 ANSI 解码 —— 中文会乱码甚至导致语法错误。

检查：

```powershell
$bytes = [System.IO.File]::ReadAllBytes('.\tools\push.ps1')
($bytes | Where-Object { $_ -gt 127 }).Count   # 必须是 0
```
