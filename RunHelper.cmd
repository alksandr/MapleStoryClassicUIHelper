@echo off
if exist "%~dp0dist\MapleStoryClassicUIHelper.exe" (
  start "" "%~dp0dist\MapleStoryClassicUIHelper.exe"
) else (
  powershell.exe -NoProfile -File "%~dp0run.ps1"
  if errorlevel 1 pause
)
