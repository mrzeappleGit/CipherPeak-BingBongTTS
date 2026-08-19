<#
.SYNOPSIS
    Builds the mod and assembles a Thunderstore-ready zip in dist/.

.EXAMPLE
    ./build.ps1
    ./build.ps1 -PeakDir "D:\SteamLibrary\steamapps\common\PEAK"
    ./build.ps1 -Deploy          # also copies the plugin into the game's BepInEx/plugins
    ./build.ps1 -Deploy -DeployDir "$env:APPDATA\r2modmanPlus-local\PEAK\profiles\Default"
#>
[CmdletBinding()]
param(
    # Where the game's reference assemblies live (PEAK_Data\Managed). Auto-detected if omitted.
    [string] $PeakDir,
    [switch] $Deploy,
    # BepInEx root to deploy into. Defaults to $PeakDir; an r2modman profile folder is also a
    # valid BepInEx root, and with r2modman it is never the same folder as the game install.
    [string] $DeployDir,
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = '1.0.0'
$pluginFolder = 'CipherPeak-BingBongTTS'

$buildArgs = @("$root\CipherPeak.sln", '-c', $Configuration)
if ($PeakDir) { $buildArgs += "-p:PeakDir=$PeakDir" }

Write-Host 'Building...' -ForegroundColor Cyan
dotnet build @buildArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

Write-Host 'Testing...' -ForegroundColor Cyan
dotnet test "$root\tests\CipherPeak.Tests\CipherPeak.Tests.csproj" -c $Configuration --no-build
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

$binaries = Join-Path $root "src\CipherPeak.Plugin\bin\$Configuration"
$staging = Join-Path $root 'dist\staging'
$pluginStaging = Join-Path $staging "BepInEx\plugins\$pluginFolder"

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force $pluginStaging | Out-Null

Copy-Item (Join-Path $binaries 'CipherPeak.BingBongTTS.dll') $pluginStaging
Copy-Item (Join-Path $binaries 'CipherPeak.Core.dll') $pluginStaging

Copy-Item (Join-Path $root 'package\manifest.json') $staging
Copy-Item (Join-Path $root 'package\icon.png') $staging
Copy-Item (Join-Path $root 'package\CHANGELOG.md') $staging
Copy-Item (Join-Path $root 'package\README.md') $staging

$zip = Join-Path $root "dist\CipherPeak_BingBongTTS-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }

# Written by hand rather than with Compress-Archive: the ZIP spec wants '/' separators, and
# Windows PowerShell would write '\', which some package validators reject.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, 'Create')
try {
    foreach ($file in Get-ChildItem $staging -Recurse -File) {
        $entryName = $file.FullName.Substring($staging.Length + 1).Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entryName)
    }
}
finally { $archive.Dispose() }

Remove-Item $staging -Recurse -Force

Write-Host "Package: $zip" -ForegroundColor Green

if ($Deploy) {
    if (-not $DeployDir) { $DeployDir = $PeakDir }
    if (-not $DeployDir) { throw 'Pass -DeployDir (or -PeakDir) when using -Deploy.' }
    $target = Join-Path $DeployDir "BepInEx\plugins\$pluginFolder"
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item (Join-Path $binaries '*.dll') $target -Force
    Write-Host "Deployed to $target" -ForegroundColor Green
}
