# CSM TM:PE Sync - Lane Connector / Fahrbahnverbindungen

This document is bilingual. German (DE) first, English (EN) follows.

---

## DE - Deutsch

### 1) Kurzbeschreibung
- Synchronisiert TM:PE-Lane-Connector-Verbindungen zwischen Host und Clients in CSM-Sitzungen (Segmentende → Zielspuren).
- Nutzt das Segmentend-Schema wie Lane Arrows: Kommunikation pro `(NodeId, SegmentId, StartNode)` mit stabilen Spur-Ordinals (kein Versand von `laneId`/`laneIndex`).
- Resiliente Apply-Pipeline: Apply-Koordinator sammelt Requests pro Segmentende, überprüft TM:PE-Manager (LaneConnectionManager + Datenbank), führt Backoff-Retrys aus und verhindert Echo über Ignore-Scope.

### 2) Dateistruktur
- `src/CSM.TmpeSync.LaneConnector/`
  - `LaneConnectorSyncFeature.cs` – Feature-Bootstrap (aktiviert Listener).
  - `Handlers/` – CSM-Command-Handler für Server/Client (inkl. Retry-Integration).
  - `Messages/` – Netzwerkbefehle (ProtoBuf-Verträge) für Requests/Broadcasts.
  - `Services/` – Harmony-Listener, End-Selektor, Apply-Koordinator, TM:PE-Adapter.

### 3) Dateiübersicht
| Datei | Zweck |
| --- | --- |
| `LaneConnectorSyncFeature.cs` | Registriert/aktiviert das Feature und den TM:PE-Listener. |
| `Handlers/LaneConnectionsAppliedCommandHandler.cs` | Client: verarbeitet Host-Broadcasts und nutzt den Apply-Koordinator zum lokalen Anwenden (inkl. Retry). |
| `Handlers/LaneConnectionsUpdateRequestHandler.cs` | Server: validiert Requests, wendet sie via Apply-Koordinator an und broadcastet nach Erfolg. |
| `Messages/LaneConnectionsAppliedCommand.cs` | Server → Alle: angewandter Zustand eines Segmentendes (Ordinals + Ziel-Ordinals). |
| `Messages/LaneConnectionsUpdateRequest.cs` | Client → Server: gewünschter Zustand eines Segmentendes (Ordinals + Ziel-Ordinals). |
| `Services/LaneConnectorEventListener.cs` | Harmony-Hooks auf TM:PE, erkennt lokale Änderungen und erstellt Snapshot pro Segmentende. |
| `Services/LaneConnectorSynchronization.cs` | Gemeinsamer Versandweg, Apply-Koordinator mit Retry/Backoff, Merge-Logik für konkurrierende Requests. |
| `Services/LaneConnectorTmpeAdapter.cs` | Apply-/Read-Fassade zu TM:PE (Ignore-Scope, LaneId-Auflösung). |
| `Services/LaneConnectionAdapter.cs` | Niedrig-Level-Adapter für LaneConnectionManager (lesen/anwenden). |
| `Services/LaneConnectorEndSelector.cs` | Ermittelt Kandidaten-Lanes eines Segmentendes in stabiler Reihenfolge (Ordinalbildung). |

### 4) Workflow (Server/Host und Client)
- **Host ändert Lane-Connections in TM:PE**
  - Harmony-Postfix erkennt die Änderung, bestimmt `(nodeId, segmentId, startNode)` und erstellt einen Snapshot aller Querschnitts-Lanes inkl. Ziel-Ordinals.
  - Synchronisation broadcastet `LaneConnectionsAppliedCommand`; Clients wenden den Zustand über den Apply-Koordinator an (Retry solange TM:PE-Manager/Datenbank nicht bereit).

- **Client ändert Lane-Connections in TM:PE**
  - Harmony-Postfix erstellt denselben Snapshot und sendet `LaneConnectionsUpdateRequest` an den Server.
  - Server validiert Knoten/Segment, übergibt den Request dem Apply-Koordinator.
  - Apply-Koordinator prüft LaneConnectionManager + Connection-DB (Reflection), mappt Ordinals → lokale LaneIds und versucht das Apply. Bei `NullReferenceException`/fehlenden Managern werden bis zu sechs Versuche mit wachsender Frame-Verzögerung (5 → 240) gestartet; konkurrierende Requests werden zusammengeführt.
  - Nach Erfolg broadcastet der Server `LaneConnectionsAppliedCommand` mit dem gelesenen Endzustand.

- **Abgelehnte Requests (Server)**
  - Fehlende Entitäten oder endgültige TM:PE-Fehler führen zu `RequestRejected` (Grund + Entitätstyp/-ID).

### 5) Datenaustausch (Nachrichten/Felder)
| Nachricht | Richtung | Feld | Typ | Beschreibung |
| --- | --- | --- | --- | --- |
| `LaneConnectionsUpdateRequest` | Client → Server | `NodeId` | `ushort` | Knoten-ID der Kreuzung. |
|  |  | `SegmentId` | `ushort` | Segment-ID am Knoten (betroffenes Segmentende). |
|  |  | `StartNode` | `bool` | `true` = Segmentanfang, `false` = Segmentende. |
|  |  | `Items[]` | `List<Entry>` | Liste pro Querschnitts-Lane (siehe unten). |
|  |  | `Items[].SourceOrdinal` | `int` | Ordinal (0..n-1) der Ausgangsspur am Segmentende. |
|  |  | `Items[].TargetOrdinals` | `List<int>` | Ziel-Ordinals (0..n-1) innerhalb desselben Segmentendes. |
| `LaneConnectionsAppliedCommand` | Server → Alle | `NodeId` | `ushort` | Knoten-ID der Kreuzung. |
|  |  | `SegmentId` | `ushort` | Segment-ID am Knoten (Ziel-Segmentende). |
|  |  | `StartNode` | `bool` | Wie oben. |
|  |  | `Items[]` | `List<Entry>` | Effektiv angewandte Zuordnungen (SourceOrdinal → TargetOrdinals). |
| `RequestRejected` | Server → Client | `EntityType` | `byte` | 3 = Node, 2 = Segment, 1 = Lane (kontextabhängig). |
|  |  | `EntityId` | `int` | Betroffene Entität. |
|  |  | `Reason` | `string` | Grund, z. B. `entity_missing`, `tmpe_apply_failed`. |

Hinweise
- Keine Übertragung von `laneId`/`laneIndex`; Ordinals werden anhand des aktuellen Segmentendes (Sortierung nach `NetInfo.Lane.m_position`) gebildet.
- Apply-Koordinator vereint konkurrierende Requests pro Segmentende, verwaltet einen Retry-Backoff und prüft die Lane-Connection-Datenbank per Reflection.
- `LaneConnectorTmpeAdapter` nutzt Ignore-Scopes, damit eigene Applies nicht erneut Listener-Hooks triggern; fehlende Lanes werden übersprungen und durch den Host-Broadcast korrigiert.

---

## EN - English

### 1) Summary
- Synchronises TM:PE lane connector links between host and clients in CSM sessions (segment-end → target lanes).
- Uses the lane-end schema like Lane Arrows: communication per `(NodeId, SegmentId, StartNode)` with stable ordinals (no `laneId`/`laneIndex` on the wire).
- Resilient apply pipeline: an apply coordinator collects requests per segment end, checks LaneConnectionManager/readiness, retries with backoff, and shields against echo via ignore scope.

### 2) Directory Layout
- `src/CSM.TmpeSync.LaneConnector/`
  - `LaneConnectorSyncFeature.cs` – Feature bootstrap (enables listener).
  - `Handlers/` – CSM command handlers for server/client (retry-aware).
  - `Messages/` – Network commands (ProtoBuf contracts) for requests/broadcasts.
  - `Services/` – Harmony listener, end selector, apply coordinator, TM:PE adapters.

### 3) File Overview
| File | Purpose |
| --- | --- |
| `LaneConnectorSyncFeature.cs` | Registers/enables the feature and the TM:PE listener. |
| `Handlers/LaneConnectionsAppliedCommandHandler.cs` | Client: processes host broadcasts and applies locally via the retry-capable coordinator. |
| `Handlers/LaneConnectionsUpdateRequestHandler.cs` | Server: validates, delegates to the apply coordinator, and broadcasts after success. |
| `Messages/LaneConnectionsAppliedCommand.cs` | Server → All: applied state per segment end (source ordinal + target ordinals). |
| `Messages/LaneConnectionsUpdateRequest.cs` | Client → Server: desired state per segment end (source ordinal + target ordinals). |
| `Services/LaneConnectorEventListener.cs` | Harmony hooks into TM:PE and captures local changes as end snapshots. |
| `Services/LaneConnectorSynchronization.cs` | Common dispatch path, apply coordinator with retry/backoff, merge logic for competing requests. |
| `Services/LaneConnectorTmpeAdapter.cs` | Apply/read façade with ignore scope and laneId resolution. |
| `Services/LaneConnectionAdapter.cs` | Low-level adapter to TM:PE’s LaneConnectionManager (read/apply). |
| `Services/LaneConnectorEndSelector.cs` | Enumerates segment-end lanes in stable order (ordinal mapping). |

### 4) Workflow (Server/Host and Client)
- **Host edits lane connections in TM:PE**
  - Harmony postfix detects the change, determines `(nodeId, segmentId, startNode)`, builds a snapshot of all cross-section lanes including their target ordinals.
  - Synchronisation broadcasts `LaneConnectionsAppliedCommand`; clients feed it into the apply coordinator, which retries until TM:PE managers/databases are ready.

- **Client edits lane connections in TM:PE**
  - Harmony postfix builds the same snapshot and sends `LaneConnectionsUpdateRequest` to the server.
  - The server validates node/segment and forwards the request to the apply coordinator.
  - Apply coordinator checks LaneConnectionManager readiness (including connection database via reflection), maps ordinals → local lane IDs, and applies via TM:PE. `NullReferenceException` or missing managers trigger retries with increasing frame delays (5 → 240, up to six attempts); concurrent requests are merged.
  - After success the server reads back the end state and broadcasts `LaneConnectionsAppliedCommand` to everyone.

- **Rejections (Server)**
  - Missing entities or unrecoverable TM:PE failures result in `RequestRejected` (reason + entity info).

### 5) Data Exchange (messages/fields)
| Message | Direction | Field | Type | Description |
| --- | --- | --- | --- | --- |
| `LaneConnectionsUpdateRequest` | Client → Server | `NodeId` | `ushort` | Node ID of the junction. |
|  |  | `SegmentId` | `ushort` | Segment ID at the node (segment end). |
|  |  | `StartNode` | `bool` | `true` = segment start, `false` = segment end. |
|  |  | `Items[]` | `List<Entry>` | Entries per cross-section lane (see below). |
|  |  | `Items[].SourceOrdinal` | `int` | Ordinal (0..n-1) for the source lane. |
|  |  | `Items[].TargetOrdinals` | `List<int>` | Target ordinals (0..n-1) within the same end. |
| `LaneConnectionsAppliedCommand` | Server → All | `NodeId` | `ushort` | Node ID of the junction. |
|  |  | `SegmentId` | `ushort` | Segment ID at the node (target end). |
|  |  | `StartNode` | `bool` | Same semantics as above. |
|  |  | `Items[]` | `List<Entry>` | Applied mapping (source ordinal → target ordinals). |
| `RequestRejected` | Server → Client | `EntityType` | `byte` | 3 = node, 2 = segment, 1 = lane (context-dependent). |
|  |  | `EntityId` | `int` | Affected entity. |
|  |  | `Reason` | `string` | Reason, e.g. `entity_missing`, `tmpe_apply_failed`. |

Notes
- No `laneId`/`laneIndex` is transmitted; ordinals are derived from the current segment end (sorted by `NetInfo.Lane.m_position`).
- The apply coordinator merges concurrent requests per segment end, runs retry/backoff (5/15/30/60/120/240 frames), and checks LaneConnectionManager connection databases before applying.
- `LaneConnectorTmpeAdapter` wraps TM:PE calls with ignore scopes; missing lanes are skipped and later corrected by the authoritative broadcast.
