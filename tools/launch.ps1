<#
.SYNOPSIS
  Deploys the current build to C:\Apps\UnitySolarSystem, then offers Normal Play, Demo +
  Capture, or Quit.
.NOTES
  Always redeploys from source first (tools\deploy.ps1), so this never runs a stale build.
  Requires the Unity Editor closed for this project. Capture needs ffmpeg on PATH and the
  MindAttic.Capture library compiled in (see README.md).
#>
$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "deploy.ps1")
if ($LASTEXITCODE -ne 0) {
    Write-Host "Deploy failed (exit $LASTEXITCODE); not launching." -ForegroundColor Red
    exit $LASTEXITCODE
}

$exe = "C:\Apps\UnitySolarSystem\UnitySolarSystem.exe"
if (-not (Test-Path $exe)) {
    Write-Host "Deploy reported success but $exe is missing." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "  UnitySolarSystem" -ForegroundColor Cyan
Write-Host "  [1] Normal Play"
Write-Host "  [2] Demo + Capture"
Write-Host "  [3] Quit"
Write-Host ""
$choice = Read-Host "Select (default 1)"
if ([string]::IsNullOrWhiteSpace($choice)) { $choice = "1" }

switch ($choice) {
    "1" { Start-Process -FilePath $exe }
    "2" { Start-Process -FilePath $exe -ArgumentList @("-demo", "-capture", "video", "screen", "5") }
    "3" { exit 0 }
    default {
        Write-Host "Unrecognized choice, launching Normal Play." -ForegroundColor Yellow
        Start-Process -FilePath $exe
    }
}
