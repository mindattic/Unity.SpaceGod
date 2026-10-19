#Requires -Version 5.1
<#
.SYNOPSIS
    Shuts down any running UnitySpace, clears the build output, rebuilds the Windows player
    from scratch (ProjectSetup.BuildWindows — this project has no separate dev/release split; its
    one build already uses BuildOptions.None), and publishes a standalone copy to
    C:\Apps\UnitySpace\ — same process UnityEve/Automata/KdpPublish/IdiotProof use to always
    have a fresh, independent deployed copy instead of a possibly-stale one.

.DESCRIPTION
    1. Stops any running UnitySpace.exe process (dev copy or deployed copy).
    2. Removes Builds\Windows — the build OUTPUT only, never Library\ (Unity's asset
       import / script-compile cache, which survives exactly as it does for a normal
       Editor rebuild) — so the player build below is never a stale incremental copy.
    3. Runs Unity in batch mode (-executeMethod UnitySpace.EditorTools.ProjectSetup.SetupAndBuild,
       requires the Editor closed) -> Builds\Windows\UnitySpace.exe.
    4. Copies Builds\Windows\* -> C:\Apps\UnitySpace\.

.PARAMETER Launch
    After publishing, immediately run the deployed UnitySpace.exe.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\deploy.ps1
    powershell -ExecutionPolicy Bypass -File tools\deploy.ps1 -Launch
#>
param([switch]$Launch)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoDir  = Split-Path $PSScriptRoot   # tools\ -> UnitySpace\
$out      = 'C:\Apps\UnitySpace'
$exeName  = 'UnitySpace.exe'
$buildDir = Join-Path $repoDir 'Builds\Windows'

# ── Resolve Unity editor ────────────────────────────────────────────────────
$ver = (Select-String -Path "$repoDir/ProjectSettings/ProjectVersion.txt" -Pattern "m_EditorVersion: (.+)").Matches[0].Groups[1].Value.Trim()
$unity = "C:\Program Files\Unity\Hub\Editor\$ver\Editor\Unity.exe"
if (-not (Test-Path $unity)) { throw "Unity $ver not installed at $unity" }
if (Get-Process -Name Unity -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*UnitySpace*" }) {
    throw "UnitySpace is open in the Unity Editor. Close it (File > Quit) and re-run."
}

# ── Stop running instance ──────────────────────────────────────────────────
Write-Host ''
Write-Host '  Stopping running instance...' -ForegroundColor Yellow
$procs = Get-Process 'UnitySpace' -ErrorAction SilentlyContinue
if ($procs) {
    $procs | Stop-Process -Force
    Write-Host "    Stopped ($(@($procs).Count) process(es))" -ForegroundColor DarkYellow
    Start-Sleep -Milliseconds 800
} else {
    Write-Host '    Nothing running.' -ForegroundColor DarkGray
}

# ── Clear build output ───────────────────────────────────────────────────────
# Builds\Windows only — never Library\, which is Unity's import/compile cache, not
# build output, and must survive a redeploy exactly as it survives a normal rebuild.
Write-Host ''
Write-Host '  Clearing build output (Builds\Windows)...' -ForegroundColor Cyan
if (Test-Path $buildDir) {
    Remove-Item -Recurse -Force $buildDir
}

# ── Build ────────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '  Building Windows player (Unity batch mode)...' -ForegroundColor Cyan
New-Item -ItemType Directory -Force "$repoDir/Logs" | Out-Null
$logFile = "$repoDir/Logs/deploy-build.log"
# Start-Process joins these with bare spaces, so paths are quoted here or a space in one would split it.
$uargs = @("-batchmode", "-quit", "-projectPath", "`"$repoDir`"", "-executeMethod", "UnitySpace.EditorTools.ProjectSetup.SetupAndBuild", "-logFile", "`"$logFile`"")
$p = Start-Process -FilePath $unity -ArgumentList $uargs -Wait -PassThru -NoNewWindow
$errors = Select-String -Path $logFile -Pattern "error CS\d+" -ErrorAction SilentlyContinue | Select-Object -First 40
if ($errors) { $errors | ForEach-Object { Write-Host $_.Line -ForegroundColor Red } }
if ($p.ExitCode -ne 0) {
    Write-Host "Build failed (exit $($p.ExitCode)). Log: $logFile" -ForegroundColor Red
    exit $p.ExitCode
}

$builtExe = Join-Path $buildDir $exeName
if (-not (Test-Path $builtExe)) {
    Write-Host "Build completed but $exeName not found at $builtExe." -ForegroundColor Red
    exit 1
}

# ── Publish ─────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host "  Publishing $builtExe" -ForegroundColor Cyan
Write-Host "    -> $out\$exeName"

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item -Recurse -Force (Join-Path $buildDir '*') $out

$exe = Join-Path $out $exeName
if (-not (Test-Path $exe)) {
    Write-Host "Publish completed but $exeName not found at $exe." -ForegroundColor Red
    exit 1
}

# ── Desktop shortcut ─────────────────────────────────────────────────────────
# Points at the published copy, so double-clicking it (or /run) always plays the last deploy.
# GetFolderPath, not $HOME\Desktop: OneDrive may have moved the Desktop folder.
$shortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'UnitySpace.lnk'
$link = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
$link.TargetPath       = $exe
$link.WorkingDirectory = $out
$link.IconLocation     = "$exe,0"
$link.Description      = 'UnitySpace (deployed copy in C:\Apps)'
$link.Save()
Write-Host "    Shortcut -> $shortcut" -ForegroundColor Gray

# ── Summary ────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '  Published successfully.' -ForegroundColor Green
Write-Host "    Exe : $exe ($([math]::Round(((Get-Item $exe).Length / 1MB), 1)) MB)" -ForegroundColor Gray
Write-Host ''

if ($Launch) {
    Write-Host '  Launching...' -ForegroundColor Cyan
    Start-Process $exe
}

# An explicit success code: nothing above sets $LASTEXITCODE (Unity runs via Start-Process), so a
# caller checking it (launch.ps1) would otherwise read an empty value as a failure.
exit 0
