<#
.SYNOPSIS
  Bouwt FLEsync en maakt de MSI-installer (FLEsync-<versie>-x64.msi).
.EXAMPLE
  .\build.ps1                 # Release-build, tests, publish en MSI
  .\build.ps1 -Version 1.2.0  # met specifiek versienummer
#>
param(
    [string]$Version = "",
    [switch]$SkipTests
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$publish = Join-Path $root "publish\FLEsync"
$out = Join-Path $root "out"
$versionArg = @()
if ($Version) { $versionArg = @("-p:Version=$Version") }

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
New-Item -ItemType Directory -Force -Path $out | Out-Null

if (-not $SkipTests) {
    dotnet test "$root\tests\FLEsync.Core.Tests" -c Release
    if ($LASTEXITCODE) { throw "Tests mislukt" }
}

foreach ($proj in "src\FLEsync.App\FLEsync.App.csproj", "src\FLEsync.Service\FLEsync.Service.csproj") {
    dotnet publish "$root\$proj" -c Release -r win-x64 --self-contained true -o $publish @versionArg
    if ($LASTEXITCODE) { throw "Publish van $proj mislukt" }
}
Get-ChildItem $publish -Filter *.pdb | Remove-Item

dotnet build "$root\installer\FLEsync.Installer.wixproj" -c Release -p:PublishDir="$publish" -o $out @versionArg
if ($LASTEXITCODE) { throw "MSI-build mislukt" }

Get-ChildItem $out -Filter *.msi | ForEach-Object { Write-Host "MSI gereed: $($_.FullName)" -ForegroundColor Green }
