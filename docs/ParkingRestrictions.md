# CSM TM:PE Sync - Parking Restrictions / Parkbeschränkungen

This document is bilingual. German (DE) first, English (EN) follows.

---

## DE - Deutsch

### 1) Kurzbeschreibung
- Synchronisiert TM:PE-Parkbeschränkungen (Parken erlaubt/verboten je Fahrtrichtung) zwischen Host und Clients in CSM-Sitzungen.
- Überträgt segmentweise den vollständigen Zustand (`AllowParkingForward`, `AllowParkingBackward`) – keine partialen Diffs.
- Apply-Koordinator mit Retry: prüft TM:PE-Optionen, ParkingRestrictionsManager und verhindert Echo über Ignore-Scope.

### 2) Dateistruktur
- `src/CSM.TmpeSync.ParkingRestrictions/`
  - `ParkingRestrictionSyncFeature.cs` – Feature-Bootstrap (aktiviert Listener).
  - `Handlers/` – CSM-Command-Handler (Server/Client, Retry-fähig).
  - `Messages/` – Netzwerkbefehle (ProtoBuf-Verträge) mit `ParkingRestrictionState`.
  - `Services/` – Harmony-Listener, Apply-Koordinator, TM:PE-Adapter.

### 3) Dateiübersicht
| Datei | Zweck |
| --- | --- |
| `ParkingRestrictionSyncFeature.cs` | Registriert/aktiviert das Feature und den TM:PE-Listener. |
| `Handlers/ParkingRestrictionAppliedCommandHandler.cs` | Client: verarbeitet Host-Broadcasts und nutzt den Apply-Koordinator (inkl. Retry). |
| `Handlers/ParkingRestrictionUpdateRequestHandler.cs` | Server: validiert Requests, ruft den Apply-Koordinator auf und broadcastet nach Erfolg. |
| `Messages/ParkingRestrictionAppliedCommand.cs` | Server → Alle: Segmentzustand (`SegmentId`, `ParkingRestrictionState`). |
| `Messages/ParkingRestrictionUpdateRequest.cs` | Client → Server: Änderungswunsch (`SegmentId`, `ParkingRestrictionState`). |
| `Services/ParkingRestrictionEventListener.cs` | Harmony-Hook (`SetParkingAllowed`), liest Segmentzustände und leitet sie weiter. |
| `Services/ParkingRestrictionSynchronization.cs` | Gemeinsamer Versandweg, Apply-Koordinator mit Retry/Backoff und State-Merge. |
| `Services/ParkingRestrictionTmpeAdapter.cs` | TM:PE-Adapter (Lesen/Schreiben, Ignore-Scope). |

### 4) Workflow (Server/Host und Client)
- **Host ändert Parken in TM:PE**
  - Harmony-Postfix auf `SetParkingAllowed` erkennt das Segment, liest den kompletten Zustand (`AllowParkingForward/Backward`) und broadcastet `ParkingRestrictionApplied`.
  - Clients wenden den Zustand via Apply-Koordinator an; bei fehlenden TM:PE-Optionen/Managern erfolgt Retry.

- **Client ändert Parken in TM:PE**
  - Harmony-Postfix sendet `ParkingRestrictionUpdateRequest` (SegmentId + gewünschter State) an den Server.
  - Server validiert Segmentexistenz, übergibt den State an den Apply-Koordinator.
  - Apply-Koordinator prüft `SavedGameOptions` und ParkingRestrictionsManager, setzt Werte unter Ignore-Scope. Bei `NullReferenceException` oder nicht verfügbarem Manager wird bis zu sechs Mal mit wachsendem Frame-Abstand (5 → 240) wiederholt; konkurrierende Requests werden gemergt (`bool?`-Flags).
  - Nach erfolgreicher Anwendung broadcastet der Server den gelesenen Segmentzustand (`ParkingRestrictionApplied`).

- **Abgelehnte Requests (Server)**
  - Fehlende Segmente oder endgültige TM:PE-Fehler führen zu `RequestRejected` (Grund + Entitätstyp/-ID).

### 5) Datenaustausch (Nachrichten/Felder)
| Nachricht | Richtung | Feld | Typ | Beschreibung |
| --- | --- | --- | --- | --- |
| `ParkingRestrictionUpdateRequest` | Client → Server | `SegmentId` | `ushort` | Segment-ID, dessen Parkzustand geändert werden soll. |
|  |  | `State` | `ParkingRestrictionState` | Gewünschter Zustand (siehe unten). |
| `ParkingRestrictionAppliedCommand` | Server → Alle | `SegmentId` | `ushort` | Segment-ID mit angewandtem Zustand. |
|  |  | `State` | `ParkingRestrictionState` | Effektiver Zustand nach TM:PE. |
| `RequestRejected` | Server → Client | `EntityType` | `byte` | 2 = Segment. |
|  |  | `EntityId` | `int` | Betroffene Segment-ID. |
|  |  | `Reason` | `string` | Grund, z. B. `entity_missing`, `tmpe_apply_failed`. |

`ParkingRestrictionState`
- `AllowParkingForward` (`bool?`): `true` = Parken in Fahrtrichtung erlaubt, `false` = verboten, `null` = unverändert (für Merge).
- `AllowParkingBackward` (`bool?`): analog für Gegenrichtung.

Hinweise
- Apply-Koordinator verwaltet Requests pro Segment, führt Retry-Backoff (5/15/30/60/120/240 Frames) aus und merge’t `bool?`-Felder aus mehreren Requests.
- `ParkingRestrictionTmpeAdapter` nutzt Ignore-Scope, damit eigene Applies keine neuen Harmony-Events erzeugen; Host-Broadcast hält Clients konsistent.

---

## EN - English

### 1) Summary
- Synchronises TM:PE parking restrictions (allow/forbid per driving direction) between host and clients in CSM sessions.
- Transfers the full segment state (`AllowParkingForward`, `AllowParkingBackward`) each time.
- Apply coordinator with retry: checks TM:PE options, ParkingRestrictionsManager, merges requests, and guards against echo via ignore scope.

### 2) Directory Layout
- `src/CSM.TmpeSync.ParkingRestrictions/`
  - `ParkingRestrictionSyncFeature.cs` – Feature bootstrap (enables listener).
  - `Handlers/` – CSM command handlers (server/client, retry-aware).
  - `Messages/` – Network commands (ProtoBuf contracts) containing `ParkingRestrictionState`.
  - `Services/` – Harmony listener, apply coordinator, TM:PE adapter.

### 3) File Overview
| File | Purpose |
| --- | --- |
| `ParkingRestrictionSyncFeature.cs` | Registers/enables the feature and the TM:PE listener. |
| `Handlers/ParkingRestrictionAppliedCommandHandler.cs` | Client: handles host broadcasts and invokes the retry-capable apply coordinator. |
| `Handlers/ParkingRestrictionUpdateRequestHandler.cs` | Server: validates requests, calls the apply coordinator, then broadcasts on success. |
| `Messages/ParkingRestrictionAppliedCommand.cs` | Server → All: segment state (`SegmentId`, `ParkingRestrictionState`). |
| `Messages/ParkingRestrictionUpdateRequest.cs` | Client → Server: desired state (`SegmentId`, `ParkingRestrictionState`). |
| `Services/ParkingRestrictionEventListener.cs` | Harmony hook on `SetParkingAllowed`, reads states, relays them. |
| `Services/ParkingRestrictionSynchronization.cs` | Common dispatch path, apply coordinator with retry/backoff, state merging. |
| `Services/ParkingRestrictionTmpeAdapter.cs` | TM:PE adapter (read/write, ignore scope). |

### 4) Workflow (Server/Host and Client)
- **Host edits parking in TM:PE**
  - Harmony postfix detects the segment, reads the full state (`AllowParkingForward/Backward`), and broadcasts `ParkingRestrictionApplied`.
  - Clients apply via the coordinator; retries occur until TM:PE options/manager are ready.

- **Client edits parking in TM:PE**
  - Harmony postfix sends `ParkingRestrictionUpdateRequest` (segmentId + desired state) to the server.
  - The server validates the segment and forwards the state to the apply coordinator.
  - Apply coordinator checks `SavedGameOptions` and ParkingRestrictionsManager, applies under ignore scope, and retries on `NullReferenceException` or missing manager (up to six attempts, frame delays 5 → 240). Concurrent requests merge via nullable booleans.
  - After success the server broadcasts the applied state using `ParkingRestrictionApplied`.

- **Rejections (Server)**
  - Missing segments or unrecoverable TM:PE failures result in `RequestRejected` (reason + entity info).

### 5) Data Exchange (messages/fields)
| Message | Direction | Field | Type | Description |
| --- | --- | --- | --- | --- |
| `ParkingRestrictionUpdateRequest` | Client → Server | `SegmentId` | `ushort` | Segment ID whose parking state changes. |
|  |  | `State` | `ParkingRestrictionState` | Desired state (see below). |
| `ParkingRestrictionAppliedCommand` | Server → All | `SegmentId` | `ushort` | Segment ID with the applied state. |
|  |  | `State` | `ParkingRestrictionState` | Effective state after TM:PE application. |
| `RequestRejected` | Server → Client | `EntityType` | `byte` | 2 = segment. |
|  |  | `EntityId` | `int` | Affected segment ID. |
|  |  | `Reason` | `string` | Reason, e.g. `entity_missing`, `tmpe_apply_failed`. |

`ParkingRestrictionState`
- `AllowParkingForward` (`bool?`): `true` = allowed, `false` = forbidden, `null` = unchanged (for merging).
- `AllowParkingBackward` (`bool?`): same for the opposite direction.

Notes
- Apply coordinator keeps per-segment queues, retries with exponential frame backoff (5/15/30/60/120/240), and merges nullable fields from multiple requests.
- `ParkingRestrictionTmpeAdapter` wraps TM:PE calls with ignore scope so that local applies do not trigger listener hooks again; host broadcasts realign clients if needed.
