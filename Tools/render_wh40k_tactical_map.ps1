param(
    [string]$MapPath = "Resources/Maps/_WH40K/battlefield40k.yml",
    [string]$OutputRoot = "Temp/_wh40k_tactical_render",
    [string]$Destination = "Resources/Textures/_WH40K/Interface/TacticalMap/battlefield40k_snapshot.png"
)

$ErrorActionPreference = "Stop"

$mapShortName = [System.IO.Path]::GetFileNameWithoutExtension($MapPath)

dotnet run --project Content.MapRenderer -- -f $MapPath -o $OutputRoot --viewer

$renderDir = Join-Path $OutputRoot $mapShortName
if (-not (Test-Path $renderDir))
{
    throw "Map renderer output directory not found: $renderDir"
}

$mainSnapshot = Get-ChildItem $renderDir -Filter "$mapShortName-*.png" |
    Sort-Object Length -Descending |
    Select-Object -First 1

if ($null -eq $mainSnapshot)
{
    throw "No rendered PNG files were produced for $mapShortName"
}

$destinationDir = Split-Path $Destination -Parent
if (-not (Test-Path $destinationDir))
{
    New-Item -ItemType Directory -Path $destinationDir | Out-Null
}

Copy-Item $mainSnapshot.FullName $Destination -Force
Write-Host "Copied $($mainSnapshot.FullName) -> $Destination"
