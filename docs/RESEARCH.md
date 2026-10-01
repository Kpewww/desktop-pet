# 技术调研与选型

## 结论：原生 C# WinForms + Win32 分层窗口
桌宠要常驻桌面，所以把「不给电脑增加负担」放在第一位。

| 方案 | 体积 | 内存 | 其他 |
|---|---|---|---|
| **C# WinForms（.NET Framework 4.8）** | 单个 exe，目标小于 3MB | 目标小于 40MB | Windows 10/11 自带运行库，开发也只用系统自带的 `csc.exe` |
| Electron | 安装包约 80MB，装好约 250MB | 100–200MB | 自带 Chromium，常驻多个进程 |
| Tauri | 小（用系统自带的 WebView2） | WebView2 进程仍有几十 MB | 开发需要 Rust 工具链 |
| Godot / PySide | 30–100MB | 80MB 以上 | 引擎或 Qt 整包打包 |

## 关键 Win32 技术点
- **逐像素透明窗口**：[UpdateLayeredWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow)
  - 一次调用同时更新窗口的位置和画面，移动时不会撕裂。
  - alpha 为 0 的像素，鼠标点击会直接穿透到后面的窗口，所以透明区域天然可以点穿。
- **不抢焦点**：窗口加上 `WS_EX_NOACTIVATE`，并且在处理 `WM_MOUSEACTIVATE` 时返回 `MA_NOACTIVATE`；再加上 `WS_EX_TOOLWINDOW`，这样任务栏和 Alt+Tab 里都不会出现。
- **垂直同步节拍**：用 `DwmFlush` 等到下一次桌面合成。只在人偶移动时启用；静止时用一次性计时器，按帧时长唤醒。
- **剪贴板监听**：[AddClipboardFormatListener](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-addclipboardformatlistener) 注册后会收到 `WM_CLIPBOARDUPDATE` 通知，不需要轮询。
- **剪贴板隐私**：密码管理器复制密码时，会同时放入几个特殊格式，提示监听程序不要记录。碰到下面任一格式就跳过这次复制：
  - `ExcludeClipboardContentFromMonitorProcessing`
  - `Clipboard Viewer Ignore`
  - `CanIncludeInClipboardHistory`，且值为 0

  详见 [Edge Drop 的说明](https://www.edgedrop.app/blog/what-sensitive-formats-should-monitors-ignore) 和 [CrossPaste 的实现](https://github.com/CrossPaste/crosspaste-desktop/pull/5015)。
- **离开检测**：`GetLastInputInfo`。
- **全屏 / 演示模式检测**：`SHQueryUserNotificationState`。
- **全局快捷键**：`RegisterHotKey`。
- **开机自启**：写注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`。

## 字体
[Fusion Pixel Font（缝合像素字体）](https://github.com/TakWolf/fusion-pixel-font)：开源的泛中日韩像素字体，有 8、10、12px 三种尺寸，授权是 SIL OFL 1.1。我们把它的 BDF 版本转成位图字体，嵌进 exe 使用，并附带授权文件。

## 参考项目
- [WindowPet](https://github.com/SeakMengs/WindowPet)：用 Tauri + React 写的桌宠，支持点击穿透、自定义角色、同时放多只。
- [AI Desktop Pet](https://github.com/AkshitIreddy/convai-desktop-pet)：会感知窗口、能对话的 AI 桌宠。我们这次不做 AI 对话。
