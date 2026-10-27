@echo off
rem Desktop shortcut target. Asks first: 1) Deploy & Run pulls latest, rebuilds even when nothing
rem changed (-Force), publishes to C:\Apps\Unity.SpaceGod, refreshes the Desktop shortcut and launches
rem it (tools\run.ps1 does the work); 2) Run starts the published copy without deploying.
rem "run.bat --deploy [flags]" skips the menu and deploys without -Force (the /deploy hook);
rem any other arguments pass through to tools\run.ps1 when deploying.
setlocal
set "EXE=C:\Apps\Unity.SpaceGod\Unity.SpaceGod.exe"
set "FORCE=-Force"
set "ARGS=%*"
if /i not "%~1"=="--deploy" goto menu
set "FORCE="
set "ARGS="
shift
:collect
if "%~1"=="" goto deploy
set "ARGS=%ARGS% %1"
shift
goto collect

:menu
echo.
echo   Unity.SpaceGod
echo     1^) Deploy ^& Run
echo     2^) Run
echo.
choice /c 12 /n /m "  Choose 1 or 2: "
if errorlevel 2 goto run

:deploy
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\run.ps1" -Launch %FORCE% %ARGS%
if errorlevel 1 (
    echo.
    echo   Deploy failed. See Logs\deploy-build.log.
    pause
    exit /b 1
)
timeout /t 5
exit /b 0

:run
if not exist "%EXE%" (
    echo.
    echo   Unity.SpaceGod is not deployed yet ^(%EXE% is missing^) - deploying first.
    goto deploy
)
start "" /d "C:\Apps\Unity.SpaceGod" "%EXE%"
exit /b 0
