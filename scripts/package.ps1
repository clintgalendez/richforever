<#
.SYNOPSIS
  Builds the release assets into dist/: addon ZIP, companion ZIPs, Discord art ZIP, SHA256SUMS.txt.

.EXAMPLE
  pwsh ./scripts/package.ps1 -Version 0.1.0-beta.1
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')][string]$Version,
    [string]$Output = "dist"
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root $Output
$stage = Join-Path $dist "stage"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

function New-Zip([string]$Source, [string]$Name, [bool]$IncludeFolder = $false) {
    [System.IO.Compression.ZipFile]::CreateFromDirectory($Source, (Join-Path $dist $Name), [System.IO.Compression.CompressionLevel]::Optimal, $IncludeFolder)
}

# Addon: the ZIP contains the RichForever folder that goes into Interface\AddOns.
New-Zip (Join-Path $root "addon/RichForever") "RichForever-addon-$Version.zip" $true

# Companion. Single-file without bundle compression: compression would add ~40 MB of private memory.
$project = Join-Path $root "companion/src/RichForever.App/RichForever.App.csproj"
$common = @("-c", "Release", "-r", "win-x64", "-p:DebugType=none", "-p:Version=$Version")
dotnet publish $project @common --self-contained true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $stage "self-contained")
if ($LASTEXITCODE -ne 0) { throw "self-contained publish failed" }
dotnet publish $project @common --self-contained false -o (Join-Path $stage "framework-dependent")
if ($LASTEXITCODE -ne 0) { throw "framework-dependent publish failed" }

foreach ($flavour in "self-contained", "framework-dependent") {
    $folder = Join-Path $stage $flavour
    Copy-Item (Join-Path $root "LICENSE") $folder
    Copy-Item (Join-Path $root "companion/README.txt") $folder
}
New-Zip (Join-Path $stage "self-contained") "RichForever-$Version-win-x64.zip"
New-Zip (Join-Path $stage "framework-dependent") "RichForever-$Version-win-x64-framework-dependent.zip"

# Original Rich Presence artwork for your own Discord application.
New-Zip (Join-Path $root "companion/assets/discord") "RichForever-discord-assets-$Version.zip"

Remove-Item $stage -Recurse -Force
Get-ChildItem $dist -Filter *.zip | Sort-Object Name | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
} | Set-Content (Join-Path $dist "SHA256SUMS.txt") -Encoding ascii

Get-ChildItem $dist | Format-Table Name, @{ Name = "MB"; Expression = { [math]::Round($_.Length / 1MB, 2) } }
