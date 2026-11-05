param(
    [ValidateSet('Capture', 'Profile')]
    [string] $Mode = 'Capture',
    [string] $PlayerPath = 'Artifacts/Builds/HumanoidCrowd/AnimationSystemDOTS.exe'
)
$ErrorActionPreference = 'Stop'
$taskProjectRoot = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathRooted($PlayerPath)) { $PlayerPath = Join-Path $taskProjectRoot $PlayerPath }
$taskPlayerPath = (Resolve-Path -LiteralPath $PlayerPath).Path
$taskStamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$taskOutput = Join-Path $taskProjectRoot "Artifacts/Verification/HumanoidCrowd/$Mode-$taskStamp"
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskFlag = if ($Mode -eq 'Profile') { '-humanoidCrowdProfile' } else { '-humanoidCrowdCapture' }
$taskArguments = @($taskFlag, ('"{0}"' -f $taskOutput), '-logFile',
    ('"{0}"' -f (Join-Path $taskOutput 'Player.log')), '-screen-width', '1280',
    '-screen-height', '720', '-screen-fullscreen', '0', '-force-d3d12')
# This is an interactive graphical verification: a hidden/minimized swapchain
# can skip rendering entirely and cannot establish real scene performance.
$taskProcess = Start-Process -FilePath $taskPlayerPath -ArgumentList $taskArguments `
    -WorkingDirectory $taskProjectRoot -WindowStyle Normal -PassThru
$taskDeadline = [DateTime]::UtcNow.AddSeconds(120)
while (-not $taskProcess.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -gt $taskDeadline) {
        Stop-Process -Id $taskProcess.Id
        throw "Crowd verification timed out. Log: $taskOutput/Player.log"
    }
}
$taskReportName = if ($Mode -eq 'Profile') { 'profile.json' } else { 'humanoid-crowd.json' }
$taskReportPath = Join-Path $taskOutput $taskReportName
if (-not (Test-Path -LiteralPath $taskReportPath)) { throw "No report. Log: $taskOutput/Player.log" }
$taskReport = Get-Content -LiteralPath $taskReportPath -Raw | ConvertFrom-Json
Write-Output "$Mode : exit $($taskProcess.ExitCode); $taskReportPath"
if ($taskProcess.ExitCode -ne 0) { throw "Crowd verification failed. Inspect $taskReportPath" }
if ($Mode -eq 'Capture' -and -not $taskReport.passed) { throw 'Capture predicates failed.' }
if ($Mode -eq 'Profile' -and ($taskReport.characters -ne 1000 -or $taskReport.phases[0].triangles -le 0)) {
    throw 'Invalid benchmark: requires 1000 characters and real rendered geometry.'
}
