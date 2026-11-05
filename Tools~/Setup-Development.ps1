param(
    [ValidateSet('UPM', 'Assets')][string] $Mode = 'UPM',
    [string] $ProjectPath,
    [switch] $SamplesOnly
)
$ErrorActionPreference = 'Stop'
$devRepo = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
$devProject = if ($ProjectPath) { [IO.Path]::GetFullPath($ProjectPath) } else { Join-Path $devRepo 'DevProject~' }
$devAssets = Join-Path $devProject 'Assets'
New-Item -ItemType Directory -Path $devAssets -Force | Out-Null
function Mount-Source([string] $Name, [string] $Target, [string] $Meta) {
    $mountPath = Join-Path $devAssets $Name
    if (Test-Path -LiteralPath $mountPath) {
        $mount = Get-Item -LiteralPath $mountPath -Force
        if ($mount.LinkType -ne 'Junction' -or [IO.Path]::GetFullPath($mount.Target[0]) -ne [IO.Path]::GetFullPath($Target)) {
            throw "Refusing to replace existing assets: $mountPath"
        }
    } else { New-Item -ItemType Junction -Path $mountPath -Target $Target | Out-Null }
    if ($Meta -and -not (Test-Path -LiteralPath "$mountPath.meta")) { Copy-Item -LiteralPath $Meta -Destination "$mountPath.meta" }
}
Mount-Source 'AnimationSystemDOTS.Sandbox' (Join-Path $devRepo 'Samples~\Sandbox') (Join-Path $devRepo 'Samples~\Sandbox.meta')
if ($SamplesOnly) { Write-Output "Live sample scenes: $devAssets/AnimationSystemDOTS.Sandbox"; return }
Mount-Source 'AnimationSystemDOTS.Tests' (Join-Path $devRepo 'Tests~') ''
$devManifest = Join-Path $devProject 'Packages\manifest.json'
$devJson = Get-Content -LiteralPath $devManifest -Raw | ConvertFrom-Json
$devName = 'com.vertoker.animation-system-dots'
$devJson.dependencies.PSObject.Properties.Remove($devName)
$devPackageMount = Join-Path $devAssets 'AnimationSystemDOTS'
if ($Mode -eq 'UPM') {
    if (Test-Path -LiteralPath $devPackageMount) {
        $mount = Get-Item -LiteralPath $devPackageMount -Force
        if ($mount.LinkType -ne 'Junction' -or [IO.Path]::GetFullPath($mount.Target[0]) -ne $devRepo) { throw 'Unexpected package Assets folder; refusing to replace it.' }
        # Delete only this verified junction, never its source directory.
        [IO.Directory]::Delete($devPackageMount)
        if (Test-Path -LiteralPath "$devPackageMount.meta") { Remove-Item -LiteralPath "$devPackageMount.meta" -Force }
    }
    $devDependency = if ($ProjectPath) { 'file:' + $devRepo.Replace('\', '/') } else { 'file:../..' }
    $devJson.dependencies | Add-Member -NotePropertyName $devName -NotePropertyValue $devDependency
} else {
    Mount-Source 'AnimationSystemDOTS' $devRepo ''
}
$devText = $devJson | ConvertTo-Json -Depth 20
[IO.File]::WriteAllText($devManifest, $devText + [Environment]::NewLine, (New-Object Text.UTF8Encoding $false))
Write-Output "Development project ($Mode): $devProject"
Write-Output 'Runtime, scenes and tests are live repository sources. Open this project in Unity 6000.6.8f1.'
