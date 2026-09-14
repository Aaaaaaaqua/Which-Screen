# 任务栏屏幕标记

后台运行后，它会在每个任务栏程序图标的下方绘制一条细色带：每种颜色对应一个屏幕。窗口跨多个屏幕时，图标下方的色带会分段显示。色带是鼠标穿透的，不影响任务栏图标原有的点击、右键或拖动。

已配置为当前用户登录后自动后台启动。首次构建或代码更新后，在 PowerShell 中执行：

```powershell
Set-Location "C:\Users\25139\Desktop\new thought"
.\build_taskbar_screen_indicator.ps1
```

平时启动程序时执行：

```powershell
Start-Process ".\TaskbarScreenIndicator\TaskbarScreenIndicator.exe" -WorkingDirectory ".\TaskbarScreenIndicator"
```

双击通知区域图标可打开设置，右键菜单提供开关、立即刷新和退出功能。

默认主屏幕为蓝色，右侧屏幕为橙色。设置窗口支持为每块屏幕自定义颜色、开关标记以及调整刷新速度；配置保存在当前 Windows 用户下。显示器布局改变后会自动刷新。
