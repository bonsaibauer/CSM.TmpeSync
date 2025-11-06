# CSM TM:PE Sync - Lane Arrows / Fahrspurpfeile

This document is bilingual. German (DE) first, English (EN) follows.

---

## DE - Deutsch

### 1) Kurzbeschreibung
- Synchronisiert TM:PE-Fahrspurpfeile an Segmentenden zwischen Host und Clients in CSM-Sitzungen.
- Nutzt ein Ordinal-Schema pro Segmentende `(NodeId, SegmentId, StartNode)` ohne `laneId`/`laneIndex`; jede Spur erhält eine 3-Bit-Maske (Links/Geradeaus/Rechts).
- Apply-Koordinator mit Retry sorgt für robuste Anwendung: prüft TM:PE-Optionen, LaneArrowManager-Verfügbarkeit und segmentiert Requests pro Segmentende.

### 2) Dateistruktur
- `src/CSM.TmpeSync.LaneArrows/`
  - `LaneArrowsSyncFeature.cs` – Feature-Bootstrap (aktiviert Listener).
  - `Handlers/` – CSM-Command-Handler (Server/Client, Retry-fähig).
  - `Messages/` – Netzwerkbefehle (ProtoBuf-Verträge) für Lane-Arrows.
  - `Services/` – Harmony-Listener, End-Selektor, Apply-Koordinator, TM:PE-Adapter.

### 3) Dateiübersicht
| Datei | Zweck |
| --- | --- |
| `LaneArrowsSyncFeature.cs` | Registriert/aktiviert das Feature und den TM:PE-Listener. |
| `Handlers/LaneArrowsAppliedCommandHandler.cs` | Client: verarbeitet Host-Broadcasts und delegiert an den Apply-Koordinator (mit Retry/Backoff). |
| `Handlers/LaneArrowsUpdateRequestHandler.cs` | Server: validiert Requests, nutzt den Apply-Koordinator und broadcastet nach erfolgreicher Anwendung. |
| `Messages/LaneArrowsAppliedCommand.cs` | Server → Alle: Zustand eines Segmentendes mit `Ordinal` und `LaneArrowFlags`. |
| `Messages/LaneArrowsUpdateRequest.cs` | Client → Server: Änderungswunsch für ein Segmentende (Ordinals + Flags). |
| `Services/LaneArrowEventListener.cs` | Harmony-Hooks auf TM:PE, erkennt lokale Änderungen und erstellt Segmentend-Snapshots. |
| `Services/LaneArrowSynchronization.cs` | Gemeinsamer Versandweg, Apply-Koordinator mit Retry/Backoff, Merge-Logik für konkurrierende Requests. |
| `Services/LaneArrowTmpeAdapter.cs` | Apply-/Read-Fassade (Ignore-Scope, LaneId-Auflösung). |
| `Services/LaneArrowAdapter.cs` | Low-Level-Zugriff auf TM:PE LaneArrowManager (Lesen/Setzen). |
| `Services/LaneArrowEndSelector.cs` | Ermittelt Kandidaten-Lanes am Segmentende in stabiler Reihenfolge (Ordinalbildung). |

### 4) Workflow (Server/Host und Client)
- **Host ändert Pfeile in TM:PE**
  - Harmony-Postfix identifiziert `(nodeId, segmentId, startNode)` und liest alle Spur-Ordinals + Pfeilmasken.
  - Synchronisation broadcastet `LaneArrowsAppliedCommand`; Clients wenden den Zustand über den Apply-Koordinator an (Retry bis TM:PE-Optionen/Manager bereit sind).

- **Client ändert Pfeile in TM:PE**
  - Harmony-Postfix erstellt denselben Snapshot und sendet `LaneArrowsUpdateRequest` an den Server.
  - Server validiert Knoten/Segment, übergibt den Request an den Apply-Koordinator.
  - Apply-Koordinator prüft TM:PE-Options-Kontext (`SavedGameOptions`), LaneArrowManager-Verfügbarkeit und Kandidaten-Ermittlung. Fehlen Manager, Kandidaten oder tritt `NullReferenceException` auf, werden bis zu sechs Versuche mit wachsendem Frame-Delay (5 → 240) gestartet; konkurrierende Requests werden zusammengeführt.
  - Nach Erfolg broadcastet der Server `LaneArrowsAppliedCommand`.

- **Abgelehnte Requests (Server)**
  - Bei fehlenden Entitäten oder endgültigen TM:PE-Fehlern sendet der Server `RequestRejected` (Grund + Entität).

### 5) Datenaustausch (Nachrichten/Felder)
| Nachricht | Richtung | Feld | Typ | Beschreibung |
| --- | --- | --- | --- | --- |
| `LaneArrowsUpdateRequest` | Client → Server | `NodeId` | `ushort` | Knoten-ID der Kreuzung. |
|  |  | `SegmentId` | `ushort` | Segment-ID am Knoten (betroffenes Segmentende). |
|  |  | `StartNode` | `bool` | `true` = Segmentanfang, `false` = Segmentende. |
|  |  | `Items[]` | `List<Entry>` | Spur-Einträge (Ordinal + Pfeile). |
|  |  | `Items[].Ordinal` | `int` | Ordinal (0..n-1) der Spur am Segmentende. |
|  |  | `Items[].Arrows` | `LaneArrowFlags` | Bitmaske: Left=1, Forward=2, Right=4 (kombinierbar). |
| `LaneArrowsAppliedCommand` | Server → Alle | `NodeId` | `ushort` | Knoten-ID der Kreuzung. |
|  |  | `SegmentId` | `ushort` | Segment-ID am Knoten (Ziel-Segmentende). |
|  |  | `StartNode` | `bool` | Wie oben. |
|  |  | `Items[]` | `List<Entry>` | Effektiv angewandte Spurwerte (Ordinal + Pfeilmaske). |
| `RequestRejected` | Server → Client | `EntityType` | `byte` | 3 = Node, 2 = Segment, 1 = Lane (kontextabhängig). |
|  |  | `EntityId` | `uint` | Betroffene Entität. |
|  |  | `Reason` | `string` | Grund, z. B. `entity_missing`, `tmpe_apply_failed`. |

Hinweise
- Keine Übertragung von `laneId`/`laneIndex`; Ordinals werden pro Segmentende anhand `NetInfo.Lane.m_position` bestimmt.
- Apply-Koordinator verwaltet Requests pro Segmentende, führt Backoff-Retry durch (5/15/30/60/120/240 Frames) und prüft TM:PE-Kontext (Options, Manager, Kandidaten).
- `LaneArrowTmpeAdapter` nutzt Ignore-Scope, damit eigene Applies keine erneuten Events erzeugen; fehlende Lanes werden übersprungen und durch den Host-Broadcast korrigiert.

---

## EN - English

### 1) Summary
- Synchronises TM:PE lane arrows at segment ends between host and clients in CSM sessions.
- Uses a per-end ordinal scheme `(NodeId, SegmentId, StartNode)` without transmitting `laneId`/`laneIndex`; each lane carries a 3-bit arrow mask (Left/Forward/Right).
- Apply coordinator with retry ensures robustness: checks TM:PE options, LaneArrowManager readiness, and batches requests per segment end.

### 2) Directory Layout
- `src/CSM.TmpeSync.LaneArrows/`
  - `LaneArrowsSyncFeature.cs` – Feature bootstrap (enables listener).
  - `Handlers/` – CSM command handlers (server/client, retry-aware).
  - `Messages/` – Network commands (ProtoBuf contracts) for lane arrows.
  - `Services/` – Harmony listener, end selector, apply coordinator, TM:PE adapter.

### 3) File Overview
| File | Purpose |
| --- | --- |
| `LaneArrowsSyncFeature.cs` | Registers/enables the feature and the TM:PE listener. |
| `Handlers/LaneArrowsAppliedCommandHandler.cs` | Client: handles host broadcasts and delegates to the retry/backoff-aware apply coordinator. |
| `Handlers/LaneArrowsUpdateRequestHandler.cs` | Server: validates requests, runs them through the apply coordinator, and broadcasts upon success. |
| `Messages/LaneArrowsAppliedCommand.cs` | Server → All: applied state per segment end (ordinal + arrow mask). |
| `Messages/LaneArrowsUpdateRequest.cs` | Client → Server: desired state per segment end (ordinal + arrow mask). |
| `Services/LaneArrowEventListener.cs` | Harmony hooks capturing local changes and building end snapshots. |
| `Services/LaneArrowSynchronization.cs` | Common dispatch path, apply coordinator with retry/backoff, merge logic for concurrent requests. |
| `Services/LaneArrowTmpeAdapter.cs` | Apply/read façade with ignore scope and laneId resolution. |
| `Services/LaneArrowAdapter.cs` | Low-level adapter to TM:PE’s LaneArrowManager (read/apply). |
| `Services/LaneArrowEndSelector.cs` | Enumerates candidate lanes at the segment end in stable order (ordinal mapping). |

### 4) Workflow (Server/Host and Client)
- **Host edits lane arrows in TM:PE**
  - Harmony postfix detects the change, determines `(nodeId, segmentId, startNode)`, and gathers all lane ordinals plus arrow flags.
  - Synchronisation broadcasts `LaneArrowsAppliedCommand`; clients apply it via the retry-capable coordinator until TM:PE options/manager are ready.

- **Client edits lane arrows in TM:PE**
  - Harmony postfix builds the same snapshot and sends `LaneArrowsUpdateRequest` to the server.
  - The server validates node/segment and forwards the request to the apply coordinator.
  - Apply coordinator checks TM:PE options (`SavedGameOptions`), ensures the LaneArrowManager and candidate list exist, maps ordinals → lane IDs, and applies arrow flags. `NullReferenceException` or missing prerequisites trigger retries (5 → 240 frames, up to six attempts); concurrent requests merge.
  - After success the server broadcasts `LaneArrowsAppliedCommand`.

- **Rejections (Server)**
  - Missing entities or unrecoverable TM:PE failures result in `RequestRejected` (reason + entity info).

### 5) Data Exchange (messages/fields)
| Message | Direction | Field | Type | Description |
| --- | --- | --- | --- | --- |
| `LaneArrowsUpdateRequest` | Client → Server | `NodeId` | `ushort` | Node ID of the junction. |
|  |  | `SegmentId` | `ushort` | Segment ID at the node (segment end). |
|  |  | `StartNode` | `bool` | `true` = segment start, `false` = segment end. |
|  |  | `Items[]` | `List<Entry>` | Lane entries (ordinal + arrow mask). |
|  |  | `Items[].Ordinal` | `int` | Ordinal (0..n-1) for the lane at this end. |
|  |  | `Items[].Arrows` | `LaneArrowFlags` | Bitmask: Left=1, Forward=2, Right=4 (combinable). |
| `LaneArrowsAppliedCommand` | Server → All | `NodeId` | `ushort` | Node ID of the junction. |
|  |  | `SegmentId` | `ushort` | Segment ID at the node (target end). |
|  |  | `StartNode` | `bool` | Same semantics as above. |
|  |  | `Items[]` | `List<Entry>` | Applied lane entries (ordinal + arrow mask). |
| `RequestRejected` | Server → Client | `EntityType` | `byte` | 3 = node, 2 = segment, 1 = lane (context-dependent). |
|  |  | `EntityId` | `uint` | Affected entity. |
|  |  | `Reason` | `string` | Reason, e.g. `entity_missing`, `tmpe_apply_failed`. |

Notes
- No `laneId`/`laneIndex` is transmitted; ordinals are derived per segment end from `NetInfo.Lane.m_position`.
- The apply coordinator batches concurrent requests per segment end, performs retry/backoff (5/15/30/60/120/240 frames), and validates TM:PE options/manager/candidates before applying.
- `LaneArrowTmpeAdapter` wraps TM:PE calls with ignore scopes to prevent feedback loops; missing lanes are skipped and corrected by the authoritative broadcast.
