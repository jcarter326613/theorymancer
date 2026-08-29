# Guild Wars 2 Icon Assets

The Guild Wars 2 icon library is stored outside Git in the private,
environment-specific `game-assets` bucket. Its versioned source of truth is
`../assets/icons.manifest.json`.

## Publishing

Run `../assets/update-icons-manifest.ps1` to refresh the checked-in manifest
from Guild Wars 2 API metadata. It records every skill with an icon, including
the profession, weapon, slot, and specialization context needed to distinguish
same-named skills. It also records unique fact and traited-fact icons for boons,
conditions, and other effects. This metadata-only operation never downloads PNGs.

Run the **Sync Guild Wars 2 Assets** GitHub Actions workflow after the target
Terraform environment exists. It checks the bucket before fetching an icon, so
it downloads and uploads only missing UUID-addressed assets. It also uploads the
manifest to:

```text
guild-wars-2/icons.manifest.json
```

The deployment identity can write the bucket. Runtime services cannot.

## Asset API

`../api` is a dedicated Node.js and TypeScript Cloud Run service. Each
environment has its own generated Cloud Run URL and runtime service account.
That service account has `roles/storage.objectViewer` on only its environment's
game-assets bucket.

The service exposes:

```text
GET /health
GET /icons/manifest
GET /icons/:assetId.png
```

The icon route accepts only lowercase UUID asset IDs. It resolves the request
through the stored manifest and never accepts a bucket object path from the
caller. The canonical ArenaNet render URL and the manifest publisher are the
trusted integrity boundary. UUID-addressed icons receive long-lived immutable
HTTP caching headers.
