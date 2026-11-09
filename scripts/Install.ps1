param(
    [Parameter(Mandatory = $true)][string]$RainWorldDir,
    [string]$PackageDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $PackageDirectory) { $PackageDirectory = Join-Path $repository 'artifacts/mod/RainWright' }
$PackageDirectory = [IO.Path]::GetFullPath($PackageDirectory)
$packageInfo = Get-Content -LiteralPath (Join-Path $PackageDirectory 'modinfo.json') -Raw | ConvertFrom-Json
if ($packageInfo.id -cne 'rainwright' -or -not (Test-Path -LiteralPath (Join-Path $PackageDirectory 'newest/plugins/RainWright.dll'))) {
    throw 'Build the complete game Mod with scripts/Build.ps1 before installation.'
}
$game = [IO.Path]::GetFullPath($RainWorldDir)
if (-not (Test-Path -LiteralPath (Join-Path $game 'RainWorld_Data/Managed/Assembly-CSharp.dll'))) { throw 'RainWorldDir is not a Rain World installation.' }
$mods = [IO.Path]::GetFullPath((Join-Path $game 'RainWorld_Data/StreamingAssets/mods'))
$targetRoot = [IO.Path]::GetFullPath((Join-Path $mods 'RainWright'))
if (-not $targetRoot.StartsWith($mods + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid Mod target.' }
$existingInfo = Join-Path $targetRoot 'modinfo.json'
if ((Test-Path -LiteralPath $existingInfo) -and (Get-Content -LiteralPath $existingInfo -Raw | ConvertFrom-Json).id -cne 'rainwright') {
    throw 'The destination directory belongs to another Mod.'
}
$backup = Join-Path $repository ('artifacts/install-backups/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$completed = [Collections.Generic.List[object]]::new()
try {
    foreach ($file in Get-ChildItem -LiteralPath $PackageDirectory -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($PackageDirectory, $file.FullName)
        $target = [IO.Path]::GetFullPath((Join-Path $targetRoot $relative))
        if (-not $target.StartsWith($targetRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package path escaped its target.' }
        $cursor = $target
        while ($cursor -and $cursor.Length -ge $mods.Length) {
            if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Installation refuses redirected paths: $cursor" }
            $cursor = Split-Path -Parent $cursor
        }
        $saved = $null
        if (Test-Path -LiteralPath $target) {
            $saved = Join-Path $backup $relative
            [IO.Directory]::CreateDirectory((Split-Path -Parent $saved)) | Out-Null
            Copy-Item -LiteralPath $target -Destination $saved
        }
        [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
        $completed.Add(@{ Target = $target; Backup = $saved })
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}
catch {
    $originalError = $_
    for ($i = $completed.Count - 1; $i -ge 0; $i--) {
        $operation = $completed[$i]
        try {
            if ($operation.Backup) { Copy-Item -LiteralPath $operation.Backup -Destination $operation.Target }
            else { Remove-Item -LiteralPath $operation.Target -ErrorAction Stop }
            Write-Warning "Restored installation target: $($operation.Target)"
        }
        catch { Write-Warning "Rollback failed for $($operation.Target): $_" }
    }
    throw $originalError
}
Write-Output "Installed RainWright: $targetRoot"
Write-Output "Previous package files, if any: $backup"
