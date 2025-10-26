# CSM TM:PE Sync - Junction Restrictions / Kreuzungsbeschränkungen

This document is bilingual. German (DE) first, English (EN) follows.

---

## DE - Deutsch

### 1) Kurzbeschreibung
- Synchronisiert TM:PE-Kreuzungsbeschränkungen (U-Turn, Geradeaus-Spurwechsel, In blockierte Kreuzung einfahren, Fußgängerüberweg, Nahes/Fernes Rechtsabbiegen bei Rot) zwischen Host und Clients in CSM-Sitzungen.
- Fängt lokale Änderungen in TM:PE per Harmony ab und überträgt sie via CSM.
- Wie bei Priority Signs: nach einer Änderung wird der gesamte Zustand des Knotens für jedes Segmentende übertragen, damit TM:PE-Automatiken korrekt gespiegelt werden.

### 2) Dateistruktur
- `src/CSM.TmpeSync.JunctionRestrictions/`
  - `JunctionRestrictionsFeature.cs` - Feature-Bootstrap (aktiviert Listener).
  - `Handlers/` - CSM-Command-Handler (Server/Client Verarbeitungslogik).
  - `Messages/` - Netzwerkbefehle (ProtoBuf-Verträge).
  - `Services/` - TM:PE-Anbindung (Adapter), Harmony-Listener, Synchronisations-Helfer.

### 3) Dateiübersicht
| Datei | Zweck |
| --- | --- |
| `JunctionRestrictionsFeature.cs` | Registriert/aktiviert das Feature und den TM:PE-Listener. |
| `Handlers/JunctionRestrictionsAppliedCommandHandler.cs` | Client: verarbeitet Server-Broadcast „Applied“ und setzt Werte lokal in TM:PE. |
| `Handlers/JunctionRestrictionsUpdateRequestHandler.cs` | Server: validiert Request, setzt die Restriktion am Segmentende in TM:PE und broadcastet den gesamten Knoten-Zustand. |
| `Messages/JunctionRestrictionsAppliedCommand.cs` | Server → Alle: finaler Zustand je (NodeId, SegmentId, State). |
| `Messages/JunctionRestrictionsUpdateRequest.cs` | Client → Server: Änderungswunsch (NodeId, SegmentId, State). |
| `Services/JunctionRestrictionsEventListener.cs` | Harmony-Hooks auf TM:PE, leitet lokale Änderungen in CSM-Befehle um; broadcastet knotenweit. |
| `Services/JunctionRestrictionsSynchronization.cs` | Gemeinsamer Versandweg (Server→Alle oder Client→Server) und Lese/Anwenden-Fassade. |

### 4) Workflow (Server/Host und Client)
- Host ändert Kreuzungsbeschränkung in TM:PE
  - Harmony-Postfix erkennt Änderung und bestimmt `nodeId` (Kreuzung).
  - Listener liest alle Segmentenden am Knoten und sendet für jedes Ende `JunctionRestrictionsApplied` an alle.
  - Clients wenden Werte lokal an (Ignore-Scope verhindert Schleifen).

- Client ändert Kreuzungsbeschränkung in TM:PE
  - Harmony-Postfix sendet `JunctionRestrictionsUpdateRequest` an den Server (genau für das betroffene Segmentende am Knoten).
  - Server validiert (Node/Segment vorhanden) und setzt die Restriktionen in TM:PE an diesem Ende.
  - Server liest danach den gesamten Knoten-Zustand und broadcastet pro Segmentende `JunctionRestrictionsApplied` an alle.
  - Alle wenden Werte lokal an (Client eingeschlossen; Ignore-Scope verhindert Re-Trigger).

- Hinweise
  - Nur der betroffene Knoten wird verarbeitet (max. 8 Segmentenden); keine Traversierung zu Nachbarknoten.
  - Lane-IDs/-Indices werden nicht übertragen; das Zielende wird über `(NodeId, SegmentId)` identifiziert und `isStartNode` lokal bestimmt.

### 5) Datenaustausch (Nachrichten/Felder)
| Nachricht | Richtung | Feld | Typ | Beschreibung |
| --- | --- | --- | --- | --- |
| `JunctionRestrictionsUpdateRequest` | Client → Server | `NodeId` | `ushort` | Knoten-ID der Kreuzung. |
|  |  | `SegmentId` | `ushort` | Segment-ID am Knoten (betroffenes Segmentende). |
|  |  | `State.AllowUTurns` | `bool` | U-Turns erlaubt/verboten. |
|  |  | `State.AllowLaneChangesWhenGoingStraight` | `bool` | Geradeaus-Spurwechsel erlaubt/verboten. |
|  |  | `State.AllowEnterWhenBlocked` | `bool` | In blockierte Kreuzung einfahren erlaubt/verboten. |
|  |  | `State.AllowPedestrianCrossing` | `bool` | Fußgängerüberweg erlaubt/verboten. |
|  |  | `State.AllowNearTurnOnRed` | `bool` | Nahes Rechtsabbiegen bei Rot erlaubt/verboten. |
|  |  | `State.AllowFarTurnOnRed` | `bool` | Fernes Rechtsabbiegen bei Rot erlaubt/verboten. |
| `JunctionRestrictionsAppliedCommand` | Server → Alle | `NodeId` | `ushort` | Knoten-ID der Kreuzung. |
|  |  | `SegmentId` | `ushort` | Segment-ID am Knoten (Ziel-Segmentende). |
|  |  | `State.*` | `bool` | Effektiv angewandte Werte nach TM:PE (vollständiger Snapshot je Ende). |

---

## EN - English

### 1) Summary
- Synchronizes TM:PE junction restrictions (U-turn, straight lane change, enter blocked junction, pedestrian crossing, near/far turn on red) between host and clients in CSM sessions.
- Hooks local TM:PE changes via Harmony and relays them through CSM.
- Same as Priority Signs: after any change, the full node state is broadcast for each segment end to mirror TM:PE automation.

### 2) Directory Layout
- `src/CSM.TmpeSync.JunctionRestrictions/`
  - `JunctionRestrictionsFeature.cs` - Feature bootstrap (enables listener).
  - `Handlers/` - CSM command handlers (server/client processing).
  - `Messages/` - Network commands (ProtoBuf contracts).
  - `Services/` - TM:PE adapter, Harmony listener, sync helpers.

### 3) File Overview
| File | Purpose |
| --- | --- |
| `JunctionRestrictionsFeature.cs` | Registers/enables the feature and the TM:PE listener. |
| `Handlers/JunctionRestrictionsAppliedCommandHandler.cs` | Client: handles server "Applied" broadcast and applies locally in TM:PE. |
| `Handlers/JunctionRestrictionsUpdateRequestHandler.cs` | Server: validates request, applies at the segment end in TM:PE, and broadcasts the whole node state. |
| `Messages/JunctionRestrictionsAppliedCommand.cs` | Server → All: final state per (NodeId, SegmentId, State). |
| `Messages/JunctionRestrictionsUpdateRequest.cs` | Client → Server: change request (NodeId, SegmentId, State). |
| `Services/JunctionRestrictionsEventListener.cs` | Harmony hooks into TM:PE, translates local changes into CSM commands; broadcasts per node. |
| `Services/JunctionRestrictionsSynchronization.cs` | Common dispatch path (Server→All or Client→Server) and read/apply facade. |

### 4) Workflow (Server/Host and Client)
- Host edits a junction restriction in TM:PE
  - Harmony postfix detects the change and determines `nodeId` (junction).
  - Listener reads all segment ends at the node and sends `JunctionRestrictionsApplied` for each end to all.
  - Clients apply locally (ignore scope prevents feedback loops).

- Client edits a junction restriction in TM:PE
  - Harmony postfix sends a `JunctionRestrictionsUpdateRequest` to the server (for the targeted segment end at the node).
  - Server validates (node/segment exist) and applies in TM:PE at that end.
  - Server then reads the full node state and broadcasts `JunctionRestrictionsApplied` for each segment end to all.
  - Everyone applies locally; ignore scope prevents re-triggering.

- Notes
  - Only the affected node is processed (up to 8 segment ends); no traversal to neighbouring nodes.
  - Lane IDs/indices are not transmitted; the target end is identified via `(NodeId, SegmentId)` and `isStartNode` is resolved locally.

### 5) Data Exchange (messages/fields)
| Message | Direction | Field | Type | Description |
| --- | --- | --- | --- | --- |
| `JunctionRestrictionsUpdateRequest` | Client → Server | `NodeId` | `ushort` | Node ID of the junction. |
|  |  | `SegmentId` | `ushort` | Segment ID at the node (affected segment end). |
|  |  | `State.AllowUTurns` | `bool` | U-turns allowed/forbidden. |
|  |  | `State.AllowLaneChangesWhenGoingStraight` | `bool` | Straight lane changes allowed/forbidden. |
|  |  | `State.AllowEnterWhenBlocked` | `bool` | Entering a blocked junction allowed/forbidden. |
|  |  | `State.AllowPedestrianCrossing` | `bool` | Pedestrian crossing allowed/forbidden. |
|  |  | `State.AllowNearTurnOnRed` | `bool` | Near-side right turn on red allowed/forbidden. |
|  |  | `State.AllowFarTurnOnRed` | `bool` | Far-side right turn on red allowed/forbidden. |
| `JunctionRestrictionsAppliedCommand` | Server → All | `NodeId` | `ushort` | Node ID of the junction. |
|  |  | `SegmentId` | `ushort` | Segment ID at the node (the target segment end). |
|  |  | `State.*` | `bool` | Effective values after TM:PE (full snapshot per end). |

