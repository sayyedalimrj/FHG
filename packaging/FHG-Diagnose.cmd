@echo off
setlocal EnableExtensions DisableDelayedExpansion
rem FHG read-only diagnostics. No global PowerShell execution-policy change.
rem Review the bundled FHG-Diagnose.ps1 before using this helper.
if not exist "%~dp0FHG-Diagnose.ps1" (
  echo Missing FHG-Diagnose.ps1. Extract the complete FHG package.
  pause
  exit /b 2
)
echo FHG connection diagnostics - current Windows user, no Administrator required.
echo This uses a process-only PowerShell policy override. No machine policy is changed.
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0FHG-Diagnose.ps1"
set "RESULT=%ERRORLEVEL%"
echo.
echo FHG diagnostic finished with exit code %RESULT%.
pause
exit /b %RESULT%
