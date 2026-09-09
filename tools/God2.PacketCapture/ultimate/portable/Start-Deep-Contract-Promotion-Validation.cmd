@echo off
setlocal
cd /d "%~dp0"
set "PREFLIGHT_ARG="
if "%GOD2_PORTABLE_PREFLIGHT_ONLY%"=="1" set "PREFLIGHT_ARG=-PreflightOnly"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Invoke-Portable-Deep-Contract-Capture.ps1" -LauncherPath "%~1" %PREFLIGHT_ARG%
set "EXIT_CODE=%ERRORLEVEL%"
if not "%GOD2_PORTABLE_PREFLIGHT_ONLY%"=="1" pause
exit /b %EXIT_CODE%
