@echo off
setlocal
title God2PacketCapture RTX 5070 Physical Validation
cd /d "%~dp0"
echo.
echo God2PacketCapture RTX 5070 Physical Validation
echo ==================================================
echo No game login, CUDA Toolkit, PowerShell, or arguments are required.
echo Please wait. The benchmark can take several minutes.
echo.
"%~dp0God2PacketCapture.exe" --internal-rtx5070-validation --package-root "%~dp0."
set "VALIDATION_EXIT=%ERRORLEVEL%"
echo.
if "%VALIDATION_EXIT%"=="0" (
  echo Validation run completed. Return the generated RTX5070-VALIDATION-RESULT-*.zip file.
) else (
  echo Validation runner returned code %VALIDATION_EXIT%. Return any generated result ZIP for diagnosis.
)
echo.
pause
exit /b %VALIDATION_EXIT%
