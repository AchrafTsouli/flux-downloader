@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-BrowserBridge.ps1" -InstallDirectory "%~dp0"
pause
