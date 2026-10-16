@echo off
if "%~1"=="" (
    echo Usage: UpdateInstalledVersion.cmd -Destination "D:\Apps\CodexQuota-Source" [-Launch]
    echo For a binary-only install or update, use Install.ps1 instead.
    pause
    exit /b 2
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0UpdateInstalledVersion.ps1" %*
if errorlevel 1 pause
