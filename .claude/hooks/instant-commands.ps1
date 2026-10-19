<#
  UserPromptSubmit hook - answer /deploy and /run on the spot, with no model turn.

  /deploy starts deploy.bat in its own console window (a clean build published to
  C:\Apps\UnitySpace, plus the Desktop shortcut) and returns at once; /run starts the
  published copy. Both block the prompt, so no tool call is made and the orchestrator's pace
  gate never holds them. Any other prompt passes through untouched.
#>
$ErrorActionPreference = 'Stop'

$raw = [Console]::In.ReadToEnd()
try { $j = $raw | ConvertFrom-Json } catch { $j = $null }
$prompt = if ($j -and $j.prompt) { ([string]$j.prompt).Trim() } else { '' }

if ($prompt -notmatch '^/(deploy|run)(\s|$)') { Write-Output '{}'; exit 0 }
$command = $Matches[1].ToLower()

$repoRoot = if ($env:CLAUDE_PROJECT_DIR) { $env:CLAUDE_PROJECT_DIR } else { (Get-Location).Path }
$exe = 'C:\Apps\UnitySpace\UnitySpace.exe'

function Block([string]$reason) {
  $json = @{ decision = 'block'; reason = $reason } | ConvertTo-Json -Compress
  Write-Output $json
  exit 0
}

try {
  if ($command -eq 'deploy') {
    # Flags after /deploy (e.g. -Launch) pass through to tools\deploy.ps1.
    $flags = ($prompt -replace '^/deploy\s*', '').Trim()
    $bat = Join-Path $repoRoot 'deploy.bat'
    if ($flags) { Start-Process -FilePath $bat -ArgumentList $flags -WorkingDirectory $repoRoot }
    else        { Start-Process -FilePath $bat -WorkingDirectory $repoRoot }
    Block "Deploy started in its own window: clean build, publish to C:\Apps\UnitySpace, Desktop shortcut. /run starts it when done."
  }

  if (-not (Test-Path $exe)) { Block "Not deployed yet ($exe is missing). Run /deploy first." }
  Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe)
  Block "UnitySpace started from C:\Apps\UnitySpace."
} catch {
  Block "/$command failed: $($_.Exception.Message)"
}
