param(
    [Parameter(Mandatory = $true)][string]$StackTrace,
    [Parameter(Mandatory = $true)][string]$Output,
    [string]$PrivateDirectory = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'
$PrivateDirectory = [IO.Path]::GetFullPath($PrivateDirectory)
$helper = Join-Path $PrivateDirectory 'decoder/RainWrightProtection.dll'
if (-not (Test-Path -LiteralPath $helper)) { throw 'Point PrivateDirectory at this build''s debug-private folder.' }
& dotnet $helper decode (Join-Path $PrivateDirectory 'symbol-map.json') ([IO.Path]::GetFullPath($StackTrace)) ([IO.Path]::GetFullPath($Output))
if ($LASTEXITCODE -ne 0) { throw "Stack trace restoration failed: $LASTEXITCODE" }
