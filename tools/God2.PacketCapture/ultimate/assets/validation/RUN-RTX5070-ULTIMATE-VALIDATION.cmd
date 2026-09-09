@echo off
setlocal EnableExtensions DisableDelayedExpansion
chcp 65001 >nul
cd /d "%~dp0"

set "ENGINE=%~dp0God2SemanticRecoveryEngine.exe"
if not exist "%ENGINE%" (
  echo [錯誤] 找不到 God2SemanticRecoveryEngine.exe。
  set "RESULT=20"
  goto :done
)

echo God2 Semantic Recovery Engine - RTX 5070 Ultimate 實機驗證
"%ENGINE%" --internal-rtx5070-validation --package-root "%~dp0"
set "RESULT=%ERRORLEVEL%"
if "%RESULT%"=="50" echo [部分證據] 6 項 operation-specific GPU 已精確驗證；10 項結構化工作與 AI 仍受阻，禁止提升 Ultimate。

:done
echo.
echo 驗證程序結束碼：%RESULT%
if /I not "%GOD2_ULTIMATE_NO_PAUSE%"=="1" pause
exit /b %RESULT%
