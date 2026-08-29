# Guild Wars 2

This application namespace contains Guild Wars 2-specific integrations, EVTC
ingestion, mechanics models, and deterministic performance analysis.

`desktop/` is a self-contained C# WPF companion application for capturing the
visible, calibrated combat-log panel and recognizing changed rows locally. It
requires calibration of both the combat log and skill bar, which is retained
across sessions. Skill-bar calibration detects and displays its five weapon
slots for review before saving; it does not yet identify the icons or sample
skill activations. It never loads into the game client, accesses game memory,
or intercepts network traffic. It must be run with Guild Wars 2 visible and
unobscured.

The collector targets Windows 10 version 2004 or later on x64 hardware. It
uses Windows' English OCR language pack; install that pack through Windows
Settings before recording.

Build and test it on Windows with the .NET SDK pinned in `global.json`:

```powershell
dotnet build apps/guild-wars-2/desktop/Theorymancer.GuildWars2.Desktop/Theorymancer.GuildWars2.Desktop.csproj
dotnet test apps/guild-wars-2/desktop/Theorymancer.GuildWars2.Desktop.Tests/Theorymancer.GuildWars2.Desktop.Tests.csproj
```

Publish the standard-user executable with:

```powershell
dotnet publish apps/guild-wars-2/desktop/Theorymancer.GuildWars2.Desktop/Theorymancer.GuildWars2.Desktop.csproj -c Release -r win-x64 --self-contained true
```

The publish directory contains `TheorymancerScreenCollector.exe`, which can be
started by double-clicking it.

## Icon Assets

`assets/icons.manifest.json` is the versioned source of truth for immutable GW2
icon assets. It records the public canonical source URL, SHA-256, and
content-addressed Cloud Storage object path for each icon. The PNG corpus is not
committed to Git or Git LFS.

After the applicable Terraform environment has created its game-assets bucket,
publish the manifest locally with authenticated `gcloud`:

```powershell
pwsh apps/guild-wars-2/assets/sync-icons.ps1 -Bucket YOUR_GAME_ASSETS_BUCKET
```

The **Sync Guild Wars 2 Assets** GitHub Actions workflow provides the same
operation through the existing Workload Identity Federation deployment identity.
The desktop collector downloads a missing manifest icon from the Guild Wars 2
API specified in its packaged `appsettings.json`, verifies its hash, and retains
it in a local cache. The checked-in default at
`desktop/Theorymancer.GuildWars2.Desktop/appsettings.json` targets the deployed
development API; release builds should point this setting at the production API
when it is available.

## OCR Row Model

Capture and frame matching operate on physical visual rows. Windows OCR can
split one rendered row where its text changes color, so adjacent OCR fragments
within half a character height are reassembled left-to-right before capture.
The resulting row receives one color classification based on its aggregated
words. Capture must not merge rows based on punctuation, message text, color,
or an assumed Guild Wars 2 message format. This keeps the UI faithful to the
visible game log and prevents an uncertain OCR row from corrupting later rows.
Frame matching may compare digit groups separated by OCR punctuation as the
same number, but this matcher-only normalization never changes displayed or
stored captured text.

Semantic reconstruction of wrapped or multi-part messages is a later,
Guild Wars 2-specific analysis step. It may use verified game-message formats,
but must not control raw capture, matching, or UI activity output.

Recognized combat-log text appears in the collector's live activity log and is
written locally as JSONL under
`%LOCALAPPDATA%\Theorymancer\guild-wars-2\screen-capture-sessions`. Enable
**Diagnostics** in the collector to inspect live capture and OCR counters plus
an in-memory preview of the calibrated crop. While diagnostics is enabled, the
collector also writes one JSON Lines raw-OCR file per processed frame under
`debug-combat-log-ocr-frames/<capture-start-timestamp>/` relative to its working
directory. That directory also contains `activity_log.jsonl`, which records
visible activity entries and the correlated matcher result for each processed OCR
frame.
Diagnostic previews are not saved.


Game analysis workloads remain independent of the public website and API and
may use Python or another runtime when their requirements justify it.
