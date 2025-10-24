# Development Setup

This guide walks through configuring the streamlined build pipeline for CSM TM:PE Sync. The workflow mirrors dependencies directly from Steam so you only build the sync add-on locally.

## Requirements

Install the following components on a Windows machine:

- **PowerShell 7** – the helper scripts target `pwsh`.
- **Visual Studio Build Tools 2019 or later** – supplies MSBuild, the default compiler used by the script.
- **.NET SDK 6.0 or later (optional)** – provides the `dotnet` CLI fallback when MSBuild is unavailable.
- **Visual Studio Code** – the repository is maintained in VS Code and includes workspace settings and tasks aligned with the scripts.
- **Steam edition of Cities: Skylines** – supplies the base game assemblies located under `Cities_Data/Managed`.

## Steam Workshop dependencies

Subscribe to these Workshop items and allow Steam to download them completely:

| Item | Workshop ID | Default Steam path |
| --- | --- | --- |
| Harmony 2 | 2040656402 | `C:\Program Files (x86)\Steam\steamapps\workshop\content\255710\2040656402` |
| Traffic Manager: President Edition | 1637663252 | `C:\Program Files (x86)\Steam\steamapps\workshop\content\255710\1637663252` |
| Cities: Skylines Multiplayer | 1558438291 | `C:\Program Files (x86)\Steam\steamapps\workshop\content\255710\1558438291` |

The configuration wizard asks you to confirm these subscriptions. Answer **Yes** only when Steam shows all three items as installed. Answering **No** stops the script so you can subscribe and rerun it.

## Running the configuration wizard

From the repository root run:

```powershell
pwsh ./scripts/build.ps1 -Configure
```

The wizard performs the following steps:

1. Lists the available game profiles (currently only **Steam**).
2. Prompts you to confirm the Workshop subscriptions above.
3. Captures the Cities: Skylines installation directory (default: `C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines`).
4. Captures the destination for built mods (default: `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods`).
5. Stores the values in `scripts/build-settings.json` and mirrors dependencies into `lib/` during the next `-Update`.

You can rerun the command at any time to adjust paths or to switch profiles once additional game versions are supported.

## Building and installing

After configuration you can run the typical tasks:

```powershell
pwsh ./scripts/build.ps1 -Update   # copy Harmony, TM:PE, and CSM libraries into lib/
pwsh ./scripts/build.ps1 -Build    # compile the sync mod (Release by default)
pwsh ./scripts/build.ps1 -Install  # copy the compiled DLLs into your Mods folder
```

Combine switches as needed (for example `-Update -Build -Install` in a single invocation). All commands can be executed from Visual Studio Code's integrated terminal or through configured tasks.

## Troubleshooting

- **Missing Workshop files** – rerun `-Configure` and confirm that each dependency folder exists under the Steam Workshop path listed above.
- **`MSBuild.exe` not found** – install Visual Studio Build Tools 2019+ and restart PowerShell so MSBuild is added to `PATH`.
- **`dotnet` not found** – install the .NET SDK if you rely on the CLI fallback and restart PowerShell so the command is on `PATH`.
- **Scripts cannot find the mod directory** – verify the mod root path stored in `scripts/build-settings.json` or pass `-ModDirectory` when running `build.ps1 -Install`.

Need more detail? Check the [README](../README.md) for a high-level overview or open the other documents in `docs/` for architectural guidance.
