@echo off
set "APP=%~dp0TaskbarScreenIndicator\TaskbarScreenIndicator.exe"
if not exist "%APP%" (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build_taskbar_screen_indicator.ps1"
  exit /b %errorlevel%
)
start "" /min "%APP%"
