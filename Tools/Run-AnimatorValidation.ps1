param(
    [ValidateSet('Player', 'Bots')][string] $Scene = 'Bots',
    [string] $PlayerPath = 'Artifacts/Builds/AnimatorValidation/AnimationSystemDOTS.exe'
)
$ErrorActionPreference = 'Stop'
$validationProject = (Split-Path -Parent $PSScriptRoot)
if (-not [IO.Path]::IsPathRooted($PlayerPath)) { $PlayerPath = Join-Path $validationProject $PlayerPath }
$validationPlayer = (Resolve-Path -LiteralPath $PlayerPath).Path
$validationOutput = Join-Path $validationProject ('Artifacts/Verification/AnimatorValidation/' + $Scene + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $validationOutput -Force | Out-Null
$validationSceneIndex = if ($Scene -eq 'Bots') { '1' } else { '0' }
$validationArguments = @('-animatorValidation', ('"{0}"' -f $validationOutput), '-animatorScene', $validationSceneIndex,
    '-logFile', ('"{0}"' -f (Join-Path $validationOutput 'Player.log')), '-screen-width', '1280', '-screen-height', '720', '-screen-fullscreen', '0', '-force-d3d12')
# This is an interactive graphics check: a minimized/hidden swapchain can skip
# rendering. Keep the verification Player visible until it captures and exits.
$validationProcess = Start-Process -FilePath $validationPlayer -ArgumentList $validationArguments -WorkingDirectory $validationProject -WindowStyle Normal -PassThru
$validationDeadline = [DateTime]::UtcNow.AddMinutes(3)
while (-not $validationProcess.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -gt $validationDeadline) { Stop-Process -Id $validationProcess.Id; throw "Verification timed out: $validationOutput/Player.log" }
}
$validationReportPath = Join-Path $validationOutput 'report.json'
if (-not (Test-Path -LiteralPath $validationReportPath)) { throw "No report: $validationOutput/Player.log" }
$validationReport = Get-Content -LiteralPath $validationReportPath -Raw | ConvertFrom-Json
Write-Output ("{0}: exit={1}; actors={2}; median={3:N2}ms; p95={4:N2}ms; report={5}" -f $Scene, $validationProcess.ExitCode, $validationReport.actors, $validationReport.medianMs, $validationReport.p95Ms, $validationReportPath)
if ($validationProcess.ExitCode -ne 0 -or -not $validationReport.passed) { throw "Verification failed: $validationReportPath" }
