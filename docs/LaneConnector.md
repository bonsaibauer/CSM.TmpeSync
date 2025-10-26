# CSM TM:PE Sync - Lane Connector / Fahrbahnverbindungen

This document is bilingual. German (DE) first, English (EN) follows.

---

## DE - Deutsch

### 1) Kurzbeschreibung
- Synchronisiert TM:PE-Lane-Connector-Verbindungen zwischen Host und Clients in CSM-Sitzungen.
- Fängt lokale Änderungen in TM:PE per Harmony ab und überträgt sie via CSM.
- End-basiertes Schema wie bei Lane Arrows: Übertragung erfolgt pro Segmentende mit stabilen Ordinals (kein Versand von LaneId/LaneIndex).

### 2) Dateistruktur
- `src/CSM.TmpeSync.LaneConnector/`
  - `LaneConnectorSyncFeature.cs` - Feature-Bootstrap (aktiviert Listener).
  - `Handlers/` - CSM-Command-Handler (Server/Client Verarbeitungslogik).
  - `Messages/` - Netzwerkbefehle (ProtoBuf-Verträge).
  - `Services/` - TM:PE-Adapter, Harmony-Listener, End-Auswahl, Apply/Read-Fassade.

### 3) Dateiübersicht
| Datei | Zweck |
| --- | --- |
| `LaneConnectorSyncFeature.cs` | Registriert/aktiviert das Feature und den TM:PE-Listener. |
| `Handlers/LaneConnectionsAppliedCommandHandler.cs` | Client: verarbeitet Server-Broadcast „Applied“ (ordinal-basiert) und setzt die Verbindungen lokal in TM:PE. |
| `Handlers/LaneConnectionsUpdateRequestHandler.cs` | Server: validiert Request, setzt Verbindungen ordinal-basiert in TM:PE und broadcastet den End-Zustand. |
| `Messages/LaneConnectionsAppliedCommand.cs` | Server → Alle: endgültiger Zustand je Segmentende (NodeId, SegmentId, StartNode, Items). |
| `Messages/LaneConnectionsUpdateRequest.cs` | Client → Server: Änderungswunsch je Segmentende (NodeId, SegmentId, StartNode, Items). |
| `Services/LaneConnectorEventListener.cs` | Harmony-Hooks auf TM:PE; leitet lokale Änderungen in CSM-Befehle um; broadcastet pro Segmentende. |
| `Services/LaneConnectorSynchronization.cs` | Gemeinsamer Versandweg (Server→Alle oder Client→Server) und Lese/Anwenden-Fassade. |
| `Services/LaneConnectorTmpeAdapter.cs` | Apply/Read-Fassade mit lokalem Ignore-Scope (Echo-Schutz). |
| `Services/LaneConnectionAdapter.cs` | Niedrig-Level-Adapter zur TM:PE LaneConnectionManager-Implementierung (lesen/anwenden). |
| `Services/LaneConnectorEndSelector.cs` | Ermittelt Kandidaten-Lanes am Segmentende in stabiler Reihenfolge (Ordinalbildung). |

### 4) Workflow (Server/Host und Client)
- Host ändert Lane-Connection in TM:PE
  - Harmony-Postfix erkennt Änderung, bestimmt betroffenes Segmentende (`nodeId`, `segmentId`, `startNode`).
  - Listener baut einen End-Snapshot: für jede Querschnitts-Lane (Ordinal) wird die Liste der Ziel-Ordinals ermittelt.
  - Server sendet `LaneConnectionsApplied` (kompletter End-Zustand) an alle.
  - Clients wenden Werte lokal an (Ignore-Scope verhindert Schleifen).

- Client ändert Lane-Connection in TM:PE
  - Harmony-Postfix erstellt denselben End-Snapshot und sendet `LaneConnectionsUpdateRequest` an den Server.
  - Server validiert (Node/Segment vorhanden), mappt Ordinals → lokale LaneIds und setzt die Verbindungen in TM:PE (Ignore-Scope).
  - Server liest danach den End-Zustand und broadcastet `LaneConnectionsApplied` an alle.
  - Alle wenden Werte lokal an; Echo wird durch Ignore-Scope unterdrückt.

- Abgelehnte Requests (Server)
  - Bei fehlenden Entitäten oder TM:PE-Fehlern sendet der Server `RequestRejected` zurück (Grund + Entität).

Hinweise
- Es werden keine `laneId` oder `laneIndex` übertragen. Ordinals sind stabil innerhalb eines Segmentendes (Sortierung per `NetInfo.Lane.m_position`).
- Bei Netzänderungen zwischen Event und Apply werden fehlende Lanes übersprungen; der nachgelagerte Broadcast korrigiert den Zustand.

### 5) Datenaustausch (Nachrichten/Felder)
| Nachricht | Richtung | Feld | Typ | Beschreibung |
| --- | --- | --- | --- | --- |
| `LaneConnectionsUpdateRequest` | Client → Server | `NodeId` | `ushort` | Knoten-ID der Kreuzung. |
|  |  | `SegmentId` | `ushort` | Segment-ID am Knoten (betroffenes Segmentende). |
|  |  | `StartNode` | `bool` | True: Segmentanfang, False: Segmentende. |
|  |  | `Items` | `List<Entry>` | Liste pro Querschnitts-Lane dieses Endes. |
|  |  | `Items[].SourceOrdinal` | `int` | Ordinal (0..n-1) der Querschnitts-Lane am Endesegment. |
|  |  | `Items[].TargetOrdinals` | `List<int>` | Ziel-Ordinals (0..n-1) innerhalb desselben Segmentendes. |
| `LaneConnectionsAppliedCommand` | Server → Alle | `NodeId` | `ushort` | Knoten-ID der Kreuzung. |
|  |  | `SegmentId` | `ushort` | Segment-ID am Knoten (Ziel-Segmentende). |
|  |  | `StartNode` | `bool` | True: Segmentanfang, False: Segmentende. |
|  |  | `Items` | `List<Entry>` | Effektiv angewandter End-Zustand (Ordinals). |
|  |  | `Items[].SourceOrdinal` | `int` | Ordinal der Querschnitts-Lane. |
|  |  | `Items[].TargetOrdinals` | `List<int>` | Ziel-Ordinals (0..n-1). |
| `RequestRejected` | Server → Client | `EntityType` | `byte` | Entitätstyp: 3=Node, 2=Segment, 1=Lane (kontextabhängig). |
|  |  | `EntityId` | `int` | Betroffene Entität. |
|  |  | `Reason` | `string` | Grund, z. B. `entity_missing`, `tmpe_apply_failed`. |

---

## EN - English

### 1) Summary
- Synchronizes TM:PE lane connector connections between host and clients in CSM sessions.
- Hooks local TM:PE changes with Harmony and relays them via CSM.
- End-based schema like Lane Arrows: messages are per segment end using stable ordinals (no LaneId/LaneIndex transmitted).

### 2) Directory Layout
- `src/CSM.TmpeSync.LaneConnector/`
  - `LaneConnectorSyncFeature.cs` - Feature bootstrap (enables listener).
  - `Handlers/` - CSM command handlers (server/client processing).
  - `Messages/` - Network commands (ProtoBuf contracts).
  - `Services/` - TM:PE adapters, Harmony listener, end selection, apply/read helpers.

### 3) File Overview
| File | Purpose |
| --- | --- |
| `LaneConnectorSyncFeature.cs` | Registers/enables the feature and the TM:PE listener. |
| `Handlers/LaneConnectionsAppliedCommandHandler.cs` | Client: handles server “Applied” broadcast (ordinal-based) and applies locally in TM:PE. |
| `Handlers/LaneConnectionsUpdateRequestHandler.cs` | Server: validates request, applies ordinal-based connections in TM:PE, and broadcasts the end state. |
| `Messages/LaneConnectionsAppliedCommand.cs` | Server → All: final state per segment end (NodeId, SegmentId, StartNode, Items). |
| `Messages/LaneConnectionsUpdateRequest.cs` | Client → Server: desired end state per segment end (NodeId, SegmentId, StartNode, Items). |
| `Services/LaneConnectorEventListener.cs` | Harmony hooks into TM:PE, translates local changes into CSM commands; broadcasts per segment end. |
| `Services/LaneConnectorSynchronization.cs` | Common dispatch (Server→All or Client→Server) and read/apply facade. |
| `Services/LaneConnectorTmpeAdapter.cs` | Apply/read facade with local ignore scope (feedback-loop protection). |
| `Services/LaneConnectionAdapter.cs` | Low-level adapter to TM:PE LaneConnectionManager (read/apply). |
| `Services/LaneConnectorEndSelector.cs` | Enumerates candidate lanes at the segment end in stable order (ordinal mapping). |

### 4) Workflow (Server/Host and Client)
- Host edits lane connections in TM:PE
  - Harmony postfix detects the change, identifies the affected segment end (`nodeId`, `segmentId`, `startNode`).
  - Listener builds an end snapshot: for each cross-section lane (ordinal), it collects target ordinals.
  - Server broadcasts `LaneConnectionsApplied` (full end state) to all.
  - Clients apply locally (ignore scope prevents feedback loops).

- Client edits lane connections in TM:PE
  - Harmony postfix builds the same end snapshot and sends `LaneConnectionsUpdateRequest` to the server.
  - Server validates (node/segment exist), maps ordinals → local lane IDs, applies in TM:PE (ignore scope).
  - Server then reads the end state and broadcasts `LaneConnectionsApplied` to all.
  - Everyone applies locally; echo is suppressed via the ignore scope.

- Rejections (Server)
  - If entities are missing or TM:PE fails to apply, the server returns `RequestRejected` with reason and entity info.

Notes
- No `laneId` or `laneIndex` is sent. Ordinals are stable within a segment end (sorted by `NetInfo.Lane.m_position`).
- If the network changes between event and apply, missing lanes are skipped; the subsequent authoritative broadcast corrects state.

### 5) Data Exchange (messages/fields)
| Message | Direction | Field | Type | Description |
| --- | --- | --- | --- | --- |
| `LaneConnectionsUpdateRequest` | Client → Server | `NodeId` | `ushort` | Node ID of the junction. |
|  |  | `SegmentId` | `ushort` | Segment ID at the node (the affected segment end). |
|  |  | `StartNode` | `bool` | True = segment start, False = segment end. |
|  |  | `Items` | `List<Entry>` | One entry per cross-section lane at this end. |
|  |  | `Items[].SourceOrdinal` | `int` | Ordinal (0..n-1) for the source lane at this end. |
|  |  | `Items[].TargetOrdinals` | `List<int>` | Target ordinals (0..n-1) within the same end. |
| `LaneConnectionsAppliedCommand` | Server → All | `NodeId` | `ushort` | Node ID of the junction. |
|  |  | `SegmentId` | `ushort` | Segment ID at the node (the target end). |
|  |  | `StartNode` | `bool` | True = segment start, False = segment end. |
|  |  | `Items` | `List<Entry>` | Effective applied end state (ordinals). |
|  |  | `Items[].SourceOrdinal` | `int` | Ordinal of the cross-section lane. |
|  |  | `Items[].TargetOrdinals` | `List<int>` | Target ordinals (0..n-1). |
| `RequestRejected` | Server → Client | `EntityType` | `byte` | Entity type: 3=Node, 2=Segment, 1=Lane (context-dependent). |
|  |  | `EntityId` | `int` | Affected entity. |
|  |  | `Reason` | `string` | Reason, e.g., `entity_missing`, `tmpe_apply_failed`. |

