@echo off
setlocal

pushd "%~dp0" || exit /b 1

if not defined DOTNET_ROOT if exist "%USERPROFILE%\.dotnet\dotnet.exe" set "DOTNET_ROOT=%USERPROFILE%\.dotnet"
if defined DOTNET_ROOT set "PATH=%DOTNET_ROOT%;%PATH%"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Start-God2ClassicServer.ps1" %*
set "EXIT_CODE=%ERRORLEVEL%"

popd
exit /b %EXIT_CODE%
