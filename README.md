[![Repository](https://img.shields.io/badge/Repository-CSM.TmpeSync-blue?style=flat&logo=github)](https://github.com/bonsaibauer/CSM.TmpeSync)
![License](https://img.shields.io/badge/License-MIT-blue)
![Visitors](https://visitor-badge.laobi.icu/badge?page_id=bonsaibauer.CSM.TmpeSync)
[![Report Bug](https://img.shields.io/badge/Report-Bug-critical?style=flat&logo=github)](https://github.com/bonsaibauer/CSM.TmpeSync/issues/new?template=bug_report.yml)
[![Request Feature](https://img.shields.io/badge/Request-Feature-green?style=flat&logo=github)](https://github.com/bonsaibauer/CSM.TmpeSync/issues/new?template=feature_request.yml)
[![Version Mismatch](https://img.shields.io/badge/Report-Version_Mismatch-important?style=flat&logo=github)](https://github.com/bonsaibauer/CSM.TmpeSync/issues/new?template=version_mismatch.yml)
![GitHub Stars](https://img.shields.io/github/stars/bonsaibauer/CSM.TmpeSync?style=social)
![GitHub Forks](https://img.shields.io/github/forks/bonsaibauer/CSM.TmpeSync?style=social)

# 🚧 CSM TM:PE Sync (Beta)

CSM TM:PE Sync keeps [Cities: Skylines Multiplayer (CSM)](https://github.com/CitiesSkylinesMultiplayer/CSM) host-authoritative while players edit [Traffic Manager: President Edition (TM:PE)](https://github.com/CitiesSkylinesMods/TMPE) features. After the latest restructuring the synchronization layer has been rewritten feature by feature: every TM:PE tool now owns its own command handlers, network payloads, and harmony patches.

> **Beta disclaimer**
>
> The rewrite is still stabilising. Multiplayer sessions should treat this build as experimental and report issues with logs. Use the mod in controlled test games before deploying it to long-running cities.

## Feature documentation

Detailed docs live alongside the code. Every feature description is bilingual (German first, English second) and explains file layout, message flow, and known limitations.

| Feature | Documentation | Supported |
| --- | --- | --- |
| Clear Traffic | [docs/ClearTraffic.md](docs/ClearTraffic.md) | ✅ |
| Junction Restrictions | [docs/JunctionRestrictions.md](docs/JunctionRestrictions.md) | ✅ |
| Lane Arrows | [docs/LaneArrows.md](docs/LaneArrows.md) | ✅ |
| Lane Connector | [docs/LaneConnector.md](docs/LaneConnector.md) | ✅ |
| Parking Restrictions | [docs/ParkingRestrictions.md](docs/ParkingRestrictions.md) | ✅ |
| Priority Signs | [docs/PrioritySigns.md](docs/PrioritySigns.md) | ✅ |
| Speed Limits | [docs/SpeedLimits.md](docs/SpeedLimits.md) | ✅ |
| Timed Traffic Lights | [docs/TimedTrafficLights.md](docs/TimedTrafficLights.md) | ❌ |
| Toggle Traffic Lights | [docs/ToggleTrafficLights.md](docs/ToggleTrafficLights.md) | ✅ |
| Vehicle Restrictions | [docs/VehicleRestrictions.md](docs/VehicleRestrictions.md) | ✅ |

The Lane Connector guide has been refreshed to cover the lane-ID apply pipeline rewrite - revisit `docs/LaneConnector.md` for the new retry/backoff flow and message format details.

Check the corresponding `src/CSM.TmpeSync.<FeatureName>/` project when you extend or debug a specific tool. Each project ships its own bootstrapper and harmony integration and can be iterated in isolation.

## Repository layout

| Path | Description |
| --- | --- |
| `src/CSM.TmpeSync/` | Core mod: dependency detection, logging, feature registration, and shared services. |
| `src/CSM.TmpeSync.<Feature>/` | Independent feature modules created by the restructure. Each module owns handlers, messages, services, and tests where applicable. |
| `docs/` | Bilingual feature manuals and architectural notes generated during the rewrite. |
| `scripts/` | PowerShell helpers to configure, build, install, and update dependencies. |
| `subtrees/` | Upstream references (for example TM:PE) vendored as Git subtrees for comparison only. |

## Prerequisites

The automation targets Windows with PowerShell 7. Install the following tools:

| Tool | Purpose |
| --- | --- |
| PowerShell 7 | All helper scripts target `pwsh`. |
| Visual Studio Build Tools 2019+ (MSBuild) | Provides MSBuild, the default compiler used by the build script. |
| .NET SDK 6.0+ (optional) | Supplies the `dotnet` CLI fallback when MSBuild is unavailable. |
| Visual Studio Code | Primary editor and task runner for the project. |
| Steam edition of Cities: Skylines | Supplies the base game assemblies. |

### Steam Workshop subscriptions

The build script mirrors dependencies from Steam Workshop downloads. Make sure the following items are subscribed and fully downloaded in Steam before running the setup wizard:

- [Harmony 2](https://steamcommunity.com/sharedfiles/filedetails/?id=2040656402)
- [Traffic Manager: President Edition](https://steamcommunity.com/sharedfiles/filedetails/?id=1637663252)
- [Cities: Skylines Multiplayer](https://steamcommunity.com/sharedfiles/filedetails/?id=1558438291)

During configuration the script explicitly asks you to confirm these subscriptions. Answering **No** aborts the process so you can subscribe and retry.

## Initial setup

1. Clone the repository (subtrees are optional because the build pulls libraries from Steam):

   ```powershell
   git clone https://github.com/bonsaibauer/CSM.TmpeSync.git
   cd CSM.TmpeSync
   ```

2. Launch the configuration wizard:

   ```powershell
   pwsh ./scripts/build.ps1 -Configure
   ```

   The wizard guides you through:

   - Selecting the Steam profile (currently the only option).
   - Confirming that Harmony, TM:PE, and CSM are subscribed in Steam.
   - Entering the Cities: Skylines installation directory (default: `C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines`).
   - Entering the local mod installation root (default: `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods`).

   Defaults for the Steam Workshop cache and dependency mirrors are pre-populated and stored alongside the paths you provide in `scripts/build-settings.json`.

## Build workflow

`scripts/build.ps1` drives the entire process:

```powershell
pwsh ./scripts/build.ps1 -Update -Build -Install   # refresh dependencies, build the mod, install to the configured directory
pwsh ./scripts/build.ps1 -Build                    # build only (assumes dependencies are already mirrored)
pwsh ./scripts/build.ps1 -Install                  # copy the latest build output into your mods folder
```

Key parameters:

- `-Configuration <Release|Debug>` – build configuration (Release by default).
- `-Profile Steam` – explicitly target the Steam profile when multiple profiles become available.
- `-ModDirectory <path>` – override the installation target for a single run.
- `-GameDirectory`, `-SteamModsDir`, `-HarmonySourceDir`, `-CsmSourceDir`, `-TmpeSourceDir` – override paths captured in the profile when needed.

Dependency updates copy the latest Harmony, TM:PE, and CSM libraries from Steam into the working cache. Remove those directories to force a clean refresh. The build prefers MSBuild from Visual Studio Build Tools and automatically falls back to the `dotnet` CLI only when MSBuild is unavailable.

## Manual installation

Package the contents of `src/CSM.TmpeSync/bin/<Configuration>/net35/` together with `scripts/install.ps1`. On the target machine run:

```powershell
pwsh ./scripts/install.ps1
```

The installer clears any existing copy of the mod and copies the DLL (and optional PDB files) into the default Cities: Skylines mods directory. Use `-ModDirectory` to override the destination.

## Using the add-on in-game

1. Enable Harmony, CSM, TM:PE, and CSM.TmpeSync (Beta) in the Cities: Skylines Content Manager.
2. Start or join a CSM session. Once connected, every TM:PE edit made by the host is validated, applied through the feature-specific bridge, and broadcast to all clients.
3. Monitor the daily log files (`%LOCALAPPDATA%\Colossal Order\Cities_Skylines\CSM.TmpeSync\log-<YYYY-MM-DD>.log`) for warnings while the Beta stabilises.

## Logging

Operational logs are written to daily rolling files:

```
%LOCALAPPDATA%\Colossal Order\Cities_Skylines\CSM.TmpeSync\log-<YYYY-MM-DD>.log
```

Debug-level entries are always written, so you get full bridge traces without additional setup.

## Contributing feedback

File issues with reproduction steps, the latest `log-<YYYY-MM-DD>.log`, and the feature name that triggered the problem. The modular architecture lets us iterate on one feature at a time, so accurate reports keep the Beta moving forward.

## Buy Me A Coffee
If this project has helped you in any way, do buy me a coffee so I can continue to build more of such projects in the future and share them with the community!

<a href="https://buymeacoffee.com/bonsaibauer" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/default-orange.png" alt="Buy Me A Coffee" height="41" width="174"></a>
