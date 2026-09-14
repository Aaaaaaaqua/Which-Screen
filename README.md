# 任务栏屏幕标记

~~现在还很粗糙，上班心血来潮时会更新一下子。另外，所有的代码和技术原理都是Astra大人负责的~~

这是一个 Windows 后台程序，会在任务栏每个正在运行的应用图标下方显示所在屏幕的颜色色带：

- 蓝色：主屏幕
- 橙色：右侧屏幕
- 双色：同一应用的窗口同时分布在多个屏幕

色带覆盖主任务栏和副屏任务栏，并且鼠标穿透，不影响任务栏原本的点击、右键与拖动。程序已配置为当前用户登录后自动启动。

首次构建或代码更新后，在 PowerShell 中执行：

```powershell
Set-Location "C:\Users\25139\Desktop\new thought"
.\build_taskbar_screen_indicator.ps1
```

平时启动程序时执行：

```powershell
Start-Process ".\TaskbarScreenIndicator\TaskbarScreenIndicator.exe" -WorkingDirectory ".\TaskbarScreenIndicator"
```

双击通知区域图标可打开设置，也可以右键图标进行开关、刷新或退出。

设置窗口支持为每块屏幕单独选择色带颜色、开关任务栏标记，以及调整窗口移动后的刷新速度。设置会保存到当前 Windows 用户并在下次启动时恢复。

Windows 会把同一个应用的多个窗口合并成一个任务栏按钮，因此它无法为同一应用的每一个窗口分别显示颜色。跨屏窗口用双色条表示该应用在多个屏幕都有窗口。
