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

echo God2 Semantic Recovery Engine - Ultimate Live 元件驗證
echo 請保持此視窗開啟，並依 README-ULTIMATE-LIVE-zh-TW.txt 操作。
echo 若目前不是系統管理員，封裝 EXE 會顯示 Windows UAC 提示；請核對後允許。
echo 成功狀態只限 Deep Recovery 元件證據，不代表全域 Ultimate PASS。
"%ENGINE%" --internal-ultimate-live-validation --package-root "%~dp0"
set "RESULT=%ERRORLEVEL%"

:done
echo.
echo 驗證程序結束碼：%RESULT%
if /I not "%GOD2_ULTIMATE_NO_PAUSE%"=="1" pause
exit /b %RESULT%
