# MiRemote — Windows 版

小米蓝牙遥控器 2 Pro → Windows 全能控制台的移植版本。

本目录是独立项目，与 `../mi_remote_control`（macOS 原版，Swift 6）并列。
技术栈：**C# .NET 9 + WPF**，零第三方 NuGet 依赖，仅用 Windows 内置 API（Raw Input / SendInput / Win32）。

## 构建

```powershell
cd C:\Users\umaxz\projects\mi-remote-windows
dotnet build src\MiRemote.App\MiRemote.App.csproj -c Release
# 运行
.\src\MiRemote.App\bin\Release\net9.0-windows\MiRemote.exe
# 自检
.\src\MiRemote.App\bin\Release\net9.0-windows\MiRemote.exe --self-test
```

或直接 `dotnet run --project src\MiRemote.App\MiRemote.App.csproj`。

## 功能状态

- ✅ 13 键 tap/hold/double/层/手势状态机（与 macOS 版语义对齐）
- ✅ 基础文字输入态保护（方向/OK/返回不被 Profile 接管）
- ✅ App 控制模式（TV 单击进/出层 2 + HUD）
- ✅ AI 批准层（OK=Enter、返回=Esc、音量±=切标签、菜单=Shift+Tab…）
- ✅ 窗口选择器浮层（菜单短按）
- ✅ 系统功能菜单浮层（菜单长按）
- ✅ 鼠标模式（电源长按）
- ✅ 任务视图态（Home 单击 = Win+Tab，左右切虚拟桌面）
- ✅ per-app profile（按进程名匹配：windowsterminal / code / chrome / wechat / feishu / zoom / vlc / powerpnt …）
- ✅ 预设库 + JSON 导入导出
- ✅ 托盘图标 + 暂停/恢复 + 日志
- ⚠️ 语音输入：Windows 版暂未移植 ATVV BLE 语音链路；语音键默认触发系统听写 (Win+H)。需要虚拟声卡（VB-CABLE）与解码器移植，后续版本补充。
- ⚠️ 音量键在 App 控制模式下仍会改变系统音量（Windows 无法完全拦截消费者页按键，已知限制）。

## 配置

配置文件：`%APPDATA%\MiRemote\config.json`，schema 与 macOS 版兼容（动作 type、settings 字段一致），
差异：
- profile 键为 Windows 进程名（小写、无 `.exe`），如 `windowsterminal`、`code`、`chrome`。
- 修饰键名：`ctrl/alt/shift/win`，同时接受 macOS 别名 `left_cmd→ctrl`、`left_option→alt`。
- 新增 settings：`voiceFallback`、`useKeyboardHook`、`repeatDelayMs`、`repeatIntervalMs`、`startEnabled`。

## 设备识别

默认 VID `0x2717` / PID `0x32B8`（小米蓝牙遥控器 2 Pro）。换型号在 `settings.remoteVendorID/remoteProductID`（十进制）修改。

## 已知限制

- Raw Input 的 `RIDEV_NOLEGACY` 对键盘 TLC 生效；若遥控器仍泄漏按键，可在 settings 里打开 `useKeyboardHook`（低级键盘钩子兜底，后续版本接入）。
- 未做代码签名；首次运行 SmartScreen 可能拦截，右键 → 属性 → 解除锁定。
- 系统音量键无法被完全拦截（见上）。

## 自检

`MiRemote.exe --self-test` 应输出 `[SelfTest] all scenarios passed`。
