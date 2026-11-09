param(
    [string]$RainWorldDir,
    [string]$RWImGuiPluginDir,
    [string]$Configuration = 'Release',
    [string]$BuildRoot
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $BuildRoot) { $BuildRoot = Join-Path $repository 'artifacts' }
$BuildRoot = [IO.Path]::GetFullPath($BuildRoot)
$arguments = @('build', (Join-Path $repository 'src/Rainwright/Rainwright.csproj'), '-c', $Configuration,
    '-p:DeployToGame=false', "-p:RainWrightBuildRoot=$BuildRoot", '-clp:ErrorsOnly')
if ($RainWorldDir) { $arguments += "-p:RainWorldDir=$([IO.Path]::GetFullPath($RainWorldDir))" }
if ($RWImGuiPluginDir) { $arguments += "-p:RWImGuiPluginDir=$([IO.Path]::GetFullPath($RWImGuiPluginDir))" }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "RainWright build failed: $LASTEXITCODE" }
$dll = Join-Path $BuildRoot "bin/Rainwright/$Configuration/RainWright.dll"
if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw "Game DLL was not produced: $dll" }
$package = Join-Path $BuildRoot 'mod/RainWright'
$modSource = Join-Path $repository 'mod'
foreach ($file in Get-ChildItem -LiteralPath $modSource -Recurse -File) {
    $relative = [IO.Path]::GetRelativePath($modSource, $file.FullName)
    if ($relative.StartsWith('artwork' + [IO.Path]::DirectorySeparatorChar) -or $file.Extension -in @('.md', '.psd', '.pcraft')) { continue }
    $target = Join-Path $package $relative
    [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
}
$plugins = Join-Path $package 'newest/plugins'
[IO.Directory]::CreateDirectory($plugins) | Out-Null
Copy-Item -LiteralPath $dll -Destination (Join-Path $plugins 'RainWright.dll')
$symbols = [IO.Path]::ChangeExtension($dll, '.pdb')
if (Test-Path -LiteralPath $symbols) { Copy-Item -LiteralPath $symbols -Destination (Join-Path $plugins 'RainWright.pdb') }
Copy-Item -LiteralPath (Join-Path $repository 'LICENSE') -Destination (Join-Path $package 'LICENSE')
Copy-Item -LiteralPath (Join-Path $repository 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $package 'THIRD_PARTY_NOTICES.md')
Write-Output "Game Mod package: $package"
