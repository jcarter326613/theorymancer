[CmdletBinding()]
param(
    [string]$ManifestPath = (Join-Path $PSScriptRoot "icons.manifest.json"),
    [ValidateRange(1, 200)]
    [int]$BatchSize = 100
)

$ErrorActionPreference = "Stop"
$apiBaseUrl = "https://api.guildwars2.com/v2"

$assetIdBySourceUrl = @{}
$existingAssetBySourceUrl = @{}
$addedAssetIds = @{}
$assets = [System.Collections.Generic.List[object]]::new()
if (Test-Path -LiteralPath $ManifestPath) {
    $existingManifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    if ($existingManifest.version -eq 2) {
        foreach ($asset in $existingManifest.assets) {
            $assetIdBySourceUrl[$asset.source_url] = $asset.asset_id
            $existingAssetBySourceUrl[$asset.source_url] = $asset
        }
    }
}

$effects = [System.Collections.Generic.List[object]]::new()
$effectsByKey = @{}

function Get-AssetId([string]$SourceUrl) {
    if ([string]::IsNullOrWhiteSpace($SourceUrl)) {
        return $null
    }

    if ($assetIdBySourceUrl.ContainsKey($SourceUrl)) {
        $assetId = $assetIdBySourceUrl[$SourceUrl]
        if (-not $addedAssetIds.ContainsKey($assetId)) {
            $assets.Add($existingAssetBySourceUrl[$SourceUrl])
            $addedAssetIds[$assetId] = $true
        }
        return $assetId
    }

    $assetId = [Guid]::NewGuid().ToString()
    $assetIdBySourceUrl[$SourceUrl] = $assetId
    $assets.Add([pscustomobject][ordered]@{
        asset_id = $assetId
        source_url = $SourceUrl
        object_path = "guild-wars-2/icons/$assetId.png"
    })
    $addedAssetIds[$assetId] = $true
    return $assetId
}

function Get-OptionalString($Value) {
    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }

    return [string]$Value
}

function Add-Effect($Fact, [string]$FactType) {
    $assetId = Get-AssetId (Get-OptionalString $Fact.icon)
    $name = Get-OptionalString $Fact.status
    if ($null -eq $name) {
        $name = Get-OptionalString $Fact.text
    }
    if ($null -eq $assetId -or $null -eq $name) {
        return
    }

    $description = Get-OptionalString $Fact.description
    $key = "$assetId`u001f$FactType`u001f$name`u001f$description"
    if ($effectsByKey.ContainsKey($key)) {
        return
    }

    $effect = [pscustomobject][ordered]@{
        name = $name
        fact_type = $FactType
        description = $description
        icon_asset_id = $assetId
    }
    $effectsByKey[$key] = $effect
    $effects.Add($effect)
}

$skillIds = @(Invoke-RestMethod -Uri "$apiBaseUrl/skills") | Sort-Object
$skills = [System.Collections.Generic.List[object]]::new()
for ($offset = 0; $offset -lt $skillIds.Count; $offset += $BatchSize) {
    $count = [Math]::Min($BatchSize, $skillIds.Count - $offset)
    $ids = $skillIds[$offset..($offset + $count - 1)] -join ","
    $batch = Invoke-RestMethod -Uri "$apiBaseUrl/skills?ids=$([Uri]::EscapeDataString($ids))"

    foreach ($skill in $batch | Sort-Object id) {
        $assetId = Get-AssetId (Get-OptionalString $skill.icon)
        if ($null -eq $assetId) {
            continue
        }

        $skills.Add([pscustomobject][ordered]@{
            skill_id = [int]$skill.id
            name = [string]$skill.name
            type = Get-OptionalString $skill.type
            professions = @($skill.professions | ForEach-Object { [string]$_ })
            weapon_type = Get-OptionalString $skill.weapon_type
            slot = Get-OptionalString $skill.slot
            specialization_ids = @($skill.specialization | ForEach-Object { [int]$_ })
            categories = @($skill.categories | ForEach-Object { [string]$_ })
            attunement = Get-OptionalString $skill.attunement
            icon_asset_id = $assetId
        })

        foreach ($fact in @($skill.facts) + @($skill.traited_facts)) {
            if ($null -eq $fact) {
                continue
            }

            $factType = Get-OptionalString $fact.type
            if ($null -ne $factType) {
                Add-Effect $fact $factType
            }
            if ($null -ne $fact.prefix) {
                Add-Effect $fact.prefix "$factType-prefix"
            }
        }
    }
}

$manifest = [pscustomobject][ordered]@{
    version = 2
    assets = @($assets | Sort-Object source_url)
    skills = @($skills | Sort-Object skill_id)
    effects = @($effects | Sort-Object icon_asset_id, fact_type, name, description)
}

$json = $manifest | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText(
    $ManifestPath,
    "$json`r`n",
    [System.Text.UTF8Encoding]::new($false))

[pscustomobject]@{
    Assets = $assets.Count
    Skills = $skills.Count
    Effects = $effects.Count
}
