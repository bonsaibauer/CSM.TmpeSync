# CSM.TmpeSync Add-on

CSM.TmpeSync extends [Cities: Skylines Multiplayer (CSM)](https://github.com/CitiesSkylinesMultiplayer/CSM) with synchronisation support for [Traffic Manager: President Edition (TM:PE)](https://github.com/CitiesSkylinesMods/TMPE). During a multiplayer session the add-on mirrors changes to TM:PE features such as speed limits, lane arrows, vehicle restrictions and optional [Hide Crosswalks](https://github.com/CitiesSkylinesMods/HideCrosswalks) state between every connected player.

The repository ships everything that is required to build the add-on together with a copy of CSM. Both upstream projects are tracked as Git submodules so that their sources (and build scripts) stay in sync with CSM.TmpeSync.

## Repository Layout

| Path | Description |
| --- | --- |
| src/ | Source code of the CSM.TmpeSync add-on. |
| scripts/ | PowerShell helpers that configure, build, update and install the add-on (and optionally the CSM submodule). |
| submodules/CSM | Git submodule with the current CSM sources including the original build pipeline. |
| submodules/TMPE | Git submodule with the TM:PE fork used for compatibility work. |
| docs/ | Additional documentation about integration details and troubleshooting. |

## Prerequisites

The automation is tested on Windows with PowerShell 7. You will need:

- **PowerShell 7 (pwsh)** – every helper script targets the cross-platform PowerShell runtime.
- **.NET SDK 6.0 or newer** – supplies the dotnet CLI that builds the add-on project.
- **Visual Studio 2022 / Visual Studio 2022 Build Tools** – required when the bundled CSM build script runs (MSBuild.exe).
- **Cities: Skylines** – assemblies are mirrored during the build to resolve mod references.
- **CitiesHarmony** – install the workshop item so CitiesHarmony.Harmony.dll can be mirrored into the local dependency cache.
- **(Optional) TM:PE** – if installed, TrafficManager.dll is mirrored as well which enables direct compilation against TM:PE types.

> The repository never contains proprietary game DLLs. They are copied into lib/ on your machine when the scripts run.

## Initial Setup

1. Clone the repository together with its submodules:

   `powershell
   git clone --recurse-submodules https://github.com/bonsaibauer/CSM.TmpeSync.git
   cd CSM.TmpeSync
   `

   If you already cloned without --recurse-submodules, run the following once inside the repository:

   `powershell
   git submodule update --init --recursive
   `

2. Run the configuration helper so the build scripts know where to locate dependencies and where to place the output:

   `powershell
   pwsh ./scripts/build.ps1 -Configure
   `

   The script prompts for the Cities: Skylines installation path, the CitiesHarmony workshop directory and optional overrides for the TM:PE installation, Steam workshop mirror and mod output folders. Answers are stored in scripts/build-settings.json and reused automatically.

## Build Workflow

scripts/build.ps1 mirrors the workflow of the upstream CSM repository and adds the TM:PE synchronisation build on top. The entry point accepts -Update, -Build and -Install just like the original CSM script and introduces a new -CSM flag to control whether the CSM submodule is processed alongside TM:PE Sync.

### Understanding the -CSM Flag

- **With -CSM**  
  The script forwards -Update, -Build and -Install to the CSM submodule’s uild.ps1, ensuring the multiplayer mod is rebuilt and (optionally) installed before TM:PE Sync is compiled. This guarantees that CSM.API.dll is refreshed and copied into lib/CSM/.

- **Without -CSM**  
  Only TM:PE Sync is processed. The script expects CSM.API.dll to be present in lib/CSM/ (or supplied via -CsmApiDllPath). CSM-specific skip flags (-SkipCsmUpdate, -SkipCsmBuild, -SkipCsmInstall) are ignored in this mode.

### Typical Scenarios

- Refresh dependencies, build both mods and install them into the configured Cities: Skylines mod folders:

  `powershell
  pwsh ./scripts/build.ps1 -Update -Build -Install -CSM
  `

- Iterate on TM:PE Sync only (assumes CSM.API.dll already exists in lib/CSM/):

  `powershell
  pwsh ./scripts/build.ps1 -Build -Install
  `

- Update the dependency cache without building anything else:

  `powershell
  pwsh ./scripts/build.ps1 -Update
  `

- Install the latest build output into the mod folder without touching CSM:

  `powershell
  pwsh ./scripts/build.ps1 -Install
  `

For update-only automation you can also call scripts/update.ps1, which accepts the same dependency-related parameters and the -CSM switch.

### Useful Parameters

- -Configuration <Release|Debug> – selects the TM:PE Sync build configuration (Release by default). When -CSM is used the CSM script is always invoked in Release.
- -ModDirectory <path> – overrides the install folder for CSM.TmpeSync (defaults to the platform-specific Cities: Skylines mods directory).
- -CsmModDirectory <path> – overrides the install folder for CSM when -Install -CSM is used.
- -SteamModsDir <path> – points the build to a Steam workshop mirror (for copying TM:PE Sync results in addition to the regular mod directory).
- -CsmApiDllPath <path> – explicit path to CSM.API.dll when you are not building CSM alongside TM:PE Sync.
- -SkipCsmUpdate, -SkipCsmBuild, -SkipCsmInstall – skip individual CSM steps (only effective when -CSM is present).

All mirrored assemblies land in lib/. Delete the folder if you want to force a clean refresh during the next run.

## Manual Installation

To distribute a build manually, package the contents of src/CSM.TmpeSync/bin/<Configuration>/net35/ together with scripts/install.ps1. On the target machine run:

`powershell
pwsh ./scripts/install.ps1
`

The installer clears any existing copy of the mod and copies the DLL (and optional PDB files) into the default Cities: Skylines mods directory. Use -ModDirectory to override the destination.

## Using the Add-on In-Game

1. Install and enable CSM, CitiesHarmony and TM:PE in the Cities: Skylines Content Manager (**Mods**).
2. Enable the additional entry called **CSM.TmpeSync**.
3. Start a multiplayer session through CSM. Once the connection is active, TM:PE configuration changes made by any player are synchronised to every participant.

## Diagnostics and Logs

- Runtime logs are written to %LOCALAPPDATA%\Colossal Order\Cities_Skylines\CSM.TmpeSync\Logs\CSM.TmpeSync_<YYYY-MM-DD>.log (one file per day).
- Open the folder quickly via **Win + R**, paste the path above and press **Enter**.

Additional background information lives in the docs/ directory (for example [IntegrationOverview.md](docs/IntegrationOverview.md) and [ExternalModIntegrationGuide.md](docs/ExternalModIntegrationGuide.md)).
