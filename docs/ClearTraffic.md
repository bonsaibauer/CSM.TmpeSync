# CSM TM:PE Sync - Clear Traffic

This document is bilingual. German (DE) first, English (EN) follows.

---

## DE - Deutsch

### 1) Kurzbeschreibung
- Synchronisiert den „Clear Traffic“-Button von TM:PE zwischen Host und Clients in CSM-Sitzungen.
- Lokale Auslösung wird per Harmony abgefangen und via CSM weitergeleitet.
- Minimaler Datenaustausch: Es gibt keine zielbezogenen IDs, nur die Aktion „Clear“.

### 2) Dateistruktur
- `src/CSM.TmpeSync.ClearTraffic/`
  - `ClearTrafficSyncFeature.cs` - Feature-Bootstrap (aktiviert Listener).
  - `Handlers/` - CSM-Command-Handler (Server-/Client-Verarbeitung).
  - `Messages/` - Netzwerkbefehle (ProtoBuf-Verträge).
  - `Services/` - Harmony-Listener, Synchronisations-Helfer und TM:PE-Adapter.

### 3) Dateiübersicht
| Datei | Zweck |
| --- | --- |
| `ClearTrafficSyncFeature.cs` | Registriert/aktiviert das Feature und den TM:PE-Listener. |
| `Handlers/ClearTrafficAppliedCommandHandler.cs` | Client/Host: verarbeitet Server-Broadcast „Applied“ und führt Clear Traffic lokal aus. |
| `Handlers/ClearTrafficUpdateRequestHandler.cs` | Server: verarbeitet Client-Request, führt Clear Traffic aus und broadcastet „Applied“. |
| `Messages/ClearTrafficAppliedCommand.cs` | Server → Alle: Bestätigung, dass Clear Traffic angewandt wurde (ohne Felder). |
| `Messages/ClearTrafficUpdateRequest.cs` | Client → Server: Request, Clear Traffic auszuführen (ohne Felder). |
| `Services/ClearTrafficEventListener.cs` | Harmony-Hook auf TM:PE (`UtilityManager.ClearTraffic`), erzeugt CSM-Befehle je nach Rolle. |
| `Services/ClearTrafficSynchronization.cs` | Gemeinsamer Versandweg (Server→Alle oder Client→Server) und Apply-Fassade. |
| `Services/ClearTrafficTmpeAdapter.cs` | Adapter zu TM:PE: ruft `UtilityManager.ClearTraffic()` per Reflection auf; mit lokalem Ignore-Scope gegen Echo.

### 4) Workflow (Server/Host und Client)
- Host löst Clear Traffic in TM:PE aus
  - Harmony-Postfix erkennt die Aktion.
  - Listener dispatcht `ClearTrafficAppliedCommand` an alle.
  - Alle Instanzen führen Clear Traffic lokal aus (Ignore-Scope verhindert Rückkopplung).

- Client löst Clear Traffic in TM:PE aus
  - Harmony-Postfix sendet `ClearTrafficUpdateRequest` an den Server.
  - Server führt Clear Traffic in TM:PE aus und broadcastet `ClearTrafficAppliedCommand` an alle.
  - Alle (inkl. auslösendem Client) führen Clear Traffic lokal aus; Ignore-Scope verhindert Re-Trigger.

- Abgelehnte Requests (Server)
  - Schlägt die TM:PE-Ausführung fehl, sendet der Server `RequestRejected` zurück (Reason z. B. `tmpe_clear_failed`).

### 5) Datenaustausch (Nachrichten/Felder)
| Nachricht | Richtung | Feld | Typ | Beschreibung |
| --- | --- | --- | --- | --- |
| `ClearTrafficUpdateRequest` | Client → Server | — | — | Keine Nutzlast; der Button-Trigger selbst ist das Signal. |
| `ClearTrafficAppliedCommand` | Server → Alle | — | — | Bestätigung eines ausgeführten Clear Traffic. |
| `RequestRejected` | Server → Client | `Reason` | `string` | Grund, z. B. `tmpe_clear_failed`. |
|  |  | `EntityType` | `byte` | Optional/kontextabhängig; bei Clear Traffic i. d. R. nicht gesetzt. |
|  |  | `EntityId` | `int` | Optional/kontextabhängig; bei Clear Traffic i. d. R. nicht gesetzt. |

Hinweise
- Der Adapter nutzt Reflection auf `UtilityManager.ClearTraffic()` und einen thread-lokalen Ignore-Scope, um Listener-Echos zu verhindern.
- Keine Node-/Segment-IDs beteiligt; daher minimaler Overhead und einfache Replikation.

---

## EN - English

### 1) Summary
- Synchronizes TM:PE’s “Clear Traffic” button between host and clients in CSM sessions.
- Local triggers are hooked via Harmony and relayed through CSM.
- Minimal payload: there are no target IDs, only the clear action itself.

### 2) Directory Layout
- `src/CSM.TmpeSync.ClearTraffic/`
  - `ClearTrafficSyncFeature.cs` - Feature bootstrap (enables listener).
  - `Handlers/` - CSM command handlers (server/client processing).
  - `Messages/` - Network commands (ProtoBuf contracts).
  - `Services/` - Harmony listener, sync helpers, and TM:PE adapter.

### 3) File Overview
| File | Purpose |
| --- | --- |
| `ClearTrafficSyncFeature.cs` | Registers/enables the feature and the TM:PE listener. |
| `Handlers/ClearTrafficAppliedCommandHandler.cs` | Client/Host: handles server “Applied” broadcast and executes Clear Traffic locally. |
| `Handlers/ClearTrafficUpdateRequestHandler.cs` | Server: processes client request, executes Clear Traffic, then broadcasts “Applied”. |
| `Messages/ClearTrafficAppliedCommand.cs` | Server → All: confirmation that Clear Traffic was executed (no fields). |
| `Messages/ClearTrafficUpdateRequest.cs` | Client → Server: request to execute Clear Traffic (no fields). |
| `Services/ClearTrafficEventListener.cs` | Harmony hook into TM:PE (`UtilityManager.ClearTraffic`); emits CSM commands based on current role. |
| `Services/ClearTrafficSynchronization.cs` | Common dispatch path (Server→All or Client→Server) and apply facade. |
| `Services/ClearTrafficTmpeAdapter.cs` | Adapter to TM:PE: invokes `UtilityManager.ClearTraffic()` via reflection; local ignore scope prevents echo.

### 4) Workflow (Server/Host and Client)
- Host presses Clear Traffic in TM:PE
  - Harmony postfix detects it.
  - Listener dispatches `ClearTrafficAppliedCommand` to all.
  - Everyone applies locally (ignore scope prevents feedback loops).

- Client presses Clear Traffic in TM:PE
  - Harmony postfix sends `ClearTrafficUpdateRequest` to the server.
  - Server executes Clear Traffic in TM:PE and broadcasts `ClearTrafficAppliedCommand` to all.
  - Everyone (including the originating client) applies locally; ignore scope prevents re-triggering.

- Rejections (Server)
  - If the TM:PE execution fails, the server returns `RequestRejected` (e.g., reason `tmpe_clear_failed`).

### 5) Data Exchange (messages/fields)
| Message | Direction | Field | Type | Description |
| --- | --- | --- | --- | --- |
| `ClearTrafficUpdateRequest` | Client → Server | — | — | No payload; the button trigger itself is the signal. |
| `ClearTrafficAppliedCommand` | Server → All | — | — | Confirmation of a performed Clear Traffic. |
| `RequestRejected` | Server → Client | `Reason` | `string` | Reason, e.g., `tmpe_clear_failed`. |
|  |  | `EntityType` | `byte` | Optional/contextual; typically not set for Clear Traffic. |
|  |  | `EntityId` | `int` | Optional/contextual; typically not set for Clear Traffic. |

Notes
- The adapter reflects `UtilityManager.ClearTraffic()` and uses a thread-local ignore scope to avoid echoing listener hooks.
- No node/segment IDs involved; minimal overhead and simple replication.

