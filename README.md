# MD Viewer · Markdown 文件阅读器

一款轻量级的 Markdown 桌面阅读器，支持 Windows 与 Linux 双平台。

提供 Markdown 实时预览与编辑、多主题切换、本地图片渲染、文件变更自动刷新，以及 DOCX 导出等功能。

## 下载与安装

安装包见 [Releases 页面](https://github.com/shuhan-AFK/md-viewer/releases)（也可在 [`dist/`](https://github.com/shuhan-AFK/md-viewer/tree/main/dist) 目录中直接下载）：

| 平台 | 文件 | 安装方式 |
| --- | --- | --- |
| Windows（安装版） | `MD-Viewer-2.2.0-4-Windows-x64-Setup.exe` | 双击运行，按向导安装 |
| Windows（便携版） | `MD-Viewer-2.2.0-4-Windows-x64-Portable.zip` | 解压后直接运行 `MD Viewer.exe` |
| Linux | `md-viewer-plain_2.2.0-4_all.deb` | `sudo apt install ./md-viewer-plain_2.2.0-4_all.deb` |

> 提示：Windows 版需要 WebView2 运行时（Windows 10/11 通常已自带）。

安装与构建脚本的位置：

- [`installer/md-viewer-plain.nsi`](https://github.com/shuhan-AFK/md-viewer/blob/main/installer/md-viewer-plain.nsi) —— Windows 安装程序（NSIS）脚本
- [`source/linux-package/`](https://github.com/shuhan-AFK/md-viewer/tree/main/source/linux-package) —— Linux 包结构与安装规则（`control` / `postinst` / `postrm`）
- [`scripts/build_deb.py`](https://github.com/shuhan-AFK/md-viewer/blob/main/scripts/build_deb.py) —— Linux `.deb` 打包脚本
- [`scripts/build_windows.ps1`](https://github.com/shuhan-AFK/md-viewer/blob/main/scripts/build_windows.ps1) —— Windows 构建脚本

## 功能特性

- **Markdown 预览与编辑**：实时渲染，分栏阅读
- **多主题切换**：界面使用纯色主题与项目自带图标，不依赖任何第三方素材
- **本地图片**：直接渲染文档中引用的本地图片
- **文件监听**：磁盘上的文件被修改后自动刷新
- **DOCX 导出**：将 Markdown 文档一键导出为 Word 文件
- **单实例运行**：重复打开文件时复用已有窗口（Windows）
- **窗口贴靠与高 DPI 适配**（Windows）

## 平台与实现

| 平台 | 实现 | 说明 |
| --- | --- | --- |
| Windows | C# · .NET Framework 4.8 · WebView2 | 原生窗口 + WebView2 渲染内核 |
| Linux | Python 3 · GTK4 · WebKitGTK 6 | GTK 原生应用 |
| 界面 | React（构建产物随包发布） | 运行于 `wwwroot/` |

## 目录结构

```text
.
├── assets/                      通用版图标与样式资源
├── dist/                        通用版发布包（Windows 安装/便携、Linux .deb）与校验文件
├── installer/                   Windows NSIS 安装脚本
├── qa/                          预览、截图、窗口测试与验证资料
├── scripts/                     构建、图标处理与资源处理脚本
├── source/
│   ├── windows-native/          Windows 端 C# 源码（含窗口贴靠修复）
│   ├── windows/                 Windows 运行资源与预编译程序
│   └── linux-package/           Linux 端源码与打包资源
├── LICENSE                      MIT 许可证
└── README.md                    项目说明
```

> `build/`、`tools/` 等构建产物与工具依赖不随仓库提供，可按 `scripts/` 中的脚本重新生成。

## 构建与运行

### Windows

- 环境要求：.NET Framework 4.8 Developer Pack、WebView2 Runtime
- 构建：使用 `source/windows-native/MD Viewer.csproj`（`net48`，x64）
- 打包脚本：`scripts/build_windows.ps1`（应用构建）、`installer/md-viewer-plain.nsi`（NSIS 安装包）

### Linux

- 运行依赖：`python3 (>=3.10)`、`python3-gi`、`gir1.2-gtk-4.0`、`gir1.2-webkit-6.0`、`gir1.2-gdkpixbuf-2.0`
- 直接运行：`source/linux-package/opt/md-viewer-plain/md_viewer.py`
- 打包：`scripts/build_deb.py` 生成 `.deb` 安装包（包名 `md-viewer-plain`）

## 版本说明

| 版本 | 说明 |
| --- | --- |
| 2.2.0-4（通用版） | 本工程的打包基线：通用纯色主题，Windows 安装版/便携版、Linux `.deb` |
| 2.2.0-5（SnapFix） | 修复 Windows 窗口贴靠行为；修复源码已包含在本仓库 `source/windows-native` 中 |

`dist/SHA256SUMS.txt` 提供各发布包的 SHA-256 校验值。

## 已验证内容

分屏修复已通过原生窗口样式、句柄重建、八方向缩放、窗口尺寸恢复、最大化工作区以及 WebView2 加载检查；其余验证资料见 `qa/` 目录。

## 版权

© 2026 shuhan-AFK。本项目代码采用 [MIT 许可证](LICENSE)，可自由使用、修改和分发（需保留版权声明）。
