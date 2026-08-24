# Theorymancer Guild Wars 2 Windows Collector

This project is a Theorymancer-owned Windows DLL scaffold for a future,
read-only Guild Wars 2 combat collector. It has no ArcDPS or EVTC dependency.

## Current scope

The DLL acts as a minimal `d3d11.dll` proxy for the two Direct3D entry points
used to create a D3D11 device. It forwards each call to the system DLL and
writes a diagnostic line to:

```text
%LOCALAPPDATA%\Theorymancer\guild-wars-2-collector.log
```

It does not inspect Guild Wars 2 memory, capture combat events, render an
overlay, change input, or make network requests. The diagnostic log only
records the host executable path and process ID.

## Build

Use a Windows developer command prompt with Visual Studio 2022 and CMake 3.24
or newer:

```powershell
cmake -S apps/guild-wars-2/windows-collector -B build/gw2-collector -G Ninja
cmake --build build/gw2-collector --config Release
```

The result is `d3d11.dll`. Do not install it into a Guild Wars 2 directory yet:
the proxy exports are deliberately incomplete until compatibility testing
against a supported GW2 build is added.

## CI artifacts

Pushing changes under this directory to a `feature` or `feature/**` branch
builds the Windows x64 DLL on GitHub Actions. The completed workflow run has a
`gw2-windows-collector` artifact containing `d3d11.dll` and
`collector-build.txt`, which records the source commit and SHA-256 hash.

Artifacts are retained for seven days and are for private development testing
only. They are not published releases and must not be installed into a Guild
Wars 2 directory yet.

See [PLAN.md](PLAN.md) for the staged collector plan and safety boundaries.
