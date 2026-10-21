@echo off
rem Clean-builds the Windows player, publishes it to C:\Apps\Unity.SpaceGod and refreshes the
rem "Unity.SpaceGod" Desktop shortcut (tools\deploy.ps1 does the work; flags such as -Launch pass through).
rem This is what /deploy runs; /run starts the published copy.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\deploy.ps1" %*
if errorlevel 1 (
    echo.
    echo   Deploy failed. See Logs\deploy-build.log.
    pause
    exit /b 1
)
timeout /t 5
