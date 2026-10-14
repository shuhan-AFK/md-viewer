# Windows 分屏修复验证

`source/windows-native` 从工作区中保留的 Windows 程序恢复为可编译的 .NET Framework 4.8 源码。原版和通用版的窗口、文档管理、Word 导出和启动逻辑相同；构建时可以指定不同的图标。

修复包括：

- 给原生 HWND 添加标题栏、可调整大小、系统菜单、最大化和最小化样式，使 Windows 可以处理窗口贴靠；
- 通过 `WM_NCCALCSIZE` 保留现有自定义标题栏外观，并让最大化内容落在显示器工作区内；
- 在 WinForms 刷新窗口样式时保留贴靠能力，同时保留 `FormBorderStyle.None` 的尺寸计算，避免恢复后窗口变大；
- 允许从最大化状态发起原生标题栏拖动，并传递真实的鼠标屏幕坐标；
- 将最小窗口尺寸调整为 480 × 360，缩放边缘按 DPI 调整。

构建原生程序：

```powershell
./scripts/build_windows.ps1
```

运行原生窗口验证：

```powershell
dotnet build ./qa/window-frame/WindowFrameTests.csproj -c Release -o ./build/window-frame-tests
./build/window-frame-tests/WindowFrameTests.exe
```

验证覆盖实际 HWND 样式、窗口句柄重建、无边框客户区、窄窗口、八方向缩放、负坐标、最大化工作区、最大化/最小化恢复尺寸。它创建自己的透明测试窗口，不操作用户已打开的阅读器。

可使用 `--webview-smoke` 验证 WebView2 加载和标题栏拖动接口。运行目录必须包含原版运行资源（`bridge.js`、`wwwroot`、WebView2 DLL 和 `WebView2Loader.dll`）。测试显式安装界面线程同步上下文，确保异步初始化回到界面线程。

2026-10-01：上述原生窗口检查和 WebView2 加载检查通过。实际鼠标拖到屏幕边缘的贴靠预览仍需在桌面上确认。
