$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sources = @(
  (Join-Path $root 'TaskbarScreenIndicator\TaskbarScreenIndicator.Legacy.cs'),
  (Join-Path $root 'TaskbarScreenIndicator\SettingsForm.Legacy.cs')
)
$output = Join-Path $root 'TaskbarScreenIndicator\TaskbarScreenIndicator.exe'
$manifest = Join-Path $root 'TaskbarScreenIndicator\TaskbarScreenIndicator.exe.manifest'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs = @(
  '/r:System.Windows.Forms.dll',
  '/r:System.Drawing.dll',
  '/r:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll',
  '/r:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\UIAutomationClient\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationClient.dll',
  '/r:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\UIAutomationTypes\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationTypes.dll'
)
& $csc '/nologo' '/target:winexe' ("/win32manifest:$manifest") ("/out:$output") $refs $sources
if ($LASTEXITCODE -ne 0) { throw "编译失败：$LASTEXITCODE" }
Start-Process -FilePath $output -WorkingDirectory (Split-Path $output)
Write-Host 'Taskbar screen indicator started.'
