# Guild Wars 2 Icon Assets

The Guild Wars 2 icon library is stored outside Git in the private,
environment-specific `game-assets` bucket. Its versioned source of truth is
`../assets/icons.manifest.json`.

## Publishing

Run the **Sync Guild Wars 2 Assets** GitHub Actions workflow after the target
Terraform environment exists. It downloads every icon from its canonical source,
checks its SHA-256 against the manifest, then uploads the icon to its
content-addressed object path. It also uploads the manifest to:

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
GET /icons/:sha256.png
```

The icon route accepts only lowercase 64-character SHA-256 values. It resolves
the request through the stored manifest and never accepts a bucket object path
from the caller. Icon bytes are returned without recomputing their hash; bucket
write access and the manifest publisher are the trusted integrity boundary.
Content-addressed icons receive long-lived immutable HTTP caching headers.
