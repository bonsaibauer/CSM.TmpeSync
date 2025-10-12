# Log reference for CSM.TmpeSync

This document summarises the most common log lines that appear when the mod starts and how to interpret them.

## Startup sequence

During `OnEnabled` the mod will:

1. Log that it is checking dependencies.
2. Report whether required mods (CSM and Harmony) were found.
3. Attempt to register the TM:PE connection with CSM.

```
[INFO] [CSM.TmpeSync] Enable... checking deps
[DEBUG] [CSM.TmpeSync] Dependency check -> CSM: True, Harmony: True
[INFO]  [CSM.TmpeSync] Dependencies available. Registering TM:PE sync connection with CSM.
```

These entries indicate that the dependency probe ran successfully and the registration step is starting.

## CSM compatibility reflection

When the compatibility helper initialises, it reflects the `CSM.API` assembly to find the communication hooks it needs. A healthy setup will log the resolved method names:

```
[DEBUG] [CSM.TmpeSync] CSM compat initialised. SendToClient=CSM.API.Command.SendToClient(...); SendToAll=...; Register=CSM.API.Helper.RegisterConnection(...); Unregister=...
```

If you instead see `<missing method>` in the debug line, or follow-up warnings such as:

```
[WARN] [CSM.TmpeSync] Unable to register connection – CSM.API register hook missing
[WARN] [CSM.TmpeSync] TM:PE sync connection could not be registered with CSM. Synchronisation remains inactive.
```

it means the current CSM installation does not expose the expected `SendToClient`/`SendToAll` methods on `CSM.API.Command` or the `RegisterConnection` hook in the API. Without those hooks, the sync channel cannot be registered and no TM:PE data will be exchanged. Update to a CSM build that includes these APIs (or ensure the real game libraries are loaded instead of the local stubs) before continuing tests.

## Multiplayer role detection

If the mod cannot read the multiplayer role from the CSM API it logs:

```
[WARN] [CSM.TmpeSync] Unable to query current CSM multiplayer role: ... CurrentRole property is unavailable.
```

This happens when the properties probed in `CsmCompat` (`Command.CurrentRole`, `Command.Role`, `Command.IsServer`, etc.) are missing. The mod will fall back to treating the environment as unknown and will not synchronise TM:PE changes until the role can be resolved.

## Expected next steps

As long as the warnings persist, TM:PE synchronisation stays inactive. Verify that:

- You are running against a CSM version that contains the reflective hooks.
- The game is started (the stub DLLs used for editor builds intentionally leave those members empty).
- Harmony and TM:PE are enabled so the mod can complete the registration path once the hooks are present.

Once the hooks are available the warnings disappear and the log ends with:

```
[INFO] [CSM.TmpeSync] CSM connection ready – TM:PE synchronisation active.
```

At that point the log output matches the expected plan.

## Stub simulation (no live clients)

When running the project outside the real game (`GAME` flag unset), the bundled CSM API stub now simulates the communication hooks. In this mode the log includes additional entries prefixed with `[CSM.API Stub]`:

```
[INFO] [CSM.TmpeSync] [CSM.API Stub] Registered connection 'TM:PE Extended Sync'. Commands will be logged locally until a client connects.
[INFO] [CSM.TmpeSync] [CSM.API Stub] Queued broadcast (no simulated clients): SpeedLimitApplied {LaneId=42, SegmentId=1089, SpeedLimit=100}
```

These lines confirm that TM:PE commands are created correctly even though no actual clients are attached. Use `CSM.API.Command.SimulateClientConnected(1)` to replay queued commands locally and `CSM.API.Command.DumpSimulatedCommandLog()` to inspect the captured payloads.

When CSM reports that the current role switched to `Server` but only the stub API is present, the mod now starts a simulated client automatically. The log shows:

```
[INFO] [CSM.TmpeSync] Stub CSM simulation started automatically (clientId=1).
```

This guarantees that TM:PE updates are replayed immediately without having to run the debug console command manually. If you want to inspect the data in-game, enable the CSM debug overlay in *Options → Mods → Cities: Skylines Multiplayer → Debug tools* and press **F7**. The overlay mirrors the new auto-started simulation client and displays the queued TM:PE commands.
