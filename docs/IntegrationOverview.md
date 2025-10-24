# Integration Overview

CSM TM:PE Sync keeps a Cities: Skylines Multiplayer (CSM) lobby authoritative for the most commonly used Traffic Manager: President Edition (TM:PE) features. The implementation is split across three layers:

| Area | Purpose | Entry Points |
|------|---------|--------------|
| Mod lifecycle | Registers a dedicated CSM connection when the mod is enabled and ensures required dependencies are loaded. | `src/CSM.TmpeSync/Mod/MyUserMod.cs` |
| Net handlers | React to CSM requests/applied events, perform validation on the simulation thread, and broadcast authoritative results. | `src/CSM.TmpeSync/Net/Handlers` |
| TM:PE bridge | Uses TM:PE's public API to translate between CSM wire formats and TM:PE data structures, keeping cached state for replay. | `src/CSM.TmpeSync/Tmpe/TmpeAdapter.cs` |

Key characteristics:

- **Host authoritative**: The server validates and applies every TM:PE change, then rebroadcasts the outcome so all clients converge.
- **State capture**: Features such as Hide Crosswalks are mirrored using local caches, allowing the host to share the resulting layout during the session.
- **File-based diagnostics**: Operational logs are written to `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\CSM.TmpeSync\csm.tmpe-sync.log` for later inspection.
- **Full TM:PE access**: TM:PE tools remain available in multiplayer, matching the single-player user experience.

When adding new synchronised features, follow the existing pattern:

1. Define request/applied contracts under `src/CSM.TmpeSync/Net/Contracts`.
2. Implement an authoritative request handler under `src/CSM.TmpeSync/Net/Handlers` that validates entity existence, acquires the appropriate locks and invokes the bridge.
3. Teach `TmpeAdapter` how to read/write the feature using TM:PE types (with a stub fallback).
4. Emit a broadcast with the resulting state so all clients converge.
