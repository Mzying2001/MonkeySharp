@echo off
setlocal DisableDelayedExpansion
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-Packages.ps1" %*
exit /b %errorlevel%
