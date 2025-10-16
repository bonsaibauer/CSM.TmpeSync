# CSM.TmpeSync Add-on

This repository provides an add-on for [Cities: Skylines Multiplayer (CSM)](https://github.com/CitiesSkylinesMultiplayer/CSM)
that synchronises the configuration of [Traffic Manager: President Edition (TM:PE)](https://github.com/CitiesSkylinesMods/TMPE)
and – optionally – the hidden crosswalk state from [Hide Crosswalks](https://github.com/CitiesSkylinesMods/HideCrosswalks).
The following sections explain how to build the project locally, install the add-on, and collect diagnostic information.

## Additional references

* [Official CSM repository](https://github.com/CitiesSkylinesMultiplayer/CSM) – upstream multiplayer mod this add-on extends.
* [Official TM:PE repository](https://github.com/CitiesSkylinesMods/TMPE) – reference for current TM:PE functionality.
* [bonsaibauer/CSM fork](https://github.com/bonsaibauer/CSM) – embedded via git subtree at `submodules/CSM`.
* [bonsaibauer/TMPE fork](https://github.com/bonsaibauer/TMPE) – embedded via git subtree at `submodules/TMPE`.
* [Historical CSM/TM:PE integration attempt](https://github.com/MightyWizard/TMPE/tree/CSM-API-Implementation) – older ideas for an API integration.
* [Integration overview (this repository)](docs/IntegrationOverview.md) – architecture, APIs in use, and required hooks.
* [CSM subtree setup & workflow](docs/CSM-Subtree-Setup.md) – how CSM is embedded and updated inside this repository.

Both subtrees stay connected to their official upstream repositories so that updates can be merged regularly while keeping local adjustments in sync.

## Build & install

### Prerequisites

* PowerShell 7 (`pwsh`) – required to run the scripts in `scripts/`.
* Visual Studio 2022 (or the Visual Studio 2022 Build Tools) – provides `MSBuild.exe` for the CSM subtree build.
* .NET SDK 6.0 (or newer) – used to build the add-on via the `dotnet` CLI.
* Cities: Skylines must be installed locally so that the script can copy the game assemblies. The Steam version is supported out of the box; GOG works as long as the `Cities_Data/Managed` folder is available.
* Install the [CitiesHarmony](https://steamcommunity.com/workshop/filedetails/?id=2040656402) workshop item. Optional but recommended: keep TM:PE installed so that `TrafficManager.dll` can be mirrored as well.

> ⚠️ Do not add proprietary game DLLs to the repository. The build script collects everything inside `lib/` during the build.

### One-time configuration

1. Initialise the configuration once so that the build script knows where Cities: Skylines is installed:

   ```powershell
   pwsh ./scripts/build.ps1 -Configure
   ```

   The prompt only asks for the Cities: Skylines installation directory. Optional questions let you override the destination folders for the CSM and CSM.TmpeSync mods. All answers are stored in `scripts/build-settings.json` and re-used automatically.

2. (Optional) Keep the embedded CSM sources in `submodules/CSM/` up to date by following the workflow in [CSM subtree setup & workflow](docs/CSM-Subtree-Setup.md).

### Working with the build script

`scripts/build.ps1` mirrors the upstream CSM pipeline: it can update the CSM subtree, build CSM, refresh dependencies, compile the add-on and install both mods. After the initial configuration only the Cities: Skylines path is required – Harmony, TM:PE and the CSM API DLL are discovered automatically via the Steam workshop structure and mirrored into `lib/` for future builds.

Common examples:

* Release build including installation of both CSM and the add-on:

   ```powershell
   pwsh ./scripts/build.ps1 -Build -Install
   ```

* Debug build without rebuilding or reinstalling the CSM subtree:

   ```powershell
   pwsh ./scripts/build.ps1 -Build -Configuration Debug -SkipCsmBuild -SkipCsmInstall
   ```

* Update the cached game assemblies and rebuild in one go:

   ```powershell
   pwsh ./scripts/build.ps1 -Update -Build
   ```

Useful parameters:

* `-ModDirectory` overwrites the installation target for CSM.TmpeSync (default: `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\CSM.TmpeSync`).
* `-CsmModDirectory` sets the CSM installation target when `-Install` is used.
* `-SteamModsDir` optionally copies the resulting DLL to the Steam `Cities_Skylines/Files/Mods` folder when available.
* `-SkipCsmBuild`, `-SkipCsmInstall` and `-SkipCsmUpdate` control the individual CSM subtree steps.

The script keeps a dependency cache under `lib/`. Deleting the folder forces a fresh copy of all referenced assemblies the next time `-Build` runs.

### Installing a release package

Distribute the contents of `src/CSM.TmpeSync/bin/<Configuration>/net35/` together with `scripts/install.ps1`. Installation on another machine works as follows:

```powershell
pwsh ./scripts/install.ps1
```

The script removes any previous installation of CSM.TmpeSync, recreates the mod folder and copies both DLL and PDB files.

## Using the add-on in game

1. Start Cities: Skylines and enable both CSM and the add-on "CSM.TmpeSync" in the Content Manager (**Mods**). The add-on only stays active while CSM **and** Harmony are enabled.
2. Ensure that the CSM host and all clients have TM:PE installed and enabled.
3. Once the multiplayer session is running, the add-on synchronises TM:PE changes such as speed limits, lane arrows, lane connections, junction restrictions and more between all players.

## Logs

* During gameplay, all add-on messages are written to `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\CSM.TmpeSync\Logs\CSM.TmpeSync.log`.
* Open the folder via `Win + R`, enter `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\CSM.TmpeSync\Logs` and press Enter.
* The file can be opened in any text editor for a detailed run-through of the add-on's actions and potential warnings.
* Messages are also mirrored to the in-game debug panel (`Esc` → cog wheel → **Debug Log**) and, if `-logfile` is set, to the Unity player log.

### Redirecting the Unity player log (`-logfile`)

* **Steam (recommended):**
  1. Open the Steam properties for Cities: Skylines (**Right click** → **Properties**).
  2. Add launch options similar to the following:

     ```
     -logFile "%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines\Player.log"
     ```

     The directory is created automatically when the game starts.
* **Launching via shortcut or `Cities.exe`:**
  * Extend the target with `-logFile "C:\Path\To\CitiesPlayer.log"` or launch via console:

    ```powershell
    "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities.exe" -logFile "C:\Temp\CitiesPlayer.log"
    ```

  * The destination must be writable. Point it to a shared folder if multiple players need to exchange logs quickly.
* Remove the launch option/argument again if you prefer the default `Player.log` location.

## Multiplayer smoke test

To verify that synchronisation works end-to-end:

1. Build the project and copy the DLL to the mods folder of the machine hosting the CSM server.
2. Enable the add-on in the Content Manager and start the game alongside the CSM server (either via `CSM.exe` or the integrated host button).
3. Connect to the server through the CSM UI and load a shared save.
4. Pick a road on the host, change the TM:PE speed limit and confirm that the client updates automatically.
5. Repeat in the opposite direction (client changes, host observes) to ensure bidirectional synchronisation.
6. If changes do not show up, review the logs of both server and client – missing or disabled mods are the most common cause.

With these steps you can confirm that the add-on keeps TM:PE data in sync across all connected players.
