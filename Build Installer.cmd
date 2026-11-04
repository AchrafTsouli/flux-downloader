@echo off
setlocal
where pwsh.exe >nul 2>&1
if errorlevel 1 (
  echo PowerShell 7.4 or newer is required. Install it, then run this file again.
  pause
  exit /b 1
)
pwsh.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Build-LocalInstaller.ps1"
set "APEX_BUILD_EXIT=%ERRORLEVEL%"
echo.
if not "%APEX_BUILD_EXIT%"=="0" echo Build failed. Read the error above and the log in installers.
pause
exit /b %APEX_BUILD_EXIT%
