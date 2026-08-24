# Windows Combat Collector Plan

## Goal

Create and maintain a Theorymancer-owned, open-source Windows collector that
observes Guild Wars 2 combat events locally and produces data suitable for
deterministic rotation analysis. The collector must not depend on ArcDPS,
EVTC, or any other third-party game integration.

The initial target is Windows only. Support for other platforms is deferred
until there is demonstrated user demand.

## Product Boundary

The collector is game-specific code. It remains under
`apps/guild-wars-2/windows-collector` and has no dependency on the public web
application or HTTP API.

The initial collection path is:

```text
gw2-64.exe
  -> Theorymancer-owned D3D11 proxy DLL
  -> Theorymancer collector
  -> local, versioned Theorymancer log
  -> separate user-consented uploader
```

The collector is not an ArcDPS extension and must never load, call, copy, or
parse ArcDPS components or EVTC files.

## Safety Requirements

- Read only: do not write Guild Wars 2 memory.
- Do not inject input, automate actions, or alter gameplay timing.
- Do not intercept, modify, replay, or generate game network traffic.
- Do not include an overlay in the initial collector.
- Do not allow the injected DLL to make network requests or self-update.
- Start no work in `DllMain`; initialize only after the proxied Direct3D entry
  point is called.
- Capture locally first. Upload only from a separate process after explicit
  user consent.
- Disable capture for an unknown Guild Wars 2 build.
- Publish source, reproducible build instructions, release hashes, schemas,
  and security review documentation.

## Delivery Stages

### 1. Loader and Diagnostics

Build a `d3d11.dll` proxy with Theorymancer-owned code. It forwards required
Direct3D device-creation calls to the system `d3d11.dll`, then writes a local
diagnostic record proving that it loaded in the Guild Wars 2 process.

This stage has no game-data extraction. It exists to prove the deployment,
logging, and safe failure path before any reverse-engineering work.

Feature-branch builds run on GitHub-hosted Windows runners and publish a
seven-day GitHub Actions artifact containing the DLL and its SHA-256 manifest.
This avoids a local Windows compiler and does not use GCP for development
artifacts. Public releases, signing, and a durable download location are later
release-stage concerns.

### 2. Fixed-Build Discovery

For one explicitly supported Guild Wars 2 build, use controlled training-golem
sequences to identify the client functions and structures that dispatch
high-level combat state changes. Test sequences must cover a skill activation,
weapon swap, cast cancellation, strike damage, condition damage, boon
application/removal, dodge, and target death.

Record only independently discovered information. Do not use ArcDPS code or
its internal implementation as a source.

### 3. Read-Only Event Observation

Install narrow hooks only on independently verified high-level event dispatch
functions. Each hook copies primitive event fields into an in-memory queue and
immediately calls the original function without changing its arguments or
return value.

Hooks must be small, non-blocking, and scoped to the supported build. An
unknown build must result in no hooks being installed.

### 4. Raw Log Format

Write a Theorymancer-owned, append-only binary event stream. The format must
include collector version, schema version, Guild Wars 2 build, session ID,
timestamps, and integrity records. It must support agent identity/state,
skill and animation lifecycle, damage, buffs, weapon swaps, positions, and
encounter boundaries.

EVTC may be supported as an import/export interoperability format later, but
it is not the collector's canonical storage format.

### 5. Validation and Release

Maintain repeatable test sequences for every supported game build. Validate
event ordering and timing against the controlled sequence, measure game
performance impact, and verify that no Guild Wars 2 memory, input, or network
traffic changes occur. Disable the collector immediately for a newly detected
game build until it passes validation.

## Operational Constraints

ArenaNet's third-party-program policy does not endorse this collector and
reserves discretion to act on any third-party modification. Open-source,
read-only implementation reduces trust and supply-chain risk but does not
remove account-holder risk. Releases must communicate that risk plainly.
