param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$RainWorldDir = 'D:\Steam\steamapps\common\Rain World',
    [string]$InputDll,
    [string]$BuildRoot,
    [string]$ToolDirectory,
    [ValidatePattern("^[0-9a-fA-F]{64}$")][string]$Seed,
    [string[]]$AdditionalConsumers = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $BuildRoot) { $BuildRoot = Join-Path $repository 'artifacts' }
$BuildRoot = [IO.Path]::GetFullPath($BuildRoot)
$RainWorldDir = [IO.Path]::GetFullPath($RainWorldDir)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ($OutputDirectory.StartsWith($RainWorldDir.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Choose an output directory outside the game. This command never installs a DLL.'
}
if ((Test-Path -LiteralPath $OutputDirectory) -and @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -gt 0) {
    throw "Output directory must be new or empty: $OutputDirectory"
}
if (-not $ToolDirectory) { $ToolDirectory = Join-Path $BuildRoot 'protection-tools/obfuscar-2.2.50' }
$ToolDirectory = [IO.Path]::GetFullPath($ToolDirectory)
$obfuscar = Join-Path $ToolDirectory 'obfuscar.console.exe'
$runDirectory = Join-Path $BuildRoot ('protection-runs/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$package = Join-Path $runDirectory 'verified-package'
if (-not $Seed) {
    $seedBytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($seedBytes) } finally { $rng.Dispose() }
    $Seed = [BitConverter]::ToString($seedBytes).Replace('-', '')
}
$mods = Join-Path $RainWorldDir 'RainWorld_Data/StreamingAssets/mods'
$consumers = @('DryCycle.dll', 'DryCycle.WorldData.RWImGui.dll', 'DryCycle.AIObservatory.RWImGui.dll') |
    ForEach-Object { Join-Path $mods ('Ancient Site/newest/plugins/' + $_) } | Where-Object { Test-Path -LiteralPath $_ }
$consumers = @($consumers) + @($AdditionalConsumers | ForEach-Object { [IO.Path]::GetFullPath($_) })
$consumers = @($consumers | Select-Object -Unique)
$installed = @('RainWright.dll', 'RainWright.pdb') | ForEach-Object { Join-Path $mods ('RainWright/newest/plugins/' + $_) }
$unchanged = @{}
foreach ($file in @($installed) + $consumers) {
    if (Test-Path -LiteralPath $file) { $unchanged[$file] = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash }
}
function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE): $($Arguments -join ' ')" }
}

Push-Location $repository
try {
    if (-not $InputDll) {
        Invoke-DotNet @('build', 'src/Rainwright/Rainwright.csproj', '-c', 'Release', '-p:DeployToGame=false',
            '-p:DebugType=portable', '-p:DebugSymbols=true', "-p:RainWrightBuildRoot=$BuildRoot", "-p:RainWorldDir=$RainWorldDir", '-clp:ErrorsOnly')
        $InputDll = Join-Path $BuildRoot 'bin/Rainwright/Release/RainWright.dll'
    }
    $InputDll = [IO.Path]::GetFullPath($InputDll)
    $inputHash = (Get-FileHash -LiteralPath $InputDll -Algorithm SHA256).Hash
    Invoke-DotNet @('run', '--project', 'tests/RainWrightProtection.Tests/RainWrightProtection.Tests.csproj', '-c', 'Release', '-p:DeployToGame=false', "-p:RainWrightBuildRoot=$BuildRoot", '-clp:ErrorsOnly')
    Invoke-DotNet @('build', 'tools/RainWrightProtectionProbe/RainWrightProtectionProbe.csproj', '-c', 'Release', "-p:RainWorldDir=$RainWorldDir", "-p:RainWrightBuildRoot=$BuildRoot", '-clp:ErrorsOnly')
    Invoke-DotNet @('build', 'tools/RainWrightMonoHost/RainWrightMonoHost.csproj', '-c', 'Release', "-p:RainWrightBuildRoot=$BuildRoot", '-clp:ErrorsOnly')
    Invoke-DotNet @('build', 'tools/RainWrightProtection/RainWrightProtection.csproj', '-c', 'Release', "-p:RainWrightBuildRoot=$BuildRoot", '-clp:ErrorsOnly')
    $helper = Join-Path $BuildRoot 'bin/RainWrightProtection/Release/net10.0/RainWrightProtection.dll'
    if (-not (Test-Path -LiteralPath $obfuscar)) {
        Invoke-DotNet @('tool', 'install', 'Obfuscar.GlobalTool', '--version', '2.2.50', '--tool-path', $ToolDirectory, '--allow-roll-forward')
    }
    $toolInventory = & dotnet tool list --tool-path $ToolDirectory
    if ($LASTEXITCODE -ne 0 -or -not ($toolInventory -match 'obfuscar.globaltool\s+2\.2\.50\s')) {
        throw "Expected isolated Obfuscar.GlobalTool 2.2.50 at $ToolDirectory"
    }
    Invoke-DotNet (@($helper, 'plan', $InputDll, $runDirectory, $RainWorldDir, $Seed) + $consumers)
    & $obfuscar (Join-Path $runDirectory 'obfuscar.xml')
    if ($LASTEXITCODE -ne 0) { throw "Obfuscar failed: $LASTEXITCODE" }
    Invoke-DotNet @($helper, 'finish', $runDirectory, $package)
    Invoke-DotNet @($helper, 'smoke', $package)
    $probe = Join-Path $BuildRoot 'bin/RainWrightProtectionProbe/Release/net48/RainWrightProtectionProbe.exe'
    $monoHost = Join-Path $BuildRoot 'bin/RainWrightMonoHost/Release/net10.0/RainWrightMonoHost.dll'
    $privateDir = Join-Path $package 'debug-private'
    $probePlan = Join-Path $privateDir 'probe-plan.xml'
    $baseline = Join-Path $runDirectory 'input/RainWright.dll'
    $candidate = Join-Path $privateDir 'RainWright.dll'
    $clrBaseline = Join-Path $privateDir 'clr-baseline.xml'
    $clrCandidate = Join-Path $privateDir 'clr-candidate.xml'
    $monoBaseline = Join-Path $privateDir 'mono-baseline.xml'
    $monoCandidate = Join-Path $privateDir 'mono-candidate.xml'
    & $probe $baseline $probePlan $clrBaseline $RainWorldDir
    if ($LASTEXITCODE -ne 0) { throw 'Baseline CLR execution failed.' }
    & $probe $candidate $probePlan $clrCandidate $RainWorldDir
    if ($LASTEXITCODE -ne 0) { throw 'Protected CLR execution failed.' }
    Invoke-DotNet @($monoHost, $RainWorldDir, $probe, $baseline, $probePlan, $monoBaseline, $RainWorldDir)
    Invoke-DotNet @($monoHost, $RainWorldDir, $probe, $candidate, $probePlan, $monoCandidate, $RainWorldDir)
    foreach ($pair in @(@($clrBaseline, $clrCandidate), @($monoBaseline, $monoCandidate))) {
        $expected = [Xml.Linq.XDocument]::Load($pair[0])
        $actual = [Xml.Linq.XDocument]::Load($pair[1])
        if (-not [Xml.Linq.XNode]::DeepEquals($expected, $actual)) { throw ('Protected production behavior differs from baseline: ' + $pair[1]) }
    }
    # No automatic publication: runtime/graphics/hook acceptance remains a distinct gate.
    $gate = [ordered]@{ candidateSha256=(Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash; staticVerification=$true; clrDifferential=$true; gameMonoDifferential=$true; debugSymbols=$true; inGameAcceptance=$false; readyForPublication=$false; pending=@('In-game UI, Hook, POM/RegionKit, author-save and performance acceptance') }
    $gate | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $package 'release-gate.json') -Encoding utf8

    if ((Get-FileHash -LiteralPath $InputDll -Algorithm SHA256).Hash -ne $inputHash) { throw 'Build input changed during protection.' }
    foreach ($file in $unchanged.Keys) {
        if (-not (Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $unchanged[$file]) {
            throw "An installed file changed during this run: $file"
        }
    }
    $record = [ordered]@{
        inputSha256 = $inputHash
        obfuscarVersion = '2.2.50'; cecilVersion = '0.11.6'; gameStarted = $false
        installedFilesUnchanged = $unchanged; consumersVerified = $consumers
        staticVerificationPassed = $true; productionMethodAndStackTraceSmokePassed = $true; clrDifferentialPassed = $true; gameMonoDifferentialPassed = $true; inGameTested = $false; readyForPublication = $false
    }
    $record | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $package 'debug-private/build-record.json') -Encoding utf8
    Copy-Item -LiteralPath (Join-Path $repository 'tools/RainWrightProtection/README.md') -Destination (Join-Path $package 'README.md')
    # Include a self-contained decoder so this build's private archive needs no source checkout.
    $decoder = Join-Path $package 'debug-private/decoder'
    [void][IO.Directory]::CreateDirectory($decoder)
    foreach ($file in @('RainWrightProtection.dll', 'RainWrightProtection.deps.json', 'RainWrightProtection.runtimeconfig.json', 'Mono.Cecil.dll', 'Mono.Cecil.Pdb.dll', 'Mono.Cecil.Mdb.dll', 'Mono.Cecil.Rocks.dll')) {
        $source = Join-Path (Split-Path -Parent $helper) $file
        if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $decoder }
    }
    Copy-Item -LiteralPath (Join-Path $repository 'scripts/Restore-RainWrightStackTrace.ps1') -Destination (Join-Path $package 'debug-private/Restore-StackTrace.ps1')
    [void][IO.Directory]::CreateDirectory($OutputDirectory)
    Get-ChildItem -LiteralPath $package -Force | Copy-Item -Destination $OutputDirectory -Recurse
    Write-Output "Verified protected package: $OutputDirectory"
    Write-Output 'candidate/RainWright.dll is an offline-verified candidate, pending in-game acceptance. Keep debug-private private. No game files were installed.'
} finally { Pop-Location }
