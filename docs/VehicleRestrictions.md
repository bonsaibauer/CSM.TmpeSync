# CSM TM:PE Sync - Vehicle Restrictions / Fahrzeugbeschränkungen

This document is bilingual. German (DE) first, English (EN) follows.

---

## DE - Deutsch

### 1) Kurzbeschreibung
- Synchronisiert TM:PE-Fahrzeugbeschränkungen pro Segmentspur zwischen Host und Clients in CSM-Sitzungen.
- Fängt lokale Änderungen in TM:PE per Harmony ab und überträgt sie via CSM.
- Überträgt keine `laneId`/`laneIndex`: stattdessen segmentbezogene Einträge mit Spur-Ordinal und Lane-Signatur; die Ziel-`laneId` wird lokal auf Empfängerseite aufgelöst.

### 2) Dateistruktur
- `src/CSM.TmpeSync.VehicleRestrictions/`
  - `VehicleRestrictionSyncFeature.cs` – Feature-Bootstrap (aktiviert Listener).
  - `Handlers/` – CSM-Command-Handler (Server/Client-Verarbeitungslogik).
  - `Messages/` – Netzwerkbefehle (ProtoBuf-Verträge).
  - `Services/` – Adapter zu TM:PE, Harmony-Listener, Synchronisations-Helfer.

### 3) Dateiübersicht
| Datei | Zweck |
| --- | --- |
| `VehicleRestrictionSyncFeature.cs` | Registriert/aktiviert das Feature und den TM:PE-Listener. |
| `Handlers/VehicleRestrictionsAppliedCommandHandler.cs` | Client: verarbeitet Server-Broadcast „Applied“ und setzt die Beschränkungen lokal (per Ordinal + Signatur). |
| `Handlers/VehicleRestrictionsUpdateRequestHandler.cs` | Server: validiert Request, setzt in TM:PE und broadcastet den finalen Segmentzustand. |
| `Messages/VehicleRestrictionsAppliedCommand.cs` | Server → Alle: endgültiger Zustand pro Segment mit Liste von Spur-Einträgen. |
| `Messages/VehicleRestrictionsUpdateRequest.cs` | Client → Server: Änderungswunsch pro Segment mit Liste von Spur-Einträgen. |
| `Services/VehicleRestrictionEventListener.cs` | Harmony-Hooks auf TM:PE, leitet lokale Änderungen in CSM-Befehle um; broadcastet segmentweit. |
| `Services/VehicleRestrictionSynchronization.cs` | Gemeinsamer Versandweg (Server→Alle oder Client→Server) und Lese/Anwenden-Fassade. |
| `Services/VehicleRestrictionTmpeAdapter.cs` | Adapter zur TM:PE-API: Lesen/Schreiben der Fahrzeugbeschränkungen, Ordinal-/Signaturprüfung und lokale `laneId`-Auflösung.

### 4) Workflow (Server/Host und Client)
- Host ändert Beschränkung in TM:PE
  - Harmony-Postfix erkennt Änderung (SetAllowedVehicleTypes/ToggleAllowedType) und bestimmt das betroffene `segmentId`.
  - Listener liest alle relevanten Fahrzeugspuren im Segment und sendet einen `VehicleRestrictionsApplied` mit allen Einträgen an alle.
  - Clients wenden Werte lokal an (Ignore-Scope verhindert Schleifen).

- Client ändert Beschränkung in TM:PE
  - Harmony-Postfix sendet `VehicleRestrictionsUpdateRequest` an den Server.
  - Server validiert (`segmentId` vorhanden) und setzt die Beschränkungen spurweise in TM:PE (Ordinals + Signatur prüfen, `laneId` lokal auflösen).
  - Server liest danach den Segmentzustand und broadcastet `VehicleRestrictionsApplied` an alle.
  - Alle wenden Werte lokal an (Client eingeschlossen; Ignore-Scope verhindert Re-Trigger).

- Abgelehnte Requests (Server)
  - Bei fehlenden Entitäten oder TM:PE-Fehlern sendet der Server `RequestRejected` zurück (Grund + Entität).

### 5) Datenaustausch (Nachrichten/Felder)
| Nachricht | Richtung | Feld | Typ | Beschreibung |
| --- | --- | --- | --- | --- |
| `VehicleRestrictionsUpdateRequest` (Proto: `SetVehicleRestrictionsRequest`) | Client → Server | `SegmentId` | `ushort` | Segment-ID des betroffenen Segments. |
|  |  | `Items` | `List<Entry>` | Liste von Einträgen für betroffene Spuren. |
|  |  | `Items[i].LaneOrdinal` | `int` | Ordinal der Spur innerhalb `segment.Info.m_lanes` (nur `LaneType.Vehicle` bzw. `LaneType.TransportVehicle`). |
|  |  | `Items[i].Restrictions` | `VehicleRestrictionFlags` | Bitmaske der erlaubten Fahrzeugtypen. |
|  |  | `Items[i].Signature.LaneTypeRaw` | `int` | Rohwert von `NetInfo.Lane.m_laneType`. |
|  |  | `Items[i].Signature.VehicleTypeRaw` | `int` | Rohwert von `NetInfo.Lane.m_vehicleType`. |
|  |  | `Items[i].Signature.DirectionRaw` | `int` | Rohwert von `NetInfo.Lane.m_direction`. |
| `VehicleRestrictionsAppliedCommand` | Server → Alle | `SegmentId` | `ushort` | Segment-ID des Segments. |
|  |  | `Items` | `List<Entry>` | Final angewandter Zustand, gleiche Struktur wie im Request. |
| `RequestRejected` | Server → Client | `EntityType` | `byte` | Entitätstyp: 2=Segment (in diesem Workflow segmentbasiert). |
|  |  | `EntityId` | `int` | Betroffene Entität. |
|  |  | `Reason` | `string` | Grund, z. B. `entity_missing`, `tmpe_apply_failed`. |

Hinweise
- Berücksichtigt werden Spuren mit `LaneType.Vehicle` oder `LaneType.TransportVehicle`; Fußwege/Parkspuren werden übersprungen.
- Die Lane-Signatur verhindert Fehlauswahl bei Layout-Varianten; bei Abweichung wird der Eintrag übersprungen.
- Lokale Applies nutzen einen Ignore-Scope, damit die Harmony-Hooks nicht erneut feuern.

---

## EN - English

### 1) Summary
- Synchronizes TM:PE vehicle restrictions per segment lane between host and clients in CSM sessions.
- Hooks local TM:PE changes with Harmony and relays them via CSM.
- No `laneId`/`laneIndex` on the wire: segment-scoped entries carry lane ordinal plus lane signature; receivers resolve the runtime `laneId` locally.

### 2) Directory Layout
- `src/CSM.TmpeSync.VehicleRestrictions/`
  - `VehicleRestrictionSyncFeature.cs` – Feature bootstrap (enables listener).
  - `Handlers/` – CSM command handlers (server/client processing).
  - `Messages/` – Network commands (ProtoBuf contracts).
  - `Services/` – TM:PE adapter, Harmony listener, sync helpers.

### 3) File Overview
| File | Purpose |
| --- | --- |
| `VehicleRestrictionSyncFeature.cs` | Registers/enables the feature and TM:PE listener. |
| `Handlers/VehicleRestrictionsAppliedCommandHandler.cs` | Client: handles server “Applied” broadcast and applies locally (using ordinal + signature). |
| `Handlers/VehicleRestrictionsUpdateRequestHandler.cs` | Server: validates request, applies in TM:PE, then broadcasts final segment state. |
| `Messages/VehicleRestrictionsAppliedCommand.cs` | Server → All: final per-segment state with a list of lane entries. |
| `Messages/VehicleRestrictionsUpdateRequest.cs` | Client → Server: change request per segment with a list of lane entries. |
| `Services/VehicleRestrictionEventListener.cs` | Harmony hooks into TM:PE, translates local changes into CSM commands; broadcasts per segment. |
| `Services/VehicleRestrictionSynchronization.cs` | Common dispatch path (Server→All or Client→Server) and read/apply facade. |
| `Services/VehicleRestrictionTmpeAdapter.cs` | Adapter to TM:PE API: read/write restrictions, ordinal/signature validation, local `laneId` resolution.

### 4) Workflow (Server/Host and Client)
- Host changes restrictions in TM:PE
  - Harmony postfix detects the change (SetAllowedVehicleTypes/ToggleAllowedType) and resolves the affected `segmentId`.
  - Listener reads all relevant vehicle lanes for the segment and sends a `VehicleRestrictionsApplied` containing all entries to everyone.
  - Clients apply locally (ignore scope prevents feedback loops).

- Client changes restrictions in TM:PE
  - Harmony postfix sends a `VehicleRestrictionsUpdateRequest` to the server.
  - Server validates (`segmentId` exists) and applies lane-by-lane in TM:PE (check ordinal + signature, resolve `laneId` locally).
  - Server then reads back the segment state and broadcasts `VehicleRestrictionsApplied` to everyone.
  - Everyone (including the originating client) applies locally; ignore scope prevents re-triggering.

- Rejections (Server)
  - If entities are missing or TM:PE fails to apply, the server returns `RequestRejected` (with reason and entity info).

### 5) Data Exchange (messages/fields)
| Message | Direction | Field | Type | Description |
| --- | --- | --- | --- | --- |
| `VehicleRestrictionsUpdateRequest` (Proto: `SetVehicleRestrictionsRequest`) | Client → Server | `SegmentId` | `ushort` | Segment ID of the affected segment. |
|  |  | `Items` | `List<Entry>` | List of entries for affected lanes. |
|  |  | `Items[i].LaneOrdinal` | `int` | Lane ordinal within `segment.Info.m_lanes` (only `LaneType.Vehicle`/`LaneType.TransportVehicle`). |
|  |  | `Items[i].Restrictions` | `VehicleRestrictionFlags` | Bitmask of allowed vehicle types. |
|  |  | `Items[i].Signature.LaneTypeRaw` | `int` | Raw `NetInfo.Lane.m_laneType` value. |
|  |  | `Items[i].Signature.VehicleTypeRaw` | `int` | Raw `NetInfo.Lane.m_vehicleType` value. |
|  |  | `Items[i].Signature.DirectionRaw` | `int` | Raw `NetInfo.Lane.m_direction` value. |
| `VehicleRestrictionsAppliedCommand` | Server → All | `SegmentId` | `ushort` | Segment ID. |
|  |  | `Items` | `List<Entry>` | Final applied state, same structure as request. |
| `RequestRejected` | Server → Client | `EntityType` | `byte` | Entity type: 2=Segment (segment-scoped in this workflow). |
|  |  | `EntityId` | `int` | Affected entity. |
|  |  | `Reason` | `string` | Reason, e.g., `entity_missing`, `tmpe_apply_failed`. |

Notes
- Only lanes with `LaneType.Vehicle` or `LaneType.TransportVehicle` are considered; footpaths/parking shoulders are skipped.
- The lane signature prevents mismatches with prefab variants; entries are skipped on signature mismatch.
- Local applies use an ignore scope so listener hooks do not fire again.

