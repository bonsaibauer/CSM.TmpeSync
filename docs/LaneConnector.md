# CSM TM:PE Sync – Lane Connector / Fahrbahnverbindungen

This document is bilingual. German (DE) first, English (EN) follows.

---

## DE – Deutsch

### 1) Kurzbeschreibung
- Synchronisiert TM:PE-Lane-Connector-Verbindungen zwischen Host und Clients in CSM-Sitzungen (Segmentende -> Zielspuren).
- Bildet stabile Spur-Ordinals pro `(NodeId, SegmentId, StartNode)` anhand der TM:PE-Lane-Metadaten (LaneTypes/VehicleTypes) und `LaneConnectionManager.GetLaneEndPoint`; `laneId`/`laneIndex` werden nicht übertragen.
- Resiliente Apply-Pipeline: Apply-Koordinator bündelt Requests pro Segmentende, prüft LaneConnectionManager + Connection-Datenbank, führt Backoff-Retrys aus und verhindert Echo per Ignore-Scope.

### 2) Dateistruktur
- `src/CSM.TmpeSync.LaneConnector/`
  - `LaneConnectorSyncFeature.cs` – Feature-Bootstrap (aktiviert den Listener).
  - `Handlers/` – CSM-Command-Handler für Server/Client inkl. Retry-Integration.
  - `Messages/` – Netzwerkbefehle (ProtoBuf-Verträge) für Requests/Broadcasts.
  - `Services/` – Harmony-Listener, End-Selektor, Apply-Koordinator, TM:PE-Adapter.

### 3) Dateiübersicht
| Datei | Zweck |
| --- | --- |
| `LaneConnectorSyncFeature.cs` | Registriert/aktiviert das Feature und den TM:PE-Listener. |
| `Handlers/LaneConnectionsAppliedCommandHandler.cs` | Client: verarbeitet Host-Broadcasts, nutzt den Apply-Koordinator (Retry + Merge). |
| `Handlers/LaneConnectionsUpdateRequestHandler.cs` | Server: validiert Requests, wendet sie via Apply-Koordinator an und broadcastet nach Erfolg. |
| `Messages/LaneConnectionsAppliedCommand.cs` | Server -> Alle: angewandter Zustand eines Segmentendes (Ordinals + Ziel-Ordinals). |
| `Messages/LaneConnectionsUpdateRequest.cs` | Client -> Server: gewünschter Zustand eines Segmentendes (Ordinals + Ziel-Ordinals). |
| `Services/LaneConnectorEventListener.cs` | Harmony-Hooks auf TM:PE; nutzt den von TM:PE gelieferten `sourceStartNode`, erstellt Snapshots pro Segmentende. |
| `Services/LaneConnectorSynchronization.cs` | Gemeinsamer Versandweg, Apply-Koordinator mit Retry/Backoff, Merge-Logik für konkurrierende Requests. |
| `Services/LaneConnectorTmpeAdapter.cs` | Apply-/Read-Fassade zu TM:PE (Ignore-Scope, Übergang zu Lane IDs). |
| `Services/LaneConnectionAdapter.cs` | Niedrig-Level-Adapter für LaneConnectionManager (Lesen/Anwenden via Reflection). |
| `Services/LaneConnectorEndSelector.cs` | Selektiert TM:PE-kompatible Kandidaten-Spuren am Segmentende in stabiler Reihenfolge (Ordinalbildung). |

### 4) Workflow (Server/Host und Client)
- **Host ändert Lane-Connections in TM:PE**
  - Harmony-Postfix erhält `sourceStartNode`, ermittelt `(nodeId, segmentId)` und erstellt einen Snapshot aller zulässigen Lanes (gefiltert über LaneTypes/VehicleTypes und `GetLaneEndPoint`).
  - Synchronisation broadcastet `LaneConnectionsAppliedCommand`; Clients wenden den Zustand über den Apply-Koordinator an (Retry solange LaneConnectionManager/DB nicht bereit).

- **Client ändert Lane-Connections in TM:PE**
  - Harmony-Postfix erstellt denselben Snapshot und sendet `LaneConnectionsUpdateRequest` an den Server.
  - Server validiert Knoten/Segment, übergibt den Request dem Apply-Koordinator.
  - Apply-Koordinator mappt Ordinals -> lokale Lane IDs, prüft LaneConnectionManager + Connection-DB und versucht das Apply. Bei Bedarf bis zu sechs Versuche mit wachsender Frame-Verzögerung (5/15/30/60/120/240); konkurrierende Requests werden zusammengeführt.
  - Nach Erfolg broadcastet der Server `LaneConnectionsAppliedCommand` mit dem vom Manager gelesenen Endzustand.

- **Abgelehnte Requests (Server)**
  - Fehlende Entitäten oder finale TM:PE-Fehler führen zu `RequestRejected` (Grund + Entitätstyp/-ID).

### 5) Datenaustausch (Nachrichten/Felder)
| Nachricht | Richtung | Feld | Typ | Beschreibung |
| --- | --- | --- | --- | --- |
| `LaneConnectionsUpdateRequest` | Client -> Server | `NodeId` | `ushort` | Knoten-ID der Kreuzung. |
|  |  | `SegmentId` | `ushort` | Segment-ID am Knoten (betroffenes Segmentende). |
|  |  | `StartNode` | `bool` | `true` = Segmentanfang, `false` = Segmentende. |
|  |  | `Items[]` | `List<Entry>` | Liste pro Querschnitts-Lane (siehe unten). |
|  |  | `Items[].SourceOrdinal` | `int` | Ordinal (0..n-1) der Ausgangsspur am Segmentende. |
|  |  | `Items[].TargetOrdinals` | `List<int>` | Ziel-Ordinals (0..n-1) innerhalb desselben Segmentendes. |
| `LaneConnectionsAppliedCommand` | Server -> Alle | `NodeId` | `ushort` | Knoten-ID der Kreuzung. |
|  |  | `SegmentId` | `ushort` | Segment-ID am Knoten (Ziel-Segmentende). |
|  |  | `StartNode` | `bool` | Wie oben. |
|  |  | `Items[]` | `List<Entry>` | Effektiv angewandte Zuordnungen (SourceOrdinal -> TargetOrdinals). |
| `RequestRejected` | Server -> Client | `EntityType` | `byte` | 3 = Node, 2 = Segment, 1 = Lane (kontextabhängig). |
|  |  | `EntityId` | `int` | Betroffene Entität. |
|  |  | `Reason` | `string` | Grund, z. B. `entity_missing`, `tmpe_apply_failed`. |

Hinweise
- Keine Übertragung von `laneId`/`laneIndex`; Ordinals werden anhand der TM:PE-Metadaten (Sortierung nach `NetInfo.Lane.m_position`, Filter über LaneTypes/VehicleTypes, Validierung via `GetLaneEndPoint`) abgeleitet.
- Der Apply-Koordinator merged konkurrierende Requests pro Segmentende, führt Retry/Backoff (5/15/30/60/120/240 Frames) aus und prüft LaneConnectionManager + Connection-DB vor jedem Apply.
- `LaneConnectorTmpeAdapter` kapselt TM:PE-Aufrufe mit Ignore-Scopes; fehlen während des Applys einzelne Lanes, korrigiert der autoritative Host-Broadcast den Zustand.

---

## EN – English

### 1) Summary
- Synchronises TM:PE lane connections between host and clients during CSM sessions (segment end -> target lanes).
- Builds stable lane ordinals per `(NodeId, SegmentId, StartNode)` using TM:PE lane metadata (`LaneTypes`, `VehicleTypes`, `GetLaneEndPoint`); no `laneId`/`laneIndex` values are transmitted.
- Resilient apply pipeline: an apply coordinator batches per segment end, checks the LaneConnectionManager and connection database, applies retry/backoff, and suppresses local Harmony echoes.

### 2) Folder layout
- `src/CSM.TmpeSync.LaneConnector/`
  - `LaneConnectorSyncFeature.cs` – feature bootstrap (enables the listener).
  - `Handlers/` – CSM command handlers for server/client (with retry integration).
  - `Messages/` – network commands (ProtoBuf contracts) for requests/broadcasts.
  - `Services/` – Harmony listener, end selector, apply coordinator, TM:PE adapter.

### 3) File overview
| File | Purpose |
| --- | --- |
| `LaneConnectorSyncFeature.cs` | Registers/enables the feature and TM:PE listener. |
| `Handlers/LaneConnectionsAppliedCommandHandler.cs` | Client: consumes host broadcasts and runs the apply coordinator (retry + merge). |
| `Handlers/LaneConnectionsUpdateRequestHandler.cs` | Server: validates requests, applies via coordinator, broadcasts on success. |
| `Messages/LaneConnectionsAppliedCommand.cs` | Server -> all: applied state for a segment end (ordinals + target ordinals). |
| `Messages/LaneConnectionsUpdateRequest.cs` | Client -> server: desired state for a segment end (ordinals + target ordinals). |
| `Services/LaneConnectorEventListener.cs` | Harmony hooks on TM:PE; uses TM:PE-provided `sourceStartNode`, snapshots segment ends. |
| `Services/LaneConnectorSynchronization.cs` | Shared transport, retry/backoff coordinator, merge logic for competing requests. |
| `Services/LaneConnectorTmpeAdapter.cs` | TM:PE façade (ignore scope, translation to lane IDs). |
| `Services/LaneConnectionAdapter.cs` | Low-level adapter for LaneConnectionManager (read/apply via reflection). |
| `Services/LaneConnectorEndSelector.cs` | Selects TM:PE-compatible candidate lanes per segment end in a stable order. |

### 4) Workflow (host and client)
- **Host edits lane connections in TM:PE**
  - Harmony postfix receives `sourceStartNode`, resolves `(nodeId, segmentId)`, and snapshots all eligible lanes (filtered via LaneTypes/VehicleTypes + `GetLaneEndPoint`).
  - Synchronisation broadcasts `LaneConnectionsAppliedCommand`; clients apply via the coordinator (retry until the manager/database is ready).

- **Client edits lane connections in TM:PE**
  - Harmony postfix creates the same snapshot and sends `LaneConnectionsUpdateRequest` to the server.
  - Server validates the node/segment and queues the request in the coordinator.
  - Coordinator maps ordinals -> local lane IDs, checks the LaneConnectionManager + connection DB, and tries to apply. Up to six attempts with increasing frame delay (5/15/30/60/120/240); concurrent requests are merged.
  - On success the server broadcasts `LaneConnectionsAppliedCommand` with the authoritative state taken from TM:PE.

- **Rejected requests (server)**
  - Missing entities or unrecoverable TM:PE errors trigger `RequestRejected` (reason + entity type/id).

### 5) Message schema
| Message | Direction | Field | Type | Description |
| --- | --- | --- | --- | --- |
| `LaneConnectionsUpdateRequest` | Client -> server | `NodeId` | `ushort` | Node identifier. |
|  |  | `SegmentId` | `ushort` | Segment identifier at the node (segment end). |
|  |  | `StartNode` | `bool` | `true` = start node, `false` = end node. |
|  |  | `Items[]` | `List<Entry>` | One entry per cross-section lane. |
|  |  | `Items[].SourceOrdinal` | `int` | Ordinal (0..n-1) of the source lane at the segment end. |
|  |  | `Items[].TargetOrdinals` | `List<int>` | Ordinals (0..n-1) of the target lanes at the same segment end. |
| `LaneConnectionsAppliedCommand` | Server -> all | `NodeId` | `ushort` | Node identifier. |
|  |  | `SegmentId` | `ushort` | Segment identifier at the node (segment end). |
|  |  | `StartNode` | `bool` | As above. |
|  |  | `Items[]` | `List<Entry>` | Applied mappings (SourceOrdinal -> TargetOrdinals). |
| `RequestRejected` | Server -> client | `EntityType` | `byte` | 3 = node, 2 = segment, 1 = lane (context dependent). |
|  |  | `EntityId` | `int` | Affected entity. |
|  |  | `Reason` | `string` | Reason, e.g. `entity_missing`, `tmpe_apply_failed`. |

Notes
- No `laneId`/`laneIndex` is transmitted; ordinals are derived from TM:PE metadata (sorted by `NetInfo.Lane.m_position`, filtered by LaneTypes/VehicleTypes, validated via `GetLaneEndPoint`).
- The apply coordinator merges competing requests per segment end, runs retry/backoff (5/15/30/60/120/240 frames), and re-checks the LaneConnectionManager + connection DB before each attempt.
- `LaneConnectorTmpeAdapter` wraps TM:PE calls with ignore scopes; if a lane vanishes during apply, the authoritative host broadcast restores the state once TM:PE is ready.

