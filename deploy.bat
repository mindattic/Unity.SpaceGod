@echo off
rem Pulls latest, builds Release only if the source changed, publishes to C:\Apps\Unity.SpaceGod,
rem refreshes the Desktop shortcut, and launches it (tools\deploy.ps1 does the work).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\deploy.ps1" -Launch %*
if errorlevel 1 (
    echo.
    echo   Deploy failed. See Logs\deploy-build.log.
    pause
    exit /b 1
)
timeout /t 5
