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

语音键支持“按住说话、松开结束”。默认绑定豆包输入法 Windows 版的语音快捷键 `right_alt`（右 Alt）。
如果改过豆包设置里的语音快捷键，在「语音」页把触发键改成同一组合即可，例如 `win+ctrl`。

- 触发方式：`doubao`（按住组合键说话）/ `dictation`（Win+H 系统听写）/ `none`（关闭）
- 触发模式：`hold`（按住说话，松开结束）/ `tap`（按一下开始，再按结束）

设置保存在 `%APPDATA%\MiRemote\config.json` 的 `settings.voiceTriggerKey`、`voiceTriggerMode`、`voiceFallback`。

注意：当前实现是模拟按键触发输入法自身的语音功能，使用电脑当前麦克风，不接收遥控器内置麦克风音频。
完整的 ATVV BLE 遥控器麦克风链路尚未移植。

豆包输入法可能过滤普通 `SendInput` 注入。应用提供可选的 Interception 驱动通道，在“语音”页点击“安装语音驱动”并按 UAC 提示确认即可。驱动相关许可见 `thirdparty/Interception/README.md`。

## 实现与验证范围

- 状态机包括短按、长按、双击、OK 手势、临时层、切换层和空闲退出；`--self-test` 验证逻辑与配置序列化。
- Windows 操作使用 SendInput、Raw Input 和 Win32 窗口 API。
- 遥控器默认标识为 VID `0x2717` / PID `0x32B8`，可在配置修改。
- Raw Input 按键监听不对整个键盘类别注册 NOLEGACY，避免设置窗口和快捷键录制收不到键盘输入。

硬件按键格式、独占拦截、蓝牙重连及系统特殊按键仍需要真实遥控器测试。Raw Input 监听本身不能保证吞掉系统原有按键，不能据逻辑自检推断真机全部可用。

完整 ATVV BLE 遥控器麦克风链路尚未移植。当前语音回退为 Win+H 系统听写，使用电脑当前的麦克风；它不接收遥控器内置麦克风音频。未完成部分会在界面中注明。

## 许可

派生实现遵循原项目 MIT 许可，原作者与许可文本见 LICENSE。
