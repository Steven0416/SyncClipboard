@echo off
"%~dp0setup\ClipBridge.Setup.exe" --pair "%~dp0client"
if errorlevel 1 pause
