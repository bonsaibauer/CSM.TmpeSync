# Log Reference

CSM TM:PE Sync writes all diagnostics to a single rotating log file to keep the mod deterministic in multiplayer sessions.

## Location

```
%LOCALAPPDATA%\Colossal Order\Cities_Skylines\CSM.TmpeSync\csm.tmpe-sync.log
```

The file rolls over at 2 MB. Archived logs live next to the active file and use the pattern `csm.tmpe-sync-YYYYMMDD-HHMMSS[[-GUID]].log`.

## Levels

- `DEBUG` – Verbose bridge tracing and network chatter. These entries are always emitted to aid troubleshooting.
- `INFO` – Lifecycle events, dependency checks, and successful synchronisation operations.
- `WARN` – Missing dependencies, TM:PE features that could not be resolved, or recoverable network issues.
- `ERROR` – Exceptions thrown while applying or exporting state. These indicate the host rejected an operation.

Because logging is file-based only, there are no in-game windows or chat commands to toggle verbosity. Adjust the environment variable and restart the game if you need extra detail.
