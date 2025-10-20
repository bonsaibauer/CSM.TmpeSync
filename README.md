# CSM TM:PE Sync

CSM TM:PE Sync keeps [Cities: Skylines Multiplayer (CSM)](https://github.com/CitiesSkylinesMultiplayer/CSM) host‑authoritative while players edit [Traffic Manager: President Edition (TM:PE)](https://github.com/CitiesSkylinesMods/TMPE) features. Speed limits, lane arrows, lane connections, vehicle restrictions, junction rules, priority signs, parking restrictions, timed/manual traffic lights, and Hide Crosswalks state are all mirrored from the host to every client. TM:PE’s UI stays fully available in multiplayer; the add-on simply ensures that every tool runs through the same authoritative bridge.

The repository contains the add-on sources and keeps CSM and TM:PE as Git submodules so you can build against matching versions.

## Repository Layout

| Path | Description |
| --- | --- |
| src/ | Add-on code: CSM connection, TM:PE bridge, request handlers and utilities. |
| scripts/ | PowerShell helpers to configure, build, install and update dependencies. |
| submodules/CSM | Upstream CSM sources. |
| submodules/TMPE | TM:PE fork used for compatibility testing. |
| docs/ | Reference material (integration guide, logging reference, checklists). |

## Prerequisites

The automation is tested on Windows with PowerShell 7. You will need:

| Tool | Purpose |
| --- | --- |
| PowerShell 7 | All helper scripts target `pwsh`. |
| .NET SDK 6.0+ | Provides the `dotnet` CLI for building the add-on. |
| Visual Studio 2022 / Build Tools | Supplies MSBuild for the legacy CSM pipeline. |
| Cities: Skylines | Assemblies are mirrored locally for compilation. |
| CitiesHarmony | Required dependency for both TM:PE and CSM. |
| TM:PE (optional but recommended) | Mirrors `TrafficManager.dll` so the bridge targets real types. |

> The repository never contains proprietary game DLLs. They are copied into lib/ on your machine when the scripts run.

## Initial Setup

1. Clone the repository with submodules:

   ```powershell
   git clone --recurse-submodules https://github.com/bonsaibauer/CSM.TmpeSync.git
   cd CSM.TmpeSync
   ```

   (Already cloned? Run `git submodule update --init --recursive` once inside the repo.)

2. Configure dependency locations:

   ```powershell
   pwsh ./scripts/build.ps1 -Configure
   ```

   The wizard records installation paths (Cities: Skylines, CitiesHarmony, TM:PE, optional Steam workshop mirror, mod output folders) in `scripts/build-settings.json`.

## Build Workflow

`scripts/build.ps1` aligns with the upstream CSM workflow and adds the TM:PE Sync compilation step. Use the familiar `-Update`, `-Build`, `-Install` switches and add `-CSM` when you want to rebuild the multiplayer core alongside the sync add-on.

### Common tasks

```powershell
pwsh ./scripts/build.ps1 -Update -Build -Install -CSM   # refresh deps, rebuild CSM + TM:PE Sync, install both
pwsh ./scripts/build.ps1 -Build -Install                 # rebuild TM:PE Sync only (assumes lib/CSM/CSM.API.dll exists)
pwsh ./scripts/build.ps1 -Update                         # update dependency cache
pwsh ./scripts/build.ps1 -Install                        # install latest TM:PE Sync build output
```

Parameter highlights:

- `-Configuration <Release|Debug>` – TM:PE Sync build configuration (Release default).
- `-ModDirectory <path>` – custom install directory for TM:PE Sync.
- `-CsmModDirectory <path>` – custom install directory for CSM when `-CSM` + `-Install` are used.
- `-SteamModsDir <path>` – mirrors build output into a Steam workshop folder.
- `-CsmApiDllPath <path>` – explicit path to `CSM.API.dll` when skipping the CSM build.
- `-SkipCsmUpdate|-SkipCsmBuild|-SkipCsmInstall` – bypass individual CSM steps when `-CSM` is present.

All mirrored assemblies land in `lib/`. Remove the folder to force a clean refresh.

## Manual Installation

Package the contents of `src/CSM.TmpeSync/bin/<Configuration>/net35/` together with `scripts/install.ps1`. On the target machine run:

```powershell
pwsh ./scripts/install.ps1
```

The installer clears any existing copy of the mod and copies the DLL (and optional PDB files) into the default Cities: Skylines mods directory. Use -ModDirectory to override the destination.

## Using the Add-on In-Game

1. Enable CSM, CitiesHarmony and TM:PE in the Cities: Skylines Content Manager.
2. Enable **CSM.TmpeSync**.
3. Start or join a CSM session. Once connected, every TM:PE edit made by the host is validated, applied through the bridge, and broadcast to all clients.

## Logging

Operational logs are written to:

```
%APPDATA%\CSM.TmpeSync\csm.tmpe-sync.log
```

Set the environment variable `CSM_TMPE_SYNC_DEBUG=1` before launching the game when you need verbose diagnostics.

## Learn more

- [IntegrationOverview.md](docs/IntegrationOverview.md) – explains the mod architecture.
- [ExternalModIntegrationGuide.md](docs/ExternalModIntegrationGuide.md) – shows how other mods hook into the connection.
- [TmpeFeatureSyncChecklist.md](docs/TmpeFeatureSyncChecklist.md) – verification checklist for multiplayer sessions.
