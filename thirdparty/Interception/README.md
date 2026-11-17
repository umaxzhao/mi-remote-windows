# Interception

本项目可选用 [oblitum/Interception](https://github.com/oblitum/Interception) 的键盘驱动，让会过滤 `SendInput` 注入按键的应用（如豆包输入法）也能由遥控器触发语音输入。

- 版本：v1.0.1，官方发布包 SHA256：`AD038963D6413055765128B0B931F6E765147C9916DBA79E65D872B261F9AF10`
- 驱动与库源代码：https://github.com/oblitum/Interception
- 发布包：https://github.com/oblitum/Interception/releases/tag/v1.0.1
- 本项目通过动态链接调用 `interception.dll`，未修改驱动或库源码。
- 非商业使用许可见 `LICENSE-LGPL3.txt`；商业使用需另行查阅作者的 commercial-usage 许可。

安装：在应用“语音”页点击“安装语音驱动”，或以管理员身份运行 `scripts/install-driver.ps1`。安装时 Windows 会请求管理员确认。未安装驱动时应用仍使用普通模拟按键，不要求所有用户安装。

安装完成后重启 MiRemote。若驱动可用，语音日志会显示 `(driver)`。
