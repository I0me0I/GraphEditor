<div align="center">

# GraphEditor

**轻量的 Windows 图论绘图与编辑工具**

画顶点、连边、上色命名，然后导出成 SVG。绿色单文件，免安装，不需要 .NET SDK。

[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4)](#build)
[![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4)](#build)
[![Build](https://img.shields.io/badge/build-csc.exe%20%2F%20no%20SDK-success)](#build)
[![Tests](https://img.shields.io/badge/tests-501%20checks-brightgreen)](#tests)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

<img src="docs/images/main.png" alt="GraphEditor 界面" width="820">

</div>

## 目录

- [这是什么](#about)
- [功能](#features)
- [快速开始](#quickstart)
- [操作方式](#controls)
- [从源码构建](#build)
- [测试](#tests)
- [项目结构](#layout)
- [`.graph` 文件格式](#fileformat)
- [常见问题](#faq)
- [关于数字签名](#signing)
- [许可证](#license)

<a id="about"></a>

## 这是什么

GraphEditor 是一个画**图（graph）**的 Windows 桌面工具：顶点、边、自环、多重边，配上名字和颜色，导出成矢量图。

它不追求做成通用图示工具，而是把「画一张图论意义上的图」做扎实：

- 画布是**无限平面**（参考 draw.io），想画多远画多远，不用管理「画布尺寸」；
- 顶点与边各是**一套全局样式**，改一次全体生效，不用逐个调；
- **框选 / 复制 / 剪切 / 粘贴 / 旋转**齐全，批量整理一张图很快；
- 导出 **SVG** 只保留内容的最小外接矩形，直接能贴进论文或课件。

整个程序约 **4,800 行 C#**，零第三方依赖，用 Windows 自带的 `csc.exe` 就能编译出单个 exe。

<a id="features"></a>

## 功能

### 画布

- **无限平面** —— 没有纸张边界，元素可以落在任意坐标（含负坐标）。
- **原点居中** —— 初始时 `(0,0)` 位于客户区正中，`x` 向右、`y` 向下。
- **滚动条按需出现** —— 内容全部可见时不显示，超出时才出现，可滚动范围随内容变化。
- **滚轮缩放** —— 以光标为锚点，10% ~ 800%。

### 顶点与边

| 能力 | 说明 |
| --- | --- |
| 新建顶点 | 空白处右键 →「新建顶点」（落在光标处） |
| 连线 | **在顶点上按住右键拖到另一个顶点**再松开 |
| 自环 / 多重边 | 拖回自身即成自环；同一对顶点间可连多条边（自动弯曲错开） |
| 选中与移动 | 左键点击选中，拖动移动（关联的边自动跟随） |
| 框选 | 空白处左键拖出矩形；**只有两个端点都被框中的边才会被选中** |
| 多选 | `Ctrl` + 左键点击，可混合选择顶点与边 |
| 微调 | 方向键 1 像素，`Shift` + 方向键 10 像素 |

### 样式

顶点与边是**两组独立的全局样式**：

- **顶点**：颜色、大小、形状（圆形 / 方形）
- **边**：颜色、线宽、线型（实线 / 虚线 / 点线）

同一组内所有元素的大小与样式完全一致；**只有名字和颜色可以逐个不同**（用调色盘或右键菜单单独着色）。

### 命名

命名规则只影响**新建**的元素，不会重命名已有的。

| 规则 | 顶点得到 | 边得到 |
| --- | --- | --- |
| 不命名 | 无名字 | 无名字（**默认**） |
| 字母 | `A` `B` … `Z`，之后 `A1` `B1` …（**顶点默认**） | `a` `b` … `z`，之后 `a1` `b1` … |
| 前缀 + 数字 | `V1` `V2` `V3` … | `e1` `e2` `e3` … |
| 两端点名字 | — | 端点名字拼接，如 `AB`；重名加数字后缀 |

名字显示位置也可配置：顶点可选 内部 / 上方 / 下方 / 左侧 / 右侧，边可选 内部 / 上方 / 下方。

### 编辑

- **复制 / 剪切 / 粘贴** —— 范围是选中顶点集**诱导的子图**：选中的顶点，加上两端点都被选中的边。剪切时还会额外删除所有与选中顶点相连的边。
- **旋转** —— 把选中的顶点绕其几何中心旋转（任意角度，或 45° / 90° 等预设）。
- **删除** —— `Delete`，或右键菜单。

### 文件

| 格式 | 说明 |
| --- | --- |
| `.graph` | 自己的 JSON 格式，读回后样式、名字、颜色全部还原 |
| `.svg` | 矢量导出，只包含内容的最小外接矩形（外加 12 像素留白） |

内置容错：悬空边（引用了不存在的顶点）会被丢弃，非法颜色会被忽略，缺失字段用默认值补齐。

<a id="quickstart"></a>

## 快速开始

### 下载即用

1. 到 [Releases](https://github.com/I0me0I/GraphEditor/releases) 下载 `GraphEditor.exe`；
2. 双击运行（首次会有 SmartScreen 提示，见[关于数字签名](#signing)）；
3. 无需安装，无需 .NET SDK。

> 想让 `.graph` 文件双击默认用它打开：运行 `tools\register-filetype.ps1`（只写当前用户注册表，不需要管理员权限）。

### 五分钟上手

1. 空白处**右键 →「新建顶点」**，连点几下摆出几个点；
2. 在顶点上**按住右键拖到另一个顶点**，松开即成一条边；
3. **左键拖动**顶点调整位置，边会跟着走；
4. **滚轮**缩放，**空白处右键拖动**平移画布；
5. 想批量操作就**左键框选**，再用右键菜单复制 / 旋转 / 改色；
6. `文件 →「导出为 SVG」` 拿到矢量图。

<div align="center">
<img src="docs/images/empty.png" alt="空画布：无限平面，原点居中" width="620">
</div>

<a id="controls"></a>

## 操作方式

### 视图

| 操作 | 方式 |
| --- | --- |
| 缩放 | 鼠标滚轮（以光标为锚点，10% ~ 800%） |
| 平移 | 在空白处或边上**按住右键拖动**（起手延迟仅 50ms） |
| 适应窗口 | 视图菜单，或 `Ctrl+9` |
| 实际大小 | 视图菜单，或 `Ctrl+1` |
| 回到原点 | 视图菜单「回到原点」 |
| 居中显示内容 | 视图菜单「居中显示内容」（打开文件时自动执行一次） |

### 鼠标

| 手势 | 行为 |
| --- | --- |
| 左键点击 | 选中顶点 / 边 |
| `Ctrl` + 左键点击 | 加选 / 减选 |
| 左键拖动 | 空白处为框选；顶点上为移动 |
| **顶点上右键拖动** | 拉出连线 |
| **空白处 / 边上右键拖动** | 平移画布 |
| 空白处右键**点击**（未拖动） | 弹出右键菜单（按住多久都算，长按不动也算） |
| 滚轮 | 缩放 |

> 右键手势刻意对齐 draw.io：**右键拖动就是平移**。为避免「按下瞬间的手抖被当成拖动」，
> 加了 50ms 的起手延迟；延迟内松手、或延迟过后始终没有移动，抬手都弹菜单。

### 键盘

| 快捷键 | 功能 |
| --- | --- |
| `Ctrl+N` / `Ctrl+O` / `Ctrl+S` | 新建 / 打开 / 保存 |
| `Ctrl+Z` | 撤销 |
| `Ctrl+A` | 全选 |
| `Ctrl+C` / `Ctrl+X` / `Ctrl+V` | 复制 / 剪切 / 粘贴 |
| `Delete` | 删除选中元素 |
| `Esc` | 取消选择 |
| 方向键 / `Shift`+方向键 | 微调 1 / 10 像素 |

### 菜单栏

- **文件** —— 新建、打开、保存、另存为、导出为 SVG、退出
- **编辑** —— 撤销、全选、取消选择、复制、剪切、粘贴、删除
- **视图** —— 放大、缩小、实际大小、适应窗口、重置视图、回到原点、居中显示内容
- **属性** —— 顶点属性（颜色 / 大小 / 形状 / 默认命名 / 名字位置）、边属性（颜色 / 线宽 / 线型 / 默认命名 / 名字位置）、画布
- **帮助** —— 使用说明、关于

<a id="build"></a>

## 从源码构建

### 系统要求

| 项 | 要求 |
| --- | --- |
| 操作系统 | Windows 7 SP1 及以上（已在 Windows 10 / 11 验证） |
| 运行时 | .NET Framework 4.8（Windows 10 1903+ / Windows 11 自带） |
| 编译 | 系统自带的 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` |
| 开发环境 | **不需要** Visual Studio，也**不需要** .NET SDK |

### 构建

```powershell
git clone https://github.com/I0me0I/GraphEditor.git
cd GraphEditor
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
```

产物：`build\GraphEditor.exe`（约 470 KB 单文件）。

脚本会自动定位 `csc.exe`、把 `assets\GraphEditor.ico` 嵌进 exe，并把 `README.md`、`demand.md`、`docs\` 复制到 `build\`。

<details>
<summary>改用 Visual Studio 编译</summary>

把 `src\GraphEditor\*.cs` 加入一个「Windows 窗体应用 (.NET Framework)」项目，目标框架选 **.NET Framework 4.8** 即可。

注意源码只用了 **C# 5** 语法（没有字符串内插、没有 `nameof`、没有 `?.`），因为系统自带的 csc 版本较老。想放宽可自行提高语言版本。

</details>

<a id="tests"></a>

## 测试

项目自带一个**不依赖任何测试框架**的自检程序，共 **501 项断言**，覆盖模型、几何、序列化、SVG 导出、滚动与命中测试，以及真实的 UI 交互路径。

```powershell
# 全部
powershell -NoProfile -ExecutionPolicy Bypass -File tests\run-tests.ps1

# 只跑某个用例
powershell -NoProfile -ExecutionPolicy Bypass -File tests\run-tests.ps1 -Only TestPanGestures
```

输出：

```
================ 自检结果 ================
通过: 501    失败: 0
产物目录: ...\artifacts
========================================
RESULT passed=501 failed=0
```

自检会真实构造 `MainForm`、模拟鼠标按下 / 移动 / 抬起（包括带真实定时器等待的用例），因此验证的是**用户实际会走的代码路径**，而不只是纯函数。

<a id="layout"></a>

## 项目结构

```
GraphEditor/
├─ src/GraphEditor/            应用源码（约 4,800 行 C#）
│  ├─ Program.cs               入口、命令行开关
│  ├─ MainForm.cs              菜单栏、状态栏、文档生命周期
│  ├─ CanvasControl.cs         工作区：绘制、缩放、滚动条、全部鼠标键盘交互
│  ├─ GraphModel.cs            模型：顶点、边、画布全局样式、几何与命中测试
│  ├─ GraphIo.cs               .graph 读写（自带 JSON 读写器，无第三方库）
│  ├─ SvgExport.cs             SVG 导出
│  ├─ Dialogs.cs               输入对话框、调色盘控件
│  └─ AssemblyInfo.cs          版本信息
├─ tests/
│  ├─ TestRunner.cs            501 项断言的自检程序
│  └─ run-tests.ps1            编译并运行自检
├─ tools/
│  ├─ make-icon.ps1            从零生成应用图标（7 个尺寸的 DIB 帧）
│  └─ register-filetype.ps1    注册 / 取消 .graph 文件关联（HKCU）
├─ assets/GraphEditor.ico      应用图标
├─ docs/
│  ├─ images/                  README 截图
│  └─ TROUBLESHOOTING.md       详细说明：实现要点、诊断开关、排错
├─ build.ps1                   构建脚本
├─ demand.md                   最初的完整需求说明
└─ LICENSE                     MIT
```

<a id="fileformat"></a>

## `.graph` 文件格式

就是一个 UTF-8 的 JSON 文本，可以手改，也可以进版本控制。

```jsonc
{
  "format": "GraphEditor.Graph",
  "version": "1.0",
  "vertexStyle": {
    "radius": 18,
    "shape": "circle",                          // circle | square
    "defaultColor": "#2D7DD2",
    "naming": "alphabetic",                     // none | alphabetic | numeric
    "labelPlacement": "inside"                  // inside | above | below | left | right
  },
  "edgeStyle": {
    "width": 2,
    "lineStyle": "solid",                       // solid | dashed | dotted
    "defaultColor": "#404040",
    "naming": "none",                           // none | alphabetic | numeric | fromEndpoints
    "labelPlacement": "above"                   // inside | above | below
  },
  "vertices": [
    { "id": 1, "x": 380, "y": 200, "name": "A", "color": null }
  ],
  "edges": [
    { "id": 1, "v1": 1, "v2": 2, "name": "e1", "color": null }
  ]
}
```

- `color` 为 `null` 表示跟随全局默认色。
- 坐标是**图像坐标**：原点 `(0,0)`，`x` 向右、`y` 向下。因为画布是无限平面，
  坐标可以是负数、也可以很大；加载时**不会**被钳制到任何矩形内。
- 文件里**没有**画布尺寸字段。早期版本写过 `canvas.width/height`，现在读取时直接忽略。
- 加载时容错：悬空边丢弃、非法颜色忽略、缺失字段用默认值补齐。

<a id="faq"></a>

## 常见问题

<details>
<summary><b>首次运行弹出「Windows 已保护你的电脑」怎么办？</b></summary>

点弹窗里的 **「更多信息」** → 右下角出现 **「仍要运行」** → 点它即可，Windows 会记住这个选择。

原因见[关于数字签名](#signing)。

</details>

<details>
<summary><b>提示「无法加载文件 … 因为在此系统上禁止运行脚本」</b></summary>

系统的 PowerShell 执行策略是 Restricted。按本文档的方式调用即可（只对本次进程放开策略）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
```

不必为了跑脚本把全局执行策略改成 `Unrestricted`。

</details>

<details>
<summary><b>双击 <code>.graph</code> 文件没有用它打开</b></summary>

先运行一次文件关联注册（只写当前用户，不需管理员）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\register-filetype.ps1
```

若图标没立刻刷新，注销再登录一次，或在「默认应用」里手动指定。

</details>

<details>
<summary><b>怎么确认下载的 exe 没被篡改？</b></summary>

Release 说明里会给出 SHA-256，本地核对：

```powershell
Get-FileHash .\GraphEditor.exe -Algorithm SHA256
```

在没有数字签名的阶段，这是最实用的替代方案。

</details>

<details>
<summary><b>遇到滚动 / 视图异常，怎么抓现场信息？</b></summary>

程序内置了几个只用于排查的启动参数（正常使用不需要）：

| 参数 | 作用 |
| --- | --- |
| `/diag` | 状态栏右下角实时显示视图原点、滚动条位置、可滚动上限、缩放 |
| `/diaglog:路径` | 每 120ms 把上述状态追加写入指定文件 |
| `/dump:路径` | 启动后把滚动映射写成文本文件 |
| `/open:路径` | 直接打开指定的 `.graph` 文件 |

```powershell
.\build\GraphEditor.exe /diag
```

`/diag` 模式下按 **F12** 会把当前视图状态写入程序目录的 `scroll-state.txt`
（含缩放、视口尺寸、视图原点、滚动条位置、以及视口上下边对应的坐标）。

更多实现细节与排错见 [`docs/TROUBLESHOOTING.md`](docs/TROUBLESHOOTING.md)。

</details>

<a id="signing"></a>

## 关于数字签名

**先说结论：这个程序没有数字签名，所以 Windows SmartScreen 会拦一下。** 这是预期行为，不是程序有问题 —— 首次运行点「更多信息」→「仍要运行」即可。

之所以没签名，是因为让公开发布的程序获得**有效的**签名要花钱，而且有个容易被忽略的坑。

### 自签名证书解决不了问题

可以自己生成证书来签名（`New-SelfSignedCertificate`），但**对别人毫无用处** —— 那个证书的根不在任何一台 Windows 的受信任根列表里，别人拿到后警告不会变少，甚至可能提示「证书不受信任」。自签名只适合在自己机器上做测试。

### 真正要消除 SmartScreen 警告，只有两条路

| 方案 | 费用 | 效果 |
| --- | --- | --- |
| **EV 代码签名证书**（扩展验证） | 约 ¥2000–5000 / 年 | 立刻获得 SmartScreen 信誉，**新证书也直接不报** |
| **OV 代码签名证书**（标准） | 约 ¥1000–3000 / 年 | 仍需**累积下载量**建立信誉，新证书前期照样报警告，几周到几个月后逐渐消失 |

无论哪种，**2023 年 6 月起私钥必须存放在硬件令牌或云 HSM 中**（CA/Browser Forum 新规），不能再是硬盘上的 `.pfx` 文件。主流选择：

- **DigiCert / Sectigo / GlobalSign** —— 传统 CA，提供硬件 USB Token 或云签名服务；
- **Azure Trusted Signing**（微软自家，原 Azure Code Signing）—— 约 $9.9/月起，云 HSM，对个人开发者最省事，但**目前个人身份申请仍受限**，通常需要企业实体。

### 免费的可能途径

如果这是开源项目，可以试试：

- **[SignPath.io](https://signpath.io/)** —— 为开源项目提供免费代码签名（需申请审核）；
- **[Certum](https://certum.eu/)** —— 面向开源项目的低价证书。

这两者给的通常是 **OV 级别**，也就是说**仍然需要时间积累信誉**，只是不用自己掏钱。

### 有了证书之后怎么签

用 Windows SDK 里的 `signtool.exe`（需装 [Windows SDK](https://developer.microsoft.com/windows/downloads/windows-sdk/)，或只装其中的签名工具）。

```powershell
# 时间戳很关键：没有它，证书过期后已签名的文件会立刻失效
signtool sign `
  /fd SHA256 `
  /tr http://timestamp.digicert.com `
  /td SHA256 `
  /a `
  build\GraphEditor.exe

# 验证
signtool verify /pa /v build\GraphEditor.exe
```

若用云签名服务（Azure Trusted Signing / DigiCert KeyLocker），把 `sign` 换成对应的 `dlib` / `ks` 参数即可，CA 文档里都有现成命令。

<a id="license"></a>

## 许可证

[MIT](LICENSE) © 2026 I0me0I —— 可自由使用、修改、商用，只需保留版权声明。
