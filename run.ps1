$ErrorActionPreference = 'Stop'
$taskExe = Join-Path $PSScriptRoot 'dist\MapleStoryClassicUIHelper.exe'
if (-not (Test-Path -LiteralPath $taskExe)) { & (Join-Path $PSScriptRoot 'build.ps1') -Publish }
Start-Process -FilePath $taskExe
