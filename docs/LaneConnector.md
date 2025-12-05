# CSM TM:PE Sync - Lane Connector / Fahrbahnverbindungen

This document is bilingual. German (DE) first, English (EN) follows.

---

## DE - Deutsch

### 1) Kurzbeschreibung
- Synchronisiert TM:PE-Lane-Connector-Verbindungen zwischen Host und Clients in CSM-Sitzungen (Segmentende -> Zielspuren).
- Uebertraegt die tatsaechlichen TM:PE-Lane-IDs (Quelle und Ziele) pro `(NodeId, SegmentId, StartNode)`; stabile Segment-/Lane-Indizes dienen nur lokal als Fallback.
- Resiliente Apply-Pipeline: Apply-Koordinator buendelt Requests pro Segmentende, prueft LaneConnectionManager + Connection-Datenbank, fuehrt Backoff-Retrys aus und verhindert Echo per Ignore-Scope.

### 2) Dateistruktur
- `src/CSM.TmpeSync.LaneConnector/`
  - `LaneConnectorSyncFeature.cs` - Feature-Bootstrap (aktiviert den Listener).
  - `Handlers/` - CSM-Command-Handler fuer Server/Client inkl. Retry-Integration.
  - `Messages/` - Netzwerkbefehle (ProtoBuf-Vertraege) fuer Requests/Broadcasts.
  - `Services/` - Harmony-Listener, End-Selektor, Apply-Koordinator, TM:PE-Adapter.

### 3) Dateiuebersicht
| Datei | Zweck |
| --- | --- |
| `LaneConnectorSyncFeature.cs` | Registriert/aktiviert das Feature und den TM:PE-Listener. |
| `Handlers/LaneConnectionsAppliedCommandHandler.cs` | Client: verarbeitet Host-Broadcasts, nutzt den Apply-Koordinator (Retry + Merge). |
| `Handlers/LaneConnectionsUpdateRequestHandler.cs` | Server: validiert Requests, wendet sie via Apply-Koordinator an und broadcastet nach Erfolg. |
| `Messages/LaneConnectionsAppliedCommand.cs` | Server -> Alle: angewandter Zustand eines Segmentendes (Lane-IDs + Ziel-Lane-IDs). |
| `Messages/LaneConnectionsUpdateRequest.cs` | Client -> Server: gewuenschter Zustand eines Segmentendes (Lane-IDs + Ziel-Lane-IDs). |
| `Services/LaneConnectorEventListener.cs` | Harmony-Hooks auf TM:PE; nutzt den von TM:PE gelieferten `sourceStartNode`, erstellt Snapshots pro Segmentende. |
| `Services/LaneConnectorSynchronization.cs` | Gemeinsamer Versandweg, Apply-Koordinator mit Retry/Backoff, Merge-Logik fuer konkurrierende Requests. |
| `Services/LaneConnectorTmpeAdapter.cs` | Apply-/Read-Fassade zu TM:PE (Ignore-Scope, Uebergang zu Lane IDs). |
| `Services/LaneConnectionAdapter.cs` | Niedrig-Level-Adapter fuer LaneConnectionManager (Lesen/Anwenden via Reflection). |
| `Services/LaneConnectorEndSelector.cs` | Selektiert TM:PE-kompatible Kandidaten-Spuren am Segmentende in stabiler Reihenfolge (Ordinalbildung). |

### 4) Workflow (Server/Host und Client)
- **Host aendert Lane-Connections in TM:PE**
  - Harmony-Postfix erhaelt `sourceStartNode`, ermittelt `(nodeId, segmentId)` und erstellt einen Snapshot aller zulaessigen Lanes (gefiltert ueber LaneTypes/VehicleTypes und `GetLaneEndPoint`).
  - Synchronisation broadcastet `LaneConnectionsAppliedCommand`; Clients wenden den Zustand ueber den Apply-Koordinator an (Retry solange LaneConnectionManager/DB nicht bereit).

- **Client aendert Lane-Connections in TM:PE**
  - Harmony-Postfix erstellt denselben Snapshot pro Segmentende und sendet `LaneConnectionsUpdateRequest` (Debug-Log; Ignore-Scope verhindert Echo) an den Server.
  - Server validiert Knoten/Segment, uebergibt den Request dem Apply-Koordinator.
  - Apply-Koordinator arbeitet direkt mit Lane-IDs, prueft LaneConnectionManager + Connection-DB und versucht das Apply. Bei Bedarf bis zu sechs Versuche mit wachsender Frame-Verzoegerung (5/15/30/60/120/240); konkurrierende Requests werden zusammengefuehrt.
  - Nach Erfolg broadcastet der Server `LaneConnectionsAppliedCommand` mit dem vom Manager gelesenen Endzustand.

- **Abgelehnte Requests (Server)**
  - Fehlende Entitaeten oder finale TM:PE-Fehler fuehren zu `RequestRejected` (Grund + Entitaetstyp/-ID).

### 5) Datenaustausch (Nachrichten/Felder)
| Nachricht | Richtung | Feld | Typ | Beschreibung |
| --- | --- | --- | --- | --- |
| `LaneConnectionsUpdateRequest` | Client -> Server | `NodeId` | `ushort` | Knoten-ID der Kreuzung. |
|  |  | `SegmentId` | `ushort` | Segment-ID am Knoten (betroffenes Segmentende). |
|  |  | `StartNode` | `bool` | `true` = Segmentanfang, `false` = Segmentende. |
|  |  | `Items[]` | `List<Entry>` | Liste pro Querschnitts-Lane (siehe unten). |
|  |  | `Items[].SourceLaneId` | `uint` | Lane-ID der Ausgangsspur am Segmentende. |
|  |  | `Items[].TargetLaneIds` | `List<uint>` | Lane-IDs der Ziele (koennen andere Segmente am selben Knoten sein). |
| `LaneConnectionsAppliedCommand` | Server -> Alle | `NodeId` | `ushort` | Knoten-ID der Kreuzung. |
|  |  | `SegmentId` | `ushort` | Segment-ID am Knoten (Ziel-Segmentende). |
|  |  | `StartNode` | `bool` | Wie oben. |
|  |  | `Items[]` | `List<Entry>` | Effektiv angewandte Zuordnungen (SourceLaneId -> TargetLaneIds). |
| `RequestRejected` | Server -> Client | `EntityType` | `byte` | 3 = Node, 2 = Segment, 1 = Lane (kontextabhaengig). |
|  |  | `EntityId` | `int` | Betroffene Entitaet. |
|  |  | `Reason` | `string` | Grund, z.B. `entity_missing`, `tmpe_apply_failed`. |

Hinweise
- Uebertraegt echte `laneId`-Werte; falls eine Lane lokal fehlt, dienen Segment-/LaneIndex-Fallback und Retry-Logik dazu, sobald TM:PE die Spur wieder bereitstellt.
- Der Apply-Koordinator merged konkurrierende Requests pro Segmentende, fuehrt Retry/Backoff (5/15/30/60/120/240 Frames) aus und prueft LaneConnectionManager + Connection-DB vor jedem Apply.
- `LaneConnectorTmpeAdapter` kapselt TM:PE-Aufrufe mit Ignore-Scopes; fehlen waehrend des Applys einzelne Lanes, korrigiert der autoritative Host-Broadcast den Zustand.

---

## EN - English

### 1) Summary
- Synchronises TM:PE lane connections between host and clients during CSM sessions (segment end -> target lanes).
- Transmits the actual TM:PE lane IDs (source and targets) per `(NodeId, SegmentId, StartNode)`; stable segment/lane indices are only used locally as fallback.
- Resilient apply pipeline: an apply coordinator batches per segment end, checks the LaneConnectionManager and connection database, applies retry/backoff, and suppresses local Harmony echoes.

### 2) Folder layout
- `src/CSM.TmpeSync.LaneConnector/`
  - `LaneConnectorSyncFeature.cs` - feature bootstrap (enables the listener).
  - `Handlers/` - CSM command handlers for server/client (with retry integration).
  - `Messages/` - network commands (ProtoBuf contracts) for requests/broadcasts.
  - `Services/` - Harmony listener, end selector, apply coordinator, TM:PE adapter.

### 3) File overview
| File | Purpose |
| --- | --- |
| `LaneConnectorSyncFeature.cs` | Registers/enables the feature and TM:PE listener. |
| `Handlers/LaneConnectionsAppliedCommandHandler.cs` | Client: consumes host broadcasts and runs the apply coordinator (retry + merge). |
| `Handlers/LaneConnectionsUpdateRequestHandler.cs` | Server: validates requests, applies via coordinator, broadcasts on success. |
| `Messages/LaneConnectionsAppliedCommand.cs` | Server -> all: applied state for a segment end (lane IDs + target lane IDs). |
| `Messages/LaneConnectionsUpdateRequest.cs` | Client -> server: desired state for a segment end (lane IDs + target lane IDs). |
| `Services/LaneConnectorEventListener.cs` | Harmony hooks on TM:PE; uses TM:PE-provided `sourceStartNode`, snapshots segment ends. |
| `Services/LaneConnectorSynchronization.cs` | Shared transport, retry/backoff coordinator, merge logic for competing requests. |
| `Services/LaneConnectorTmpeAdapter.cs` | TM:PE facade (ignore scope, translation to lane IDs). |
| `Services/LaneConnectionAdapter.cs` | Low-level adapter for LaneConnectionManager (read/apply via reflection). |
| `Services/LaneConnectorEndSelector.cs` | Selects TM:PE-compatible candidate lanes per segment end in a stable order. |

### 4) Workflow (host and client)
- **Host edits lane connections in TM:PE**
  - Harmony postfix receives `sourceStartNode`, resolves `(nodeId, segmentId)`, and snapshots all eligible lanes (filtered via LaneTypes/VehicleTypes + `GetLaneEndPoint`).
  - Synchronisation broadcasts `LaneConnectionsAppliedCommand`; clients apply via the coordinator (retry until the manager/database is ready).

- **Client edits lane connections in TM:PE**
  - Harmony postfix creates the same per-segment-end snapshot and sends `LaneConnectionsUpdateRequest` to the server (debug log; ignore-scope prevents echo).
  - Server validates the node/segment and queues the request in the coordinator.
  - Coordinator operates directly on lane IDs, checks the LaneConnectionManager + connection DB, and tries to apply. Up to six attempts with increasing frame delay (5/15/30/60/120/240); concurrent requests are merged.
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
|  |  | `Items[].SourceLaneId` | `uint` | TM:PE lane ID of the source lane at the segment end. |
|  |  | `Items[].TargetLaneIds` | `List<uint>` | TM:PE lane IDs of the targets (may belong to other segments at the same node). |
| `LaneConnectionsAppliedCommand` | Server -> all | `NodeId` | `ushort` | Node identifier. |
|  |  | `SegmentId` | `ushort` | Segment identifier at the node (segment end). |
|  |  | `StartNode` | `bool` | As above. |
|  |  | `Items[]` | `List<Entry>` | Applied mappings (SourceLaneId -> TargetLaneIds). |
| `RequestRejected` | Server -> client | `EntityType` | `byte` | 3 = node, 2 = segment, 1 = lane (context dependent). |
|  |  | `EntityId` | `int` | Affected entity. |
|  |  | `Reason` | `string` | Reason, e.g. `entity_missing`, `tmpe_apply_failed`. |

Notes
- Actual `laneId` values are transmitted; if a lane is missing locally the segment/lane index acts as fallback and the coordinator retries until TM:PE exposes the lane again.
- The apply coordinator merges competing requests per segment end, runs retry/backoff (5/15/30/60/120/240 frames), and re-checks the LaneConnectionManager + connection DB before each attempt.
- `LaneConnectorTmpeAdapter` wraps TM:PE calls with ignore scopes; if a lane vanishes during apply, the authoritative host broadcast restores the state once TM:PE is ready.
