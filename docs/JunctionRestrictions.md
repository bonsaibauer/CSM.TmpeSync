# CSM TM:PE Sync - Junction Restrictions / Kreuzungsbeschränkungen

This document is bilingual. German (DE) first, English (EN) follows.

---

## DE - Deutsch

### 1) Kurzbeschreibung
- Synchronisiert TM:PE-Kreuzungsbeschränkungen (U-Turn, Geradeaus-Spurwechsel, Blockierte Kreuzung befahren, Fußgängerüberweg, nahes/fernes Rechtsabbiegen bei Rot) zwischen Host und Clients in CSM-Sitzungen.
- Segmentend-basiertes Schema wie Priority Signs: nach jeder Änderung wird der vollständige Knoten-Snapshot (bis zu 8 Segmentenden) übertragen.
- Apply-Koordinator pro Segmentende sorgt für resiliente Anwendung: prüft TM:PE-Optionen, JunctionRestrictions-Manager und LaneConnection-Abhängigkeiten, führt Backoff-Retry durch und fasst Requests zusammen.

### 2) Dateistruktur
- `src/CSM.TmpeSync.JunctionRestrictions/`
  - `JunctionRestrictionsFeature.cs` – Feature-Bootstrap (aktiviert Listener).
  - `Handlers/` – CSM-Command-Handler (Server/Client, Retry-fähig).
  - `Messages/` – Netzwerkbefehle (ProtoBuf-Verträge) mit `JunctionRestrictionsState`.
  - `Services/` – Harmony-Listener, Apply-Koordinator, TM:PE-Adapter und Hilfen.

### 3) Dateiübersicht
| Datei | Zweck |
| --- | --- |
| `JunctionRestrictionsFeature.cs` | Registriert/aktiviert das Feature und den TM:PE-Listener. |
| `Handlers/JunctionRestrictionsAppliedCommandHandler.cs` | Client: verarbeitet Host-Broadcasts, delegiert an den Apply-Koordinator und loggt Retry-Ergebnisse. |
| `Handlers/JunctionRestrictionsUpdateRequestHandler.cs` | Server: validiert Requests, nutzt den Apply-Koordinator und broadcastet nach Erfolg den kompletten Knoten-Snapshot. |
| `Messages/JunctionRestrictionsAppliedCommand.cs` | Server → Alle: Zustand eines Segmentendes (NodeId, SegmentId, `JunctionRestrictionsState`). |
| `Messages/JunctionRestrictionsUpdateRequest.cs` | Client → Server: Änderungswunsch für ein Segmentende (NodeId, SegmentId, `JunctionRestrictionsState`). |
| `Services/JunctionRestrictionsEventListener.cs` | Harmony-Hooks auf TM:PE, erkennt lokale Änderungen und broadcastet knotenweise. |
| `Services/JunctionRestrictionsSynchronization.cs` | Gemeinsamer Versandweg, Apply-Koordinator mit Retry/Backoff und Flag-Merge pro Segmentende. |

### 4) Workflow (Server/Host und Client)
- **Host ändert Beschränkungen in TM:PE**
  - Harmony-Postfix erkennt den betroffenen Knoten, liest für jedes Segmentende den vollständigen `JunctionRestrictionsState` und broadcastet `JunctionRestrictionsApplied` an alle.
  - Clients wenden den Zustand lokal via Apply-Koordinator an; falls TM:PE noch nicht bereit ist, erfolgen Wiederholungen (Backoff).

- **Client ändert Beschränkungen in TM:PE**
  - Harmony-Postfix sendet `JunctionRestrictionsUpdateRequest` für das betroffene Segmentende an den Server.
  - Server validiert Node/Segment und übergibt den State an den Apply-Koordinator.
  - Apply-Koordinator prüft `SavedGameOptions`, JunctionRestrictionsManager und (per Reflection) LaneConnectionManager/Connection-Datenbank. Setter werden mit Ignore-Scope ausgeführt; `NullReferenceException` oder fehlende Abhängigkeiten führen zu Retry (max. 6 Versuche, Frame-Delays 5 → 240). Flags aus konkurrierenden Requests werden zusammengeführt.
  - Nach Erfolg broadcastet der Server für alle Segmentenden des Knotens `JunctionRestrictionsApplied`.

- **Abgelehnte Requests (Server)**
  - Fehlende Entitäten oder harte TM:PE-Fehler führen zu `RequestRejected` (Grund + Entität).

### 5) Datenaustausch (Nachrichten/Felder)
| Nachricht | Richtung | Feld | Typ | Beschreibung |
| --- | --- | --- | --- | --- |
| `JunctionRestrictionsUpdateRequest` | Client → Server | `NodeId` | `ushort` | Knoten-ID der Kreuzung. |
|  |  | `SegmentId` | `ushort` | Segment-ID am Knoten (betroffenes Segmentende). |
|  |  | `State.AllowUTurns` | `bool?` | U-Turns erlaubt/verboten (`null` = keine Änderung). |
|  |  | `State.AllowLaneChangesWhenGoingStraight` | `bool?` | Geradeaus-Spurwechsel erlaubt/verboten (`null` = keine Änderung). |
|  |  | `State.AllowEnterWhenBlocked` | `bool?` | Blockierte Kreuzung befahren erlaubt/verboten. |
|  |  | `State.AllowPedestrianCrossing` | `bool?` | Fußgängerüberweg erlaubt/verboten. |
|  |  | `State.AllowNearTurnOnRed` | `bool?` | Nahes Rechtsabbiegen bei Rot erlaubt/verboten. |
|  |  | `State.AllowFarTurnOnRed` | `bool?` | Fernes Rechtsabbiegen bei Rot erlaubt/verboten. |
| `JunctionRestrictionsAppliedCommand` | Server → Alle | `NodeId` | `ushort` | Knoten-ID. |
|  |  | `SegmentId` | `ushort` | Segment-ID am Knoten (Ziel-Segmentende). |
|  |  | `State.*` | `bool` | Effektiv angewandte Werte nach TM:PE (vollständiger Snapshot). |
| `RequestRejected` | Server → Client | `EntityType` | `byte` | 3 = Node, 2 = Segment. |
|  |  | `EntityId` | `int` | Betroffene Entität. |
|  |  | `Reason` | `string` | Grund, z. B. `entity_missing`, `tmpe_apply_failed`. |

Hinweise
- `JunctionRestrictionsState` verwendet `bool?`, damit der Apply-Koordinator Flags aus mehreren Requests mergen kann (`null` = unverändert).
- Apply-Koordinator verwaltet Requests pro Segmentende, nutzt Backoff-Retry (5/15/30/60/120/240 Frames) und prüft LaneConnection-Abhängigkeiten, da TM:PE Setter darauf zugreifen.
- Ignore-Scopes verhindern, dass TM:PE-Setter eigene Harmony-Hooks erneut auslösen; Host-Broadcast korrigiert Abweichungen nach Netzänderungen.

---

## EN - English

### 1) Summary
- Synchronises TM:PE junction restrictions (U-turn, straight lane change, enter blocked junction, pedestrian crossing, near/far turn on red) between host and clients in CSM sessions.
- Segment-end-based snapshot like Priority Signs: after any change the full node (up to eight segment ends) is broadcast.
- Apply coordinator per segment end ensures robustness: checks TM:PE options, JunctionRestrictionsManager and lane-connection dependencies, performs retry/backoff, and merges concurrent requests.

### 2) Directory Layout
- `src/CSM.TmpeSync.JunctionRestrictions/`
  - `JunctionRestrictionsFeature.cs` – Feature bootstrap (enables listener).
  - `Handlers/` – CSM command handlers (server/client, retry-aware).
  - `Messages/` – Network commands (ProtoBuf contracts) containing `JunctionRestrictionsState`.
  - `Services/` – Harmony listener, apply coordinator, TM:PE adapter/helpers.

### 3) File Overview
| File | Purpose |
| --- | --- |
| `JunctionRestrictionsFeature.cs` | Registers/enables the feature and the TM:PE listener. |
| `Handlers/JunctionRestrictionsAppliedCommandHandler.cs` | Client: processes host broadcasts and feeds them into the retry-capable apply coordinator. |
| `Handlers/JunctionRestrictionsUpdateRequestHandler.cs` | Server: validates requests, invokes the apply coordinator, then broadcasts the entire node snapshot. |
| `Messages/JunctionRestrictionsAppliedCommand.cs` | Server → All: state per segment end (NodeId, SegmentId, `JunctionRestrictionsState`). |
| `Messages/JunctionRestrictionsUpdateRequest.cs` | Client → Server: desired state per segment end (NodeId, SegmentId, `JunctionRestrictionsState`). |
| `Services/JunctionRestrictionsEventListener.cs` | Harmony hooks capturing local changes and broadcasting node snapshots. |
| `Services/JunctionRestrictionsSynchronization.cs` | Common dispatch path, apply coordinator with retry/backoff, flag merging per segment end. |

### 4) Workflow (Server/Host and Client)
- **Host edits restrictions in TM:PE**
  - Harmony postfix identifies the node, reads each segment end into `JunctionRestrictionsState`, and broadcasts `JunctionRestrictionsApplied` to all clients.
  - Clients apply via the coordinator; retries occur until TM:PE accepts the changes.

- **Client edits restrictions in TM:PE**
  - Harmony postfix sends `JunctionRestrictionsUpdateRequest` for the targeted segment end to the server.
  - The server validates node/segment and forwards the state to the apply coordinator.
  - Apply coordinator checks `SavedGameOptions`, JunctionRestrictionsManager, and lane-connection dependencies (via reflection). Setters run under ignore scope; `NullReferenceException` or missing prerequisites trigger retries (up to six attempts with frame delays 5 → 240). Flags from concurrent requests are merged.
  - After a successful apply the server broadcasts `JunctionRestrictionsApplied` for each segment end of the node.

- **Rejections (Server)**
  - Missing entities or unrecoverable TM:PE failures result in `RequestRejected` (reason + entity info).

### 5) Data Exchange (messages/fields)
| Message | Direction | Field | Type | Description |
| --- | --- | --- | --- | --- |
| `JunctionRestrictionsUpdateRequest` | Client → Server | `NodeId` | `ushort` | Node ID of the junction. |
|  |  | `SegmentId` | `ushort` | Segment ID at the node (segment end). |
|  |  | `State.AllowUTurns` | `bool?` | Allow/forbid U-turns (`null` = unchanged). |
|  |  | `State.AllowLaneChangesWhenGoingStraight` | `bool?` | Allow/forbid straight lane changes. |
|  |  | `State.AllowEnterWhenBlocked` | `bool?` | Allow/forbid entering a blocked junction. |
|  |  | `State.AllowPedestrianCrossing` | `bool?` | Allow/forbid pedestrian crossings. |
|  |  | `State.AllowNearTurnOnRed` | `bool?` | Allow/forbid near-side turn on red. |
|  |  | `State.AllowFarTurnOnRed` | `bool?` | Allow/forbid far-side turn on red. |
| `JunctionRestrictionsAppliedCommand` | Server → All | `NodeId` | `ushort` | Node ID of the junction. |
|  |  | `SegmentId` | `ushort` | Segment ID at the node (target segment end). |
|  |  | `State.*` | `bool` | Effective applied values (full snapshot). |
| `RequestRejected` | Server → Client | `EntityType` | `byte` | 3 = node, 2 = segment. |
|  |  | `EntityId` | `int` | Affected entity. |
|  |  | `Reason` | `string` | Reason, e.g. `entity_missing`, `tmpe_apply_failed`. |

Notes
- `JunctionRestrictionsState` uses nullable booleans to allow incremental merges when multiple requests target the same segment end.
- The apply coordinator keeps per-end queues, retries with exponential frame backoff (5/15/30/60/120/240), and validates lane-connection dependencies because TM:PE setters rely on them.
- Ignore scopes prevent local setters from re-triggering Harmony hooks; the host broadcast re-aligns clients if the network layout changes mid-apply.
