@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-revit-2024.ps1" -WaitForRevit
echo.
echo Exit code: %ERRORLEVEL%
pause
