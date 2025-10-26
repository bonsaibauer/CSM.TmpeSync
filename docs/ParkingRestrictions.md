# CSM TM:PE Sync - Parking Restrictions / Parkbeschränkungen

This document is bilingual. German (DE) first, English (EN) follows.

---

## DE - Deutsch

### 1) Kurzbeschreibung
- Synchronisiert TM:PE-Parkbeschränkungen (Parken erlaubt/verboten je Fahrtrichtung) zwischen Host und Clients in CSM-Sitzungen.
- Fängt lokale Änderungen in TM:PE per Harmony ab und überträgt sie via CSM.
- Minimaler, stabiler Ablauf: Bei einer Änderung wird der vollständige Segmentzustand (Forward/Backward) übertragen.

### 2) Dateistruktur
- `src/CSM.TmpeSync.ParkingRestrictions/`
  - `ParkingRestrictionSyncFeature.cs` - Feature-Bootstrap (aktiviert Listener).
  - `Handlers/` - CSM-Command-Handler (Server/Client Verarbeitungslogik).
  - `Messages/` - Netzwerkbefehle (ProtoBuf-Verträge).
  - `Services/` - Brücke zu TM:PE, Harmony-Listener, Synchronisations-Helfer.

### 3) Dateiübersicht
| Datei | Zweck |
| --- | --- |
| `ParkingRestrictionSyncFeature.cs` | Registriert/aktiviert das Feature und den TM:PE-Listener. |
| `Handlers/ParkingRestrictionAppliedCommandHandler.cs` | Client: verarbeitet Server-Broadcast "Applied" und setzt den Zustand lokal in TM:PE. |
| `Handlers/ParkingRestrictionUpdateRequestHandler.cs` | Server: validiert Request, setzt in TM:PE und broadcastet den resultierenden Segmentzustand. |
| `Messages/ParkingRestrictionAppliedCommand.cs` | Server → Alle: Endzustand je Segment (`SegmentId`, kompletter `ParkingRestrictionState`). |
| `Messages/ParkingRestrictionUpdateRequest.cs` | Client → Server: Änderungswunsch je Segment (`SegmentId`, gewünschter `ParkingRestrictionState`). |
| `Services/ParkingRestrictionEventListener.cs` | Harmony-Hook auf TM:PE `SetParkingAllowed`, leitet lokale Änderungen in CSM-Befehle um; broadcastet segmentweise. |
| `Services/ParkingRestrictionSynchronization.cs` | Gemeinsamer Versandweg (Server→Alle oder Client→Server) und Lese/Anwenden-Fassade. |
| `Services/ParkingRestrictionTmpeAdapter.cs` | Adapter zu TM:PE API/Implementierung: Lesen/Schreiben der Parkbeschränkungen, inkl. Ignore-Scope gegen Feedback-Loops. |

### 4) Workflow (Server/Host und Client)
- Host ändert Parken in TM:PE
  - Harmony-Postfix erkennt Änderung an `SetParkingAllowed(segmentId, direction, allowed)`.
  - Listener liest den kompletten Segmentzustand (`AllowParkingForward`, `AllowParkingBackward`) und sendet `ParkingRestrictionApplied` an alle.
  - Clients wenden den Zustand lokal an (Ignore-Scope verhindert Schleifen).

- Client ändert Parken in TM:PE
  - Harmony-Postfix sendet `ParkingRestrictionUpdateRequest` an den Server.
  - Server validiert (Segment vorhanden) und setzt den Zustand in TM:PE.
  - Server liest danach den resultierenden Segmentzustand und broadcastet `ParkingRestrictionApplied` an alle.
  - Alle wenden den Zustand lokal an (Client eingeschlossen; Ignore-Scope verhindert Re-Trigger).

- Abgelehnte Requests (Server)
  - Bei fehlenden Entitäten oder TM:PE-Fehlern sendet der Server `RequestRejected` zurück (Grund + Entität).

### 5) Datenaustausch (Nachrichten/Felder)
| Nachricht | Richtung | Feld | Typ | Beschreibung |
| --- | --- | --- | --- | --- |
| `ParkingRestrictionUpdateRequest` | Client → Server | `SegmentId` | `ushort` | Segment-ID, dessen Parkzustand geändert wurde. |
|  |  | `State` | `ParkingRestrictionState` | Gewünschter Zustand. Siehe Felder unten. |
| `ParkingRestrictionAppliedCommand` | Server → Alle | `SegmentId` | `ushort` | Segment-ID mit angewandtem Zustand. |
|  |  | `State` | `ParkingRestrictionState` | Effektiver Zustand nach TM:PE-Anwendung. |
| `RequestRejected` | Server → Client | `EntityType` | `byte` | Entitätstyp: 2=Segment. |
|  |  | `EntityId` | `int` | Betroffene Entität. |
|  |  | `Reason` | `string` | Grund, z. B. `entity_missing`, `tmpe_apply_failed`. |

Felder von `ParkingRestrictionState`:
- `AllowParkingForward` (`bool?`) – Parken in Fahrtrichtung erlaubt (true), verboten (false), unverändert/Standard (null).
- `AllowParkingBackward` (`bool?`) – Parken entgegen Fahrtrichtung erlaubt (true), verboten (false), unverändert/Standard (null).

Hinweise
- Interne TM:PE-Richtung: `NetInfo.Direction.Forward`/`Backward` – wird nur vom Listener genutzt; nicht im Netzprotokoll übertragen.
- Lokale Applies nutzen einen Ignore-Scope, damit Listener-Hooks nicht erneut feuern.

---

## EN - English

### 1) Summary
- Synchronizes TM:PE parking restrictions (allow/forbid per driving direction) between host and clients in CSM sessions.
- Hooks local TM:PE changes with Harmony and relays them via CSM.
- Minimal, robust flow: on any change, the full segment state (Forward/Backward) is sent.

### 2) Directory Layout
- `src/CSM.TmpeSync.ParkingRestrictions/`
  - `ParkingRestrictionSyncFeature.cs` - Feature bootstrap (enables listener).
  - `Handlers/` - CSM command handlers (server/client processing).
  - `Messages/` - Network commands (ProtoBuf contracts).
  - `Services/` - TM:PE bridge, Harmony listener, sync helpers.

### 3) File Overview
| File | Purpose |
| --- | --- |
| `ParkingRestrictionSyncFeature.cs` | Registers/enables the feature and the TM:PE listener. |
| `Handlers/ParkingRestrictionAppliedCommandHandler.cs` | Client: handles server "Applied" broadcast and applies locally in TM:PE. |
| `Handlers/ParkingRestrictionUpdateRequestHandler.cs` | Server: validates request, applies in TM:PE, and broadcasts resulting segment state. |
| `Messages/ParkingRestrictionAppliedCommand.cs` | Server → All: final state per segment (`SegmentId`, full `ParkingRestrictionState`). |
| `Messages/ParkingRestrictionUpdateRequest.cs` | Client → Server: change request per segment (`SegmentId`, desired `ParkingRestrictionState`). |
| `Services/ParkingRestrictionEventListener.cs` | Harmony hook on TM:PE `SetParkingAllowed`, translates local changes into CSM commands; broadcasts per segment. |
| `Services/ParkingRestrictionSynchronization.cs` | Common dispatch path (Server→All or Client→Server) and read/apply facade. |
| `Services/ParkingRestrictionTmpeAdapter.cs` | Adapter to TM:PE API/implementation: read/write parking restrictions; includes ignore-scope to avoid feedback loops. |

### 4) Workflow (Server/Host and Client)
- Host edits parking in TM:PE
  - Harmony postfix detects change at `SetParkingAllowed(segmentId, direction, allowed)`.
  - Listener reads the full segment state (`AllowParkingForward`, `AllowParkingBackward`) and sends `ParkingRestrictionApplied` to all.
  - Clients apply locally (ignore scope prevents loops).

- Client edits parking in TM:PE
  - Harmony postfix sends a `ParkingRestrictionUpdateRequest` to the server.
  - Server validates (segment exists) and applies in TM:PE.
  - Server then reads the resulting segment state and broadcasts `ParkingRestrictionApplied` to all.
  - Everyone (including the originating client) applies locally; ignore scope prevents re-triggering.

- Rejections (Server)
  - If entities are missing or TM:PE fails to apply, the server returns `RequestRejected` (with reason and entity info).

### 5) Data Exchange (messages/fields)
| Message | Direction | Field | Type | Description |
| --- | --- | --- | --- | --- |
| `ParkingRestrictionUpdateRequest` | Client → Server | `SegmentId` | `ushort` | Segment ID for which the parking state changed. |
|  |  | `State` | `ParkingRestrictionState` | Desired state. See fields below. |
| `ParkingRestrictionAppliedCommand` | Server → All | `SegmentId` | `ushort` | Segment ID with the applied state. |
|  |  | `State` | `ParkingRestrictionState` | Effective state after TM:PE application. |
| `RequestRejected` | Server → Client | `EntityType` | `byte` | Entity type: 2=Segment. |
|  |  | `EntityId` | `int` | Affected entity. |
|  |  | `Reason` | `string` | Reason, e.g., `entity_missing`, `tmpe_apply_failed`. |

Fields of `ParkingRestrictionState`:
- `AllowParkingForward` (`bool?`) – parking allowed in forward direction (true), forbidden (false), unchanged/default (null).
- `AllowParkingBackward` (`bool?`) – parking allowed in backward direction (true), forbidden (false), unchanged/default (null).

Notes
- Internal TM:PE direction: `NetInfo.Direction.Forward`/`Backward` – used by the listener only; not part of the wire protocol.
- Local applies use an ignore scope so listener hooks do not fire again.

