$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$startup = [Environment]::GetFolderPath('Startup')
$shortcutPath = Join-Path $startup 'Taskbar Screen Indicator.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $root 'start_taskbar_screen_indicator.vbs'
$shortcut.WorkingDirectory = $root
$shortcut.Description = 'Shows each taskbar app group on its active display.'
$shortcut.Save()
Write-Host $shortcutPath
