param(
    [ValidateSet('Basic', 'Visibility', 'LOD', 'TwoWorlds')]
    [string[]] $Mode = @('Basic', 'Visibility', 'LOD', 'TwoWorlds'),
    [string] $PlayerPath = 'Artifacts/Builds/Stage3/AnimationSystemDOTS.exe',
    [switch] $Visible
)

$ErrorActionPreference = 'Stop'
$taskProjectRoot = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathRooted($PlayerPath)) { $PlayerPath = Join-Path $taskProjectRoot $PlayerPath }
$taskPlayerPath = (Resolve-Path -LiteralPath $PlayerPath).Path
$taskStamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$taskResults = @()
$taskCases = @{
    Basic = @('-animationCapture', 'capture.json')
    Visibility = @('-animationCaptureVisibility', 'visibility.json')
    LOD = @('-animationCaptureLOD', 'lod.json')
    TwoWorlds = @('-animationCaptureTwoWorlds', 'two-worlds.json')
}

foreach ($taskMode in $Mode) {
    $taskOutput = Join-Path $taskProjectRoot "Artifacts/Verification/Stage3/Acceptance-$taskStamp/$taskMode"
    New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
    $taskCase = $taskCases[$taskMode]
    $taskArguments = @($taskCase[0], ('"{0}"' -f $taskOutput), '-logFile',
        ('"{0}"' -f (Join-Path $taskOutput 'Player.log')), '-screen-width', '1024',
        '-screen-height', '576', '-screen-fullscreen', '0', '-force-d3d12')
    $taskStyle = if ($Visible) { 'Normal' } else { 'Hidden' }
    $taskProcess = Start-Process -FilePath $taskPlayerPath -ArgumentList $taskArguments `
        -WorkingDirectory $taskProjectRoot -WindowStyle $taskStyle -PassThru
    if (-not $taskProcess.WaitForExit(120000)) {
        Stop-Process -Id $taskProcess.Id
        throw "$taskMode timed out after 120 seconds. Log: $taskOutput/Player.log"
    }
    $taskExitCode = $taskProcess.ExitCode
    $taskReportPath = Join-Path $taskOutput $taskCase[1]
    if (-not (Test-Path -LiteralPath $taskReportPath)) { throw "$taskMode produced no report. Log: $taskOutput/Player.log" }
    $taskResults += [pscustomobject]@{ Mode = $taskMode; ExitCode = $taskExitCode; Report = $taskReportPath }
    Write-Output "$taskMode : exit $taskExitCode; $taskReportPath"
}

$taskResults | ConvertTo-Json | Set-Content -LiteralPath (Join-Path (Split-Path $taskOutput -Parent) 'results.json') -Encoding UTF8
if ($taskResults.Where({ $_.ExitCode -ne 0 }).Count -gt 0) {
    if ($taskResults.Where({ $_.Mode -eq 'TwoWorlds' -and $_.ExitCode -eq 4 }).Count -gt 0) {
        Write-Warning 'Two rendering Worlds are not independent in stock Entities Graphics 6.6. See two-worlds.json and RenderingChecks.md.'
    }
    exit 1
}
