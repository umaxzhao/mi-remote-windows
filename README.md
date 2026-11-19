# MiRemote for Windows

小米蓝牙遥控器控制台的 Windows 移植。参考项目：[godarrenw/mi_remote_control](https://github.com/godarrenw/mi_remote_control)，本地原版保存在 `../mi_remote_control`。采用 C#、.NET 9 和 WPF，无第三方 NuGet 依赖。

## 构建与运行

```powershell
.\build.ps1
.\src\MiRemote.App\bin\Release\net9.0-windows\MiRemote.exe
```

开发时：

```powershell
dotnet run --project src\MiRemote.App\MiRemote.App.csproj
dotnet run --project src\MiRemote.App\MiRemote.App.csproj -- --self-test
```

关闭设置窗口后程序保留在系统托盘。双击托盘图标可重新打开；托盘菜单可暂停或退出。程序只允许运行一个实例。

## 界面与交互

映射页参考 macOS 原版的遥控器与动作编辑布局。点击实体按键位置选择要编辑的键，通过动作菜单配置短按、长按、双击；OK 可配置方向手势，高级区可配置三个功能层。

快捷键通过录制面板直接按键输入，确认后保存；取消不修改原绑定。动作和 Profile 覆盖保存到 `%APPDATA%\MiRemote\config.json`。Windows Profile 使用小写进程名（不带 `.exe`），如 `chrome`、`code` 和 `windowsterminal`。

JSON 动作沿用原版 `type` 结构。Windows 修饰键为 `ctrl`、`alt`、`shift`、`win`，并接受部分原版别名。基础层的方向、返回与确认语义由引擎保护，应用控制动作应配置在高级层。

## 语音输入

语音键支持“按住说话、松开结束”。Windows 下通过 Interception 驱动注入右 Alt，触发豆包输入法的语音功能。

### 遥控器麦克风

已移植 ATVV 麦克风链路：MiRemote 通过 BLE GATT 订阅遥控器 ATVV 服务，接收 IMA ADPCM 音频并解码为 16kHz PCM，实时输出到 VB-CABLE 的 `CABLE Input`。

使用前需要：

1. 安装 [VB-CABLE](https://vb-audio.com/Cable/)；
2. 在豆包输入法语音设置中把麦克风选为 `CABLE Output (VB-Audio Virtual Cable)`。

这样长按遥控器语音键时，豆包使用的就是遥控器内置麦克风。音频同时保留一份 `%APPDATA%\MiRemote\remote-mic.wav` 供排查。

- 触发方式：`doubao`（驱动注入右 Alt）/ `dictation`（Win+H）/ `none`
- 触发模式：`hold`（按住说话，松开结束）/ `tap`
- 配置字段：`settings.voiceTriggerKey`、`voiceTriggerMode`、`voiceFallback`

### 音量键限制

当前型号的遥控器音量键不会出现在 Windows Raw Input、HID 报告或 ATVV 控制通道中，因此无法由应用接管。可以改用菜单键打开系统功能菜单执行音量操作。

豆包输入法会过滤普通 `SendInput` 注入，因此使用 Interception 驱动通道。驱动相关许可见 `thirdparty/Interception/README.md`。

## 实现与验证范围

- 状态机包括短按、长按、双击、OK 手势、临时层、切换层和空闲退出；`--self-test` 验证逻辑与配置序列化。
- Windows 操作使用 SendInput、Raw Input、WinRT BLE 与 Win32 窗口 API。
- 遥控器默认标识为 VID `0x2717` / PID `0x32B8`，可在配置修改。
- Raw Input 按键监听不对整个键盘类别注册 NOLEGACY，避免设置窗口和快捷键录制收不到键盘输入。

硬件按键格式、独占拦截、蓝牙重连及系统特殊按键仍需要真实遥控器测试。Raw Input 监听本身不能保证吞掉系统原有按键，不能据逻辑自检推断真机全部可用。

## 许可

派生实现遵循原项目 MIT 许可，原作者与许可文本见 LICENSE。
