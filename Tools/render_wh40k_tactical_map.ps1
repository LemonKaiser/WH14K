param(
    [string]$MapPath,
    [string]$OutputRoot = "Temp/_wh40k_tactical_render",
    [string]$Destination,
    [string]$MapsRoot = "Resources/Maps/_WH40K",
    [string]$SnapshotsRoot = "Resources/Textures/_WH40K/Interface/TacticalMap",
    [string]$Configuration = "Release",
    [switch]$AllWh40KMaps
)

$ErrorActionPreference = "Stop"

function Get-SnapshotDestination {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CurrentMapPath
    )

    $mapShortName = [System.IO.Path]::GetFileNameWithoutExtension($CurrentMapPath).ToLowerInvariant()
    return Join-Path $SnapshotsRoot "$($mapShortName)_snapshot.png"
}

function Render-Wh40KTacticalMap {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CurrentMapPath,
        [Parameter(Mandatory = $true)]
        [string]$CurrentDestination
    )

    $mapShortName = [System.IO.Path]::GetFileNameWithoutExtension($CurrentMapPath)
    Write-Host "Rendering $mapShortName -> $CurrentDestination"

    dotnet run --project Content.MapRenderer -c $Configuration -- -f $CurrentMapPath -o $OutputRoot --viewer

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

    $destinationDir = Split-Path $CurrentDestination -Parent
    if (-not (Test-Path $destinationDir))
    {
        New-Item -ItemType Directory -Path $destinationDir | Out-Null
    }

    Copy-Item $mainSnapshot.FullName $CurrentDestination -Force
    Write-Host "Copied $($mainSnapshot.FullName) -> $CurrentDestination"
}

if ($AllWh40KMaps)
{
    $maps = Get-ChildItem $MapsRoot -Filter *.yml | Sort-Object Name
    foreach ($map in $maps)
    {
        $snapshotDestination = Get-SnapshotDestination -CurrentMapPath $map.FullName
        Render-Wh40KTacticalMap -CurrentMapPath $map.FullName -CurrentDestination $snapshotDestination
    }

    return
}

if ([string]::IsNullOrWhiteSpace($MapPath))
{
    $MapPath = "Resources/Maps/_WH40K/battlefield40k.yml"
}

if ([string]::IsNullOrWhiteSpace($Destination))
{
    $Destination = Get-SnapshotDestination -CurrentMapPath $MapPath
}

Render-Wh40KTacticalMap -CurrentMapPath $MapPath -CurrentDestination $Destination
