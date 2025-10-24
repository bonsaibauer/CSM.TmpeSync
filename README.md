# CSM TM:PE Sync

CSM TM:PE Sync keeps [Cities: Skylines Multiplayer (CSM)](https://github.com/CitiesSkylinesMultiplayer/CSM) host‑authoritative while players edit [Traffic Manager: President Edition (TM:PE)](https://github.com/CitiesSkylinesMods/TMPE) features. The add-on lets the host drive TM:PE while every client receives the same authoritative outcome. Development and day-to-day maintenance happen in **Visual Studio Code** with the .NET tooling integrated through the terminal and tasks panel.

The repository contains the add-on sources, PowerShell helpers, and reference submodules. Builds rely on your Steam installation instead of compiling the upstream mods locally.

## Repository Layout

| Path | Description |
| --- | --- |
| src/ | Add-on code: CSM connection, TM:PE bridge, request handlers and utilities. |
| scripts/ | PowerShell helpers to configure, build, install, and update dependencies. |
| lib/ | Cached copies of Harmony, TM:PE, and CSM assemblies mirrored from Steam. |
| docs/ | Reference material (integration guide, logging reference, setup notes). |

## Prerequisites

The automation targets Windows with PowerShell 7. Install the following tools:

| Tool | Purpose |
| --- | --- |
| PowerShell 7 | All helper scripts target `pwsh`. |
| .NET SDK 6.0+ | Provides the `dotnet` CLI for building the add-on. |
| Visual Studio Code | Primary editor and task runner for the project. |
| Steam edition of Cities: Skylines | Supplies the base game assemblies. |

### Steam Workshop subscriptions

The build script mirrors dependencies from Steam Workshop downloads. Make sure the following items are subscribed and fully downloaded in Steam before running the setup wizard:

- [Harmony 2](https://steamcommunity.com/sharedfiles/filedetails/?id=2040656402)
- [Traffic Manager: President Edition](https://steamcommunity.com/sharedfiles/filedetails/?id=1637663252)
- [Cities: Skylines Multiplayer](https://steamcommunity.com/sharedfiles/filedetails/?id=1558438291)

During configuration the script explicitly asks you to confirm these subscriptions. Answering **No** aborts the process so you can subscribe and retry.

## Initial Setup

1. Clone the repository (submodules are optional because the build pulls libraries from Steam):

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

## Build Workflow

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

Dependency updates copy the latest Harmony, TM:PE, and CSM libraries from Steam into `lib/`. Remove that directory to force a clean refresh.

## Manual Installation

Package the contents of `src/CSM.TmpeSync/bin/<Configuration>/net35/` together with `scripts/install.ps1`. On the target machine run:

```powershell
pwsh ./scripts/install.ps1
```

The installer clears any existing copy of the mod and copies the DLL (and optional PDB files) into the default Cities: Skylines mods directory. Use `-ModDirectory` to override the destination.

## Using the Add-on In-Game

1. Enable Harmony, CSM, TM:PE, and CSM.TmpeSync in the Cities: Skylines Content Manager.
2. Start or join a CSM session. Once connected, every TM:PE edit made by the host is validated, applied through the bridge, and broadcast to all clients.

## Logging

Operational logs are written to:

```
%LOCALAPPDATA%\Colossal Order\Cities_Skylines\CSM.TmpeSync\csm.tmpe-sync.log
```

Debug-level entries are always written, so you get full bridge traces without additional setup.

## Learn more

- [DevelopmentSetup.md](docs/DevelopmentSetup.md) – repeatable setup instructions and troubleshooting notes.
- [IntegrationOverview.md](docs/IntegrationOverview.md) – explains the mod architecture.
- [ExternalModIntegrationGuide.md](docs/ExternalModIntegrationGuide.md) – shows how other mods hook into the connection.
- [TmpeFeatureSyncChecklist.md](docs/TmpeFeatureSyncChecklist.md) – verification checklist for multiplayer sessions.
