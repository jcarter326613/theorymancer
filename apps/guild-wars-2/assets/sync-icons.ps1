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
if ($manifest.version -ne 1) {
    throw "Unsupported icon manifest version '$($manifest.version)'."
}

$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "theorymancer-icon-sync-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
try {
    foreach ($icon in $manifest.icons) {
        if ($icon.object_path -notmatch "^guild-wars-2/icons/[0-9a-f]{64}\.png$") {
            throw "Invalid object path for skill $($icon.skill_id)."
        }

        $temporaryPath = Join-Path $temporaryDirectory "$($icon.skill_id).png"
        Invoke-WebRequest -Uri $icon.source_url -OutFile $temporaryPath
        $hash = (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne $icon.sha256) {
            throw "Downloaded icon hash for skill $($icon.skill_id) does not match the manifest."
        }

        $destination = "gs://$Bucket/$($icon.object_path)"
        & gcloud storage objects describe $destination --format="value(name)" 2>$null
        if ($LASTEXITCODE -eq 0) {
            continue
        }

        & gcloud storage cp $temporaryPath $destination --no-clobber
        if ($LASTEXITCODE -ne 0) {
            throw "Could not upload icon for skill $($icon.skill_id)."
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
