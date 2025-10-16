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

## Prerequisites

* PowerShell 7 (`pwsh`) – required to run the scripts in `scripts/`.
* Visual Studio 2022 (or the Visual Studio 2022 Build Tools) – provides `MSBuild.exe` for the CSM build.
* .NET SDK 6.0 (or newer) – used to build the add-on via the `dotnet` CLI.
* To compile against the real game assemblies:
  * Cities: Skylines must be installed locally (Steam or GOG). The DLLs from `Cities_Data/Managed` are required.
  * [CitiesHarmony](https://steamcommunity.com/workshop/filedetails/?id=2040656402) (Workshop 2040656402) must be installed so that `CitiesHarmony.Harmony.dll` is available.
  * The CSM main project is built automatically through the CSM subtree. If you skip the CSM build step, ensure that `CSM.API.dll` is available either from the subtree (`submodules/CSM/`) or inside a local `lib/` folder.
  * Optional: TM:PE should be installed when you reference `TrafficManager.dll` directly.

> ⚠️ Do not add proprietary game DLLs to the repository. Reference the files from your local installation instead.

## Build script (`pwsh`)

The repository mirrors the CSM build flow: `scripts/build.ps1` invokes the CSM build script first and then compiles the add-on.

### Preparation

1. Optional: Provide a `Directory.Build.props` file at the repository root so that paths to your game installation can be resolved automatically:

   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <Project>
     <PropertyGroup>
       <!-- Adjust the paths to your installation -->
       <CitiesSkylinesDir>C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines</CitiesSkylinesDir>
       <HarmonyDllDir>C:\Program Files (x86)\Steam\steamapps\workshop\content\255710\2040656402</HarmonyDllDir>
       <CsmApiDllPath>C:\Program Files (x86)\Steam\steamapps\workshop\content\255710\1558438291\CSM.API.dll</CsmApiDllPath>
       <!-- Optional, only if needed: -->
       <TmpeDir>C:\Program Files (x86)\Steam\steamapps\workshop\content\255710\1637663252</TmpeDir>
     </PropertyGroup>
   </Project>
   ```

   The build script reads these properties automatically.

If you work with your own fork, keep the embedded CSM sources up to date via the workflow described in [CSM subtree setup & workflow](docs/CSM-Subtree-Setup.md).

### Common commands

* Release build including installation (CSM + add-on):

   ```powershell
   pwsh ./scripts/build.ps1 -Build -Install
   ```

* Debug build without rebuilding the CSM subtree:

   ```powershell
   pwsh ./scripts/build.ps1 -Build -Configuration Debug -SkipCsmBuild -SkipCsmInstall
   ```

* Install the latest build outputs:

   ```powershell
   pwsh ./scripts/build.ps1 -Install
   ```

* Update game assemblies and build immediately afterwards:

   ```powershell
   pwsh ./scripts/build.ps1 -Update -Build -GameDirectory "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines"
   ```

### Useful parameters

* `-ModDirectory` controls the installation target for CSM.TmpeSync (default: `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\CSM.TmpeSync`).
* `-CsmModDirectory` configures the installation target for the CSM subtree build output.
* `-SkipCsmBuild` and `-SkipCsmInstall` skip the respective subtree steps.

### Installing a release package

A separate `install.ps1` is available for distributed build artefacts. Run it in the same directory as the `CSM.TmpeSync*.dll` files:

```powershell
pwsh ./scripts/install.ps1
```

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
