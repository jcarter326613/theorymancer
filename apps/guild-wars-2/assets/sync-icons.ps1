[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern("^[a-z0-9._-]+$")]
    [string]$Bucket,
    [string]$ManifestPath = (Join-Path $PSScriptRoot "icons.manifest.json")
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command gcloud -ErrorAction SilentlyContinue)) {
    throw "gcloud CLI is required to publish icon assets."
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.version -ne 2) {
    throw "Unsupported icon manifest version '$($manifest.version)'."
}

$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "theorymancer-icon-sync-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
try {
    foreach ($asset in $manifest.assets) {
        if ($asset.asset_id -notmatch "^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$") {
            throw "Invalid asset ID '$($asset.asset_id)'."
        }
        if ($asset.object_path -ne "guild-wars-2/icons/$($asset.asset_id).png") {
            throw "Invalid object path for asset $($asset.asset_id)."
        }

        $destination = "gs://$Bucket/$($asset.object_path)"
        & gcloud storage objects describe $destination --format="value(name)" 2>$null
        if ($LASTEXITCODE -eq 0) {
            continue
        }

        $temporaryPath = Join-Path $temporaryDirectory "$($asset.asset_id).png"
        Invoke-WebRequest -Uri $asset.source_url -OutFile $temporaryPath

        & gcloud storage cp $temporaryPath $destination --no-clobber
        if ($LASTEXITCODE -ne 0) {
            throw "Could not upload icon asset $($asset.asset_id)."
        }
    }

    & gcloud storage cp $ManifestPath "gs://$Bucket/guild-wars-2/icons.manifest.json"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not upload the icon manifest."
    }
}
finally {
    Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
