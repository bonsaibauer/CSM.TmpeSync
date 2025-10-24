# Log Reference

CSM TM:PE Sync writes all diagnostics to a single rotating log file to keep the mod deterministic in multiplayer sessions.

## Location

```
%LOCALAPPDATA%\Colossal Order\Cities_Skylines\CSM.TmpeSync\log-YYYY-MM-DD.log
```

A fresh log file is created for every calendar day at UTC+02:00. The active file rolls over at 2 MB, storing
archives alongside the daily files with the pattern `log-YYYY-MM-DD-archive-YYYYMMDD-HHMMSS[[-GUID]].log`.

Timestamps inside the files are also written at UTC+02:00 to match the file naming scheme.

## Levels

- `DEBUG` – Verbose bridge tracing and network chatter. These entries appear only in Debug builds to keep Release builds lean.
- `INFO` – Lifecycle events, dependency checks, and successful synchronisation operations.
- `WARN` – Missing dependencies, TM:PE features that could not be resolved, or recoverable network issues.
- `ERROR` – Exceptions thrown while applying or exporting state. These indicate the host rejected an operation.

Because logging is file-based only, there are no in-game windows or chat commands to toggle verbosity. Adjust the environment variable and restart the game if you need extra detail.
