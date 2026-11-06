param(
    [ValidateSet('UPM', 'Assets', 'Git')][string] $Mode = 'UPM',
    [string] $Revision = 'HEAD',
    [string] $UnityPath = 'C:\Unity\Editor\6000.6.8f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$probeRoot = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
# Keep generated project paths short: some Unity package importers still hit
# Windows MAX_PATH for their long internal asset names.
$probeProject = Join-Path $probeRoot ('.superpowers\consumers\' + $Mode.Substring(0, 1) + (Get-Date -Format 'HHmmss'))
foreach ($part in @('Assets\Editor', 'Packages', 'ProjectSettings')) { New-Item -ItemType Directory -Path (Join-Path $probeProject $part) -Force | Out-Null }
$probePackageRoot = Join-Path $probeRoot 'Assets\AnimationSystemDOTS'
$probePackage = Get-Content -LiteralPath (Join-Path $probePackageRoot 'package.json') -Raw | ConvertFrom-Json
$probeDependencies = [ordered]@{}
foreach ($dependency in $probePackage.dependencies.PSObject.Properties) { $probeDependencies[$dependency.Name] = $dependency.Value }
$probeDependencies['com.unity.render-pipelines.universal'] = '17.6.0'
# Template engine modules, with no project-specific plugins or test fixtures.
$probeDevManifest = Get-Content -LiteralPath (Join-Path $probeRoot 'Packages\manifest.json') -Raw | ConvertFrom-Json
foreach ($dependency in $probeDevManifest.dependencies.PSObject.Properties) {
    if ($dependency.Name.StartsWith('com.unity.modules.')) { $probeDependencies[$dependency.Name] = $dependency.Value }
}
if ($Mode -eq 'UPM') { $probeDependencies[$probePackage.name] = 'file:' + $probePackageRoot.Replace('\', '/') }
elseif ($Mode -eq 'Git') {
    $probeCommit = git -C $probeRoot rev-parse --verify ($Revision + '^{commit}')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve consumer revision' }
    $probeGitUri = ([Uri]($probeRoot + '\.git')).AbsoluteUri
    $probeDependencies[$probePackage.name] = $probeGitUri + '?path=/Assets/AnimationSystemDOTS#' + $probeCommit
}
else {
    $probeDestination = Join-Path $probeProject 'Assets\AnimationSystemDOTS'
    New-Item -ItemType Directory -Path $probeDestination -Force | Out-Null
    Get-ChildItem -LiteralPath $probePackageRoot -Force | Copy-Item -Destination $probeDestination -Recurse
}
$probeManifest = @{dependencies = $probeDependencies} | ConvertTo-Json -Depth 20
[IO.File]::WriteAllText((Join-Path $probeProject 'Packages\manifest.json'), $probeManifest, (New-Object Text.UTF8Encoding $false))
foreach ($part in @('ProjectVersion.txt','GraphicsSettings.asset','QualitySettings.asset','EditorSettings.asset')) {
    Copy-Item -LiteralPath (Join-Path $probeRoot "ProjectSettings\$part") -Destination (Join-Path $probeProject 'ProjectSettings')
}
$probeCode = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'ConsumerProbe.cs.template') -Raw).Replace('__MODE__', $(if ($Mode -eq 'Git') { 'UPM' } else { $Mode }))
[IO.File]::WriteAllText((Join-Path $probeProject 'Assets\Editor\ConsumerProbe.cs'), $probeCode, (New-Object Text.UTF8Encoding $false))
$probeProcess = Start-Process -FilePath $UnityPath -ArgumentList @('-projectPath', ('"{0}"' -f $probeProject), '-logFile', ('"{0}"' -f (Join-Path $probeProject 'Editor.log'))) -WorkingDirectory $probeProject -WindowStyle Hidden -PassThru
Write-Output "Consumer $Mode PID $($probeProcess.Id): $probeProject"
$probeDeadline = [DateTime]::UtcNow.AddMinutes(8)
while (-not $probeProcess.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -gt $probeDeadline) { Stop-Process -Id $probeProcess.Id; throw "Consumer $Mode timed out. Inspect $probeProject/Editor.log" }
}
$probeReportPath = Join-Path $probeProject 'consumer-result.json'
if (-not (Test-Path -LiteralPath $probeReportPath)) { throw "Consumer exited $($probeProcess.ExitCode) without a report. Inspect $probeProject/Editor.log" }
$probeReport = Get-Content -LiteralPath $probeReportPath -Raw | ConvertFrom-Json
Write-Output "$Mode : exit $($probeProcess.ExitCode); $probeReportPath"
if ($probeProcess.ExitCode -ne 0 -or -not $probeReport.passed) { throw "Consumer $Mode verification failed: $($probeReport.error)" }
