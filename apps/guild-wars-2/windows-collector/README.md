# Theorymancer Guild Wars 2 Windows Collector

This project is a Theorymancer-owned Windows DLL scaffold for a future,
read-only Guild Wars 2 combat collector. It has no ArcDPS or EVTC dependency.

## Current scope

The DLL acts as a `d3d11.dll` proxy. At configuration time, it reads the
64-bit system `d3d11.dll` export table and generates one ordinal-preserving
forwarding stub per export. Each stub tail-calls the matching entry point from
the explicitly loaded System32 DLL. The two device-creation exports also write
a diagnostic line to:

```text
%LOCALAPPDATA%\Theorymancer\guild-wars-2-collector.log
```

It does not inspect Guild Wars 2 memory, capture combat events, render an
overlay, change input, or make network requests. The diagnostic log only
records the host executable path and process ID. If a proxy-defined endpoint
is absent from the end user's System32 DLL, it also records:

```text
system_d3d11_export_missing ordinal=<ordinal> name=<endpoint>
```

The proxy cannot add or remove its own exports at runtime, so this diagnostic
does not alter `GetProcAddress` behavior. It makes a system-DLL mismatch
actionable before the proxy fails the attempted call.

## Build

Use a Windows developer command prompt with Visual Studio 2022 and CMake 3.24
or newer:

```powershell
cmake -S apps/guild-wars-2/windows-collector -B build/gw2-collector -G Ninja
cmake --build build/gw2-collector --config Release
ctest --test-dir build/gw2-collector --output-on-failure
```

The result is `d3d11.dll`. Build it on the Windows installation where it will
be tested: the generated proxy matches that installation's D3D11 export names
and ordinals. The test suite verifies that export table and creates a WARP
device through the proxy.

This remains an experimental, unsigned development build. Test it only on a
separate Guild Wars 2 installation, keep a copy of the DLL outside the game
directory, and remove the local `d3d11.dll` to restore normal system-DLL
loading. Matching exports does not make a proxy invisible to integrity checks,
anti-cheat, or DLL enumeration.

## CI artifacts

Pushing changes under this directory to a `feature` or `feature/**` branch
builds the Windows x64 DLL on GitHub Actions. The workflow initializes MSVC
and uses Ninja, avoiding a dependency on a specific Visual Studio version. The
completed workflow run has a `gw2-windows-collector` artifact containing `d3d11.dll` and
`collector-build.txt`, which records the source commit, proxy SHA-256 hash,
and the System32 D3D11 hash used to generate and test it.

Artifacts are retained for seven days and are for private development testing
only. They are not published releases.

See [PLAN.md](PLAN.md) for the staged collector plan and safety boundaries.
