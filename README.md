# CSM.TmpeSync Add-on

CSM.TmpeSync extends [Cities: Skylines Multiplayer (CSM)](https://github.com/CitiesSkylinesMultiplayer/CSM) with synchronisation
support for [Traffic Manager: President Edition (TM:PE)](https://github.com/CitiesSkylinesMods/TMPE). While a multiplayer session is
running, the add-on mirrors changes to TM:PE features such as speed limits, lane arrows, vehicle restrictions and optional
[Hide Crosswalks](https://github.com/CitiesSkylinesMods/HideCrosswalks) state between every connected player.

The repository contains everything that is required to build the add-on together with a copy of CSM. Both the multiplayer mod and
TM:PE live inside the tree under `submodules/` so that the integration hooks can be updated together with upstream changes.

## Repository layout

| Path | Description |
| --- | --- |
| `src/` | Source code of the CSM.TmpeSync add-on. |
| `scripts/` | PowerShell helpers used to configure, build and install the add-on (and the bundled CSM copy). |
| `submodules/CSM` | Git subtree with the current CSM sources including the original build script. |
| `submodules/TMPE` | Git subtree with a TM:PE fork used for compatibility work. |
| `docs/` | Additional documentation about the integration and maintenance workflows. |

## Prerequisites

The build automation is tested on Windows with PowerShell 7. The following components are required:

* **PowerShell 7 (`pwsh`)** – all helper scripts depend on the cross-platform PowerShell executable.
* **.NET SDK 6.0 or newer** – supplies the `dotnet` CLI that builds the add-on project.
* **Visual Studio 2022 / Visual Studio 2022 Build Tools** – required by the bundled CSM build script to provide `MSBuild.exe`.
* **Cities: Skylines** – the game assemblies are copied during the build to resolve the mod references.
* **CitiesHarmony** – install the workshop item so that `CitiesHarmony.Harmony.dll` can be mirrored into the local dependency
  cache.
* **(Optional) TM:PE** – if it is installed, `TrafficManager.dll` is mirrored as well which enables direct compilation against
  TM:PE types.

> ℹ️ The repository does **not** contain proprietary game DLLs. They are mirrored into `lib/` on your machine when the build script
> runs.

## Initial setup

1. Clone the repository:

   ```powershell
   git clone https://github.com/bonsaibauer/CSM.TmpeSync.git
   cd CSM.TmpeSync
   ```

   The copies of CSM and TM:PE are managed through Git subtree merges and are already included in the repository.

2. Run the configuration step once so the build script knows where to find the required assemblies and where to place the output:

   ```powershell
   pwsh ./scripts/build.ps1 -Configure
   ```

   You will be prompted for the Cities: Skylines installation path and the CitiesHarmony workshop directory. Optional questions
   let you override the target folders for the CSM and CSM.TmpeSync mods as well as the Steam `workshop/content/255710` mirror
   directory. The answers are stored in `scripts/build-settings.json` and re-used automatically.

## Building and installing

`scripts/build.ps1` mirrors the workflow from the upstream CSM repository and orchestrates both the CSM build and the
CSM.TmpeSync compilation. The most common commands are:

* Build everything (including CSM) and install both mods into the configured Cities: Skylines mod folders:

  ```powershell
  pwsh ./scripts/build.ps1 -Build -Install
  ```

* Build the add-on without reinstalling CSM (faster iteration while working on CSM.TmpeSync only):

  ```powershell
  pwsh ./scripts/build.ps1 -Build -SkipCsmBuild -SkipCsmInstall
  ```

* Refresh the dependency cache from the game, Harmony and TM:PE before building:

  ```powershell
  pwsh ./scripts/build.ps1 -Update -Build
  ```

Useful parameters:

* `-Configuration <Release|Debug>` – switches between build configurations (default: `Release`).
* `-ModDirectory <path>` – overrides the installation folder for CSM.TmpeSync. When omitted the platform specific default is
  used (for example `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\CSM.TmpeSync` on Windows).
* `-CsmModDirectory <path>` – overrides the CSM installation folder when `-Install` is present.
* `-SteamModsDir <path>` – also copies the resulting DLL into the specified Steam `Files/Mods` directory (if desired for testing).
* `-SkipCsmUpdate`, `-SkipCsmBuild`, `-SkipCsmInstall` – allow skipping the individual steps managed by the CSM build script.

The script writes all mirrored assemblies to `lib/`. Delete the folder to force a clean refresh during the next `-Build` run.

### Manual installation of a build

Release packages can be distributed by copying the contents of `src/CSM.TmpeSync/bin/<Configuration>/net35/` together with
`scripts/install.ps1`. On another machine run:

```powershell
pwsh ./scripts/install.ps1
```

The script clears any existing installation of the mod and copies the DLL (and optional PDB files) into the target directory. Use
`-ModDirectory` to override the destination when required.

## Using the add-on in game

1. Install and enable CSM, CitiesHarmony and TM:PE inside the Cities: Skylines Content Manager (**Mods**).
2. Enable the additional mod entry called **CSM.TmpeSync**.
3. Start a multiplayer session through CSM. Once the connection is active, TM:PE configuration changes made by any player are
   propagated to every participant.

## Diagnostics and logs

* Runtime logs are written to `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\CSM.TmpeSync\Logs\CSM.TmpeSync_<YYYY-MM-DD>.log` (one file per day).
* The folder can be opened quickly via **Win + R** → paste the path above → press **Enter**.

For a more detailed explanation of the integration and maintenance workflows consult the documents inside the `docs/` folder –
for example [IntegrationOverview.md](docs/IntegrationOverview.md) and [CSM-Subtree-Setup.md](docs/CSM-Subtree-Setup.md).
