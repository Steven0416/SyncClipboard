@echo off
"%~dp0setup\ClipBridge.Setup.exe" --configure "%~dp0client"
if errorlevel 1 goto done
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0ClipBridge.ps1" -ConfigureStartup
:done
pause
