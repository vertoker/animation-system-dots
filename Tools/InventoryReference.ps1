param(
    [string]$SourcePath = 'Sources~/Rukhanka Animation System 2 _ Animation Tools _ Unity Asset Store.html',
    [string]$OutputPath = 'Documentation~/ReferenceInventory.md'
)
$ErrorActionPreference = 'Stop'
$html = Get-Content -Raw -Encoding utf8 -LiteralPath $SourcePath
$stack = @{}
$files = [Collections.Generic.List[string]]::new()
$pattern = '(?s)<h3[^>]*margin-left:\s*(\d+)px[^>]*>.*?</h3>|<button[^>]*margin-left:\s*(\d+)px[^>]*>.*?</button>'
foreach ($node in [regex]::Matches($html, $pattern)) {
    $margin = if ($node.Groups[1].Success) { [int]$node.Groups[1].Value } else { [int]$node.Groups[2].Value }
    $label = [Net.WebUtility]::HtmlDecode([regex]::Replace($node.Value, '<[^>]+>', '')).Trim()
    if ($node.Value -match 'ifont-folder') {
        $stack[$margin] = $label
        foreach ($key in @($stack.Keys)) { if ($key -gt $margin) { $stack.Remove($key) } }
    } elseif ($node.Value -match 'ifont-file-empty') {
        $parents = @($stack.Keys | Where-Object { $_ -lt $margin } | Sort-Object | ForEach-Object { $stack[$_] })
        $files.Add((($parents + @($label)) -join '/'))
    }
}
if ($files.Count -eq 0) { throw 'No visible paths found; the saved markup may have changed.' }
$sha = (Get-FileHash -Algorithm SHA256 -LiteralPath $SourcePath).Hash
$captured = Get-Date -Format yyyy-MM-dd
$content = @(
    '# Reference inventory', '',
    "Captured from the saved HTML on $captured. This is a partial file-name tree, not source code.", '',
    ('Source: `' + $SourcePath + '`'), ('SHA-256: `' + $sha + '`'), '',
    ('Visible files: **' + $files.Count + ' / 878 declared**. Hidden/collapsed files are unknown. An AGENTS.md name is not readable instruction content.'), '',
    'Subsystem mapping: rig/clips/baking → stage 1; animation engine → stage 2; deformation → stage 3; controllers → stages 4–5; Humanoid/root → stage 6; events/attachments/shapes → stage 7; IK → stage 8; culling/LOD → stage 9; GPU → stage 10; physics/Netcode → stage 11; editor/debugging → stage 12.', '',
    '## Visible paths', ''
) + @($files | ForEach-Object { '- `' + $_ + '`' })
$content | Set-Content -Encoding utf8 -LiteralPath $OutputPath
Write-Output ('Visible files: {0}; SHA256: {1}' -f $files.Count, $sha)
