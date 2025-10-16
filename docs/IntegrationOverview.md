# Integration overview: CSM.TmpeSync vs. TM:PE & CSM

This note summarises how the **CSM.TmpeSync** add-on is structured, which
interfaces it uses from [Traffic Manager: President Edition (TM:PE)] and
[Cities: Skylines Multiplayer (CSM)], and how the current architecture differs
from earlier integration attempts.

## Project layout inside CSM.TmpeSync

The add-on is organised as a standalone .NET 3.5 class library. The project file
`src/CSM.TmpeSync/CSM.TmpeSync.csproj` targets the real Cities: Skylines
assemblies, Harmony, `CSM.API.dll` and – if available – `TrafficManager.dll` and
expects those DLLs to be present during every build.【F:src/CSM.TmpeSync/CSM.TmpeSync.csproj†L1-L198】

Source folders are grouped by responsibility:

- `Mod/` contains the CSM mod entry point and the connection to the multiplayer
  service.【F:src/CSM.TmpeSync/CSM.TmpeSync.csproj†L118-L135】
- `Net/` encapsulates all network contracts (requests, applied events, locks) and
  their handlers for host-authoritative execution and deferred operations.【F:src/CSM.TmpeSync/CSM.TmpeSync.csproj†L143-L180】
- `Snapshot/` exports the current TM:PE state when a player connects to
  synchronise newcomers.【F:src/CSM.TmpeSync/CSM.TmpeSync.csproj†L182-L190】
- `Tmpe/` and `HideCrosswalks/` provide adapters that either call the actual mod
  APIs or gracefully skip behaviour when the mods are missing.【F:src/CSM.TmpeSync/CSM.TmpeSync.csproj†L136-L141】【F:src/Tmpe/TmpeAdapter.cs†L9-L468】
- `Util/` bundles infrastructure such as logging, entity locks and, most
  importantly, the compatibility layer for the CSM API.【F:src/CSM.TmpeSync/CSM.TmpeSync.csproj†L136-L149】【F:src/Util/CsmCompat.cs†L1-L409】

## TM:PE integration

`TmpeAdapter` discovers at runtime whether the real TM:PE assembly is loaded and
logs a warning otherwise. Every synchronisation command (speed limits, lane
arrows, vehicle restrictions, lane connector etc.) is received by the adapter,
stored internally and – if TM:PE is present – passed through to the respective
managers.【F:src/Tmpe/TmpeAdapter.cs†L9-L468】 The snapshot export uses the same
adapter state so the behaviour is identical between single- and multiplayer
sessions.【F:docs/TmpeFeatureSyncChecklist.md†L9-L186】 No separate "TM:PE API" is
required; reflection is used to access the existing assembly.

Unlike the historical `CSM-API-Implementation` branch inside TM:PE (which tried
embedding multiplayer features into the main mod), CSM.TmpeSync keeps all
multiplayer-specific classes inside this add-on. Upstream TM:PE updates remain
independent, while the add-on connects via clearly defined adapter points.【F:src/Tmpe/TmpeAdapter.cs†L51-L60】

## Using the CSM API

The add-on relies on static methods in `CSM.API.Command` to send data to clients
(`SendToClient`, `SendToClients`, `SendToAll`) and on registration hooks to mount
a dedicated message type. `CsmCompat` resolves these methods via reflection and
logs which signatures were found.【F:src/Util/CsmCompat.cs†L12-L104】 If hooks are
missing the log will contain entries such as "Unable to register connection –
CSM.API register hook missing" and no TM:PE data is exchanged.【F:src/Util/CsmCompat.cs†L297-L409】

The multiplayer flow looks as follows:

1. `TmpeSyncConnection` registers a new channel with the CSM API during startup
   and wires up request/applied handlers.【F:src/Mod/TmpeSyncConnection.cs†L1-L111】【F:src/Util/CsmCompat.cs†L297-L409】
2. Clients send changes through `CSM.API.Command.SendToServer`; the server
   validates them, applies the result in the simulation and broadcasts the
   confirmed change via `SendToClients/SendToAll`.【F:src/Net/Handlers/SetSpeedLimitRequestHandler.cs†L14-L66】
3. Snapshots and deferred operations ensure that late joiners receive the full
   TM:PE configuration.【F:docs/TmpeFeatureSyncChecklist.md†L9-L186】

## Debugging implications

- **TM:PE does not need to be modified** as long as `TrafficManager.dll` exposes
  the known manager types – the adapter handles optional functionality itself.【F:src/Tmpe/TmpeAdapter.cs†L9-L66】
- **CSM must export the expected API hooks.** Consult the log reference
  (`docs/LogReference.md`) to verify that `CsmCompat` finds `SendToClient` and
  `RegisterConnection`. If they are missing, rebuild CSM with the full API.【F:docs/LogReference.md†L23-L38】
- **Repository structure:** CSM.TmpeSync focuses on the multiplayer glue and the
  adapter layer, whereas the official TM:PE repository retains the full traffic
  logic. The responsibilities stay cleanly separated and upstream updates are
  easier to adopt.【F:src/Tmpe/TmpeAdapter.cs†L51-L60】

This overview should help you understand the differences between the
repositories, the required integration hooks and the expected data flow so you
can diagnose hook issues quickly.
