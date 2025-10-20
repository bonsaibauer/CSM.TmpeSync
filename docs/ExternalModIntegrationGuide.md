# External Mod Integration Guide

This repository targets the stock CSM + TM:PE setup. If another mod needs to cooperate with CSM TM:PE Sync, use the following hooks:

## 1. Subscribe to state broadcasts

All authoritative updates are emitted through the public CSM connection (`TmpeSyncConnection`). Your mod can register a handler for the same `CommandBase` payloads that appear under `src/CSM.TmpeSync/Net/Contracts/Applied`.

Example outline (pseudocode):

```csharp
CSM.API.Commands.Command.RegisterHandler<LaneArrowApplied>(cmd =>
{
    // cmd.LaneId and cmd.Arrows contain the final authoritative state.
});
```

Never attempt to apply changes locally on the client; listen for the applied message and mirror what the host decided.

## 2. Submit authoritative requests

If you need to request a change, send the corresponding request contract (`src/CSM.TmpeSync/Net/Contracts/Requests`). The CSM host will validate, apply, and either broadcast the result or send back `RequestRejected` with a reason.

## 3. Extend the bridge (when necessary)

New features must be wired into `TmpeAdapter`. Keep the following rules in mind:

- Always call `NetUtil.RunOnSimulation` before touching TM:PE or Unity APIs.
- Acquire the correct `EntityLocks` guard prior to applying changes.
- Use the TM:PE public managers (`TrafficManager.API.*`) when the game exposes them; only fall back to caches for unsupported features.

## 4. Logging

Use `CSM.TmpeSync.Util.Log` if you contribute to this repository. The logger is lightweight and writes to `%APPDATA%\CSM.TmpeSync\csm.tmpe-sync.log`.

## 5. TM:PE UI behaviour

CSM TM:PE Sync keeps the TM:PE interface fully available in multiplayer. Integrations that need to alter button visibility should implement their own UI logic, leaving the default toolset untouched.
