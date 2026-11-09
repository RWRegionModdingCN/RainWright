param(
    [string]$Configuration = 'Release',
    [string]$BuildRoot,
    [switch]$IncludeGameTests,
    [string]$RainWorldDir,
    [string]$PomDllPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $BuildRoot) { $BuildRoot = Join-Path $repository 'artifacts' }
$BuildRoot = [IO.Path]::GetFullPath($BuildRoot)
function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE): $($Arguments -join ' ')" }
}
Push-Location $repository
try {
    $common = @('-c', $Configuration, '-p:DeployToGame=false', "-p:RainWrightBuildRoot=$BuildRoot", '-clp:ErrorsOnly')
    $project = 'src/Rainwright/Rainwright.csproj'
    Invoke-DotNet (@('build', $project) + $common + @('-p:RainWrightPortableBuild=true'))
    $portableDll = Join-Path $BuildRoot "bin/Rainwright/$Configuration/net48-core/RainWright.dll"
    Invoke-DotNet (@('run', '--project', 'tests/RainwrightBinaryContract.Tests') + $common + @('--', $portableDll, '--portable'))
    Invoke-DotNet (@('build', $project) + $common + @('-p:RainwrightStandaloneSmokeTest=true'))
    $passed = 1
    foreach ($directory in Get-ChildItem -LiteralPath 'tests' -Directory | Sort-Object Name) {
        if ($directory.Name -eq 'RainwrightBinaryContract.Tests') { continue }
        $testProject = Join-Path $directory.FullName ($directory.Name + '.csproj')
        if (-not (Test-Path -LiteralPath $testProject)) { continue }
        [xml]$definition = Get-Content -LiteralPath $testProject -Raw
        $framework = $definition.SelectSingleNode('/Project/PropertyGroup/TargetFramework')
        $output = $definition.SelectSingleNode('/Project/PropertyGroup/OutputType')
        if (-not $framework -or $framework.InnerText -ne 'net10.0' -or -not $output -or $output.InnerText -ne 'Exe') { continue }
        Invoke-DotNet (@('run', '--project', $testProject) + $common + @('-p:RainwrightStandaloneSmokeTest=true'))
        $passed++
    }
    Write-Output "PASS: $passed portable regression projects."
    if ($IncludeGameTests) {
        if (-not $IsWindows -or -not $RainWorldDir) { throw 'Game assembly tests require Windows and -RainWorldDir.' }
        $game = [IO.Path]::GetFullPath($RainWorldDir)
        if (-not $PomDllPath) { $PomDllPath = Join-Path $game '../../workshop/content/312520/2920439169/plugins/Pom.dll' }
        Invoke-DotNet (@('build', $project) + $common + @("-p:RainWorldDir=$game"))
        $gameDll = Join-Path $BuildRoot "bin/Rainwright/$Configuration/RainWright.dll"
        Invoke-DotNet (@('run', '--project', 'tests/RainwrightBinaryContract.Tests') + $common + @('--', $gameDll))
        foreach ($name in @('ManagedObjectCompatibility.Tests', 'RainWrightNativeRemix.Tests')) {
            Invoke-DotNet (@('build', "tests/$name/$name.csproj") + $common + @("-p:RainWorldDir=$game", "-p:RainWrightDllPath=$gameDll", "-p:PomDllPath=$PomDllPath"))
            $executable = Join-Path $BuildRoot "bin/$name/$Configuration/net48/$name.exe"
            $arguments = @($gameDll, $game)
            if ($name -eq 'ManagedObjectCompatibility.Tests') { $arguments += $PomDllPath }
            & $executable @arguments
            if ($LASTEXITCODE -ne 0) { throw "Game assembly regression failed: $name" }
        }
    }
}
finally { Pop-Location }
