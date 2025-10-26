# CSM TM:PE Sync - Lane Arrows / Fahrspurpfeile

This document is bilingual. German (DE) first, English (EN) follows.

---

## DE - Deutsch

### 1) Kurzbeschreibung
- Synchronisiert TM:PE-Fahrspurpfeile an Kreuzungen zwischen Host und Clients in CSM-Sitzungen.
- Unterstützt pro Fahrspur eine eigene 3‑Bit‑Maske (Links/Gradaus/Rechts) und damit unterschiedliche Kombinationen je Spur.
- Vermeidet LaneId/LaneIndex im Netzwerk: adressiert Segment-Enden über `(NodeId, SegmentId, StartNode)` und ordnet Spuren lokal deterministisch (Ordinal).

### 2) Dateistruktur
- `src/CSM.TmpeSync.LaneArrows/`
  - `LaneArrowsSyncFeature.cs` – Feature-Bootstrap (aktiviert Listener).
  - `Handlers/` – CSM-Command-Handler (Server/Client Verarbeitungslogik).
  - `Messages/` – Netzwerkbefehle (ProtoBuf‑Verträge).
  - `Services/` – TM:PE‑Adapter, Harmony‑Listener, Sync‑Helfer und Spur‑Selektor.

### 3) Dateiübersicht
| Datei | Zweck |
| --- | --- |
| `LaneArrowsSyncFeature.cs` | Registriert/aktiviert das Feature und den TM:PE‑Listener. |
| `Handlers/LaneArrowsAppliedCommandHandler.cs` | Client: verarbeitet Server‑Broadcast „Applied“ (kompletter Segment‑End‑Zustand) und setzt je Spur lokal in TM:PE. |
| `Handlers/LaneArrowsUpdateRequestHandler.cs` | Server: validiert Request, setzt je Spur in TM:PE und broadcastet den kompletten Segment‑End‑Zustand. |
| `Messages/LaneArrowsAppliedCommand.cs` | Server → Alle: finale per‑Spur‑Zustände je `(NodeId, SegmentId, StartNode)` inkl. Ordinals. |
| `Messages/LaneArrowsUpdateRequest.cs` | Client → Server: Änderungswunsch für ein Segment‑Ende mit per‑Spur‑Einträgen. |
| `Services/LaneArrowEventListener.cs` | Harmony‑Hook auf TM:PE; erkennt lokale Pfeil‑Änderungen, liest kompletten End‑Zustand und leitet ihn als CSM‑Befehl weiter. |
| `Services/LaneArrowSynchronization.cs` | Gemeinsamer Versandweg (Server→Alle oder Client→Server) und Fassade. |
| `Services/LaneArrowTmpeAdapter.cs` | Anwenden/Lesen am Segment‑Ende: verteilt/holt Pfeile je Spur, ohne LaneIds zu senden. |
| `Services/LaneArrowEndSelector.cs` | Ermittelt die zum Segment‑Ende gehörigen Spuren und ordnet sie stabil über Ordinal (Sortierung nach Querprofil‑Position). |
| `Services/LaneArrowAdapter.cs` | Low‑Level‑Adapter zu TM:PE (LaneArrows lesen/setzen pro Lane). |

### 4) Workflow (Server/Host und Client)
- Host ändert Pfeile in TM:PE
  - Harmony‑Postfix erkennt Änderung und bestimmt `(nodeId, segmentId, startNode)`.
  - Listener liest alle betroffenen Spuren am Segment‑Ende, bildet Ordinals und broadcastet `LaneArrowsApplied` mit kompletter Liste an alle.
  - Clients wenden die Werte je Spur lokal an (Ignore‑Scope verhindert Schleifen).

- Client ändert Pfeile in TM:PE
  - Harmony‑Postfix sendet `LaneArrowsUpdateRequest` an den Server (mit kompletter per‑Spur‑Liste für dieses Segment‑Ende).
  - Server validiert (Node/Segment vorhanden), setzt je Spur in TM:PE.
  - Server liest danach den End‑Zustand erneut und broadcastet `LaneArrowsApplied` an alle.
  - Alle wenden Werte lokal an (Client eingeschlossen; Ignore‑Scope verhindert Re‑Trigger).

- Abgelehnte Requests (Server)
  - Bei fehlenden Entitäten oder TM:PE‑Fehlern sendet der Server `RequestRejected` zurück (Grund + Entität).

### 5) Datenaustausch (Nachrichten/Felder)
| Nachricht | Richtung | Feld | Typ | Beschreibung |
| --- | --- | --- | --- | --- |
| `LaneArrowsUpdateRequest` | Client → Server | `NodeId` | `ushort` | Knoten‑ID der Kreuzung. |
|  |  | `SegmentId` | `ushort` | Segment‑ID am Knoten (betroffenes Segment‑Ende). |
|  |  | `StartNode` | `bool` | `true` = Startknoten des Segments, sonst Endknoten. |
|  |  | `Items[]` | `List<Entry>` | Per‑Spur‑Einträge für dieses Segment‑Ende. |
|  |  | `Items[i].Ordinal` | `int` | Spur‑Ordinal am Segment‑Ende (stabile Reihenfolge nach `laneInfo.m_position`). |
|  |  | `Items[i].Arrows` | `LaneArrowFlags` | 3‑Bit‑Maske: Left=1, Forward=2, Right=4 (kombinierbar). |
| `LaneArrowsAppliedCommand` | Server → Alle | `NodeId` | `ushort` | Knoten‑ID der Kreuzung. |
|  |  | `SegmentId` | `ushort` | Segment‑ID am Knoten (Ziel‑Segment‑Ende). |
|  |  | `StartNode` | `bool` | Ziel‑Segment‑Ende wie oben. |
|  |  | `Items[]` | `List<Entry>` | Effektiv angewandte per‑Spur‑Einträge (Ordinal + Maske). |
| `RequestRejected` | Server → Client | `EntityType` | `byte` | Entitätstyp: 3=Node, 2=Segment, 1=Lane. |
|  |  | `EntityId` | `uint` | Betroffene Entität. |
|  |  | `Reason` | `string` | Grund, z. B. `entity_missing`, `tmpe_apply_failed`. |

Hinweise
- LaneIds/LaneIndex werden nicht übertragen; die Ordinals werden lokal aus dem aktuellen Segmentzustand berechnet.
- `LaneArrowFlags` ist bitmaskiert und erlaubt Kombinationen wie L+S, S+R, L+S+R.

---

## EN - English

### 1) Summary
- Synchronizes TM:PE lane arrows at junctions between host and clients in CSM sessions.
- Supports a per‑lane 3‑bit mask (Left/Forward/Right), allowing different combinations across lanes.
- Avoids LaneId/LaneIndex on the wire: addresses a segment end via `(NodeId, SegmentId, StartNode)` and maps lanes locally using a stable ordinal.

### 2) Directory Layout
- `src/CSM.TmpeSync.LaneArrows/`
  - `LaneArrowsSyncFeature.cs` – Feature bootstrap (enables listener).
  - `Handlers/` – CSM command handlers (server/client processing).
  - `Messages/` – Network commands (ProtoBuf contracts).
  - `Services/` – TM:PE adapter, Harmony listener, sync helpers and lane‑end selector.

### 3) File Overview
| File | Purpose |
| --- | --- |
| `LaneArrowsSyncFeature.cs` | Registers/enables the feature and the TM:PE listener. |
| `Handlers/LaneArrowsAppliedCommandHandler.cs` | Client: handles server “Applied” broadcast (full segment‑end state) and applies per lane in TM:PE. |
| `Handlers/LaneArrowsUpdateRequestHandler.cs` | Server: validates request, applies per lane in TM:PE, then broadcasts the full segment‑end state. |
| `Messages/LaneArrowsAppliedCommand.cs` | Server → All: final per‑lane state for `(NodeId, SegmentId, StartNode)` including ordinals. |
| `Messages/LaneArrowsUpdateRequest.cs` | Client → Server: change request for one segment end with per‑lane entries. |
| `Services/LaneArrowEventListener.cs` | Harmony hook into TM:PE; detects local arrow changes, reads full end state and relays as a CSM command. |
| `Services/LaneArrowSynchronization.cs` | Common dispatch (Server→All or Client→Server) and facade. |
| `Services/LaneArrowTmpeAdapter.cs` | Apply/read at a segment end: distributes/collects per‑lane arrows without sending LaneIds. |
| `Services/LaneArrowEndSelector.cs` | Determines the lanes that belong to the segment end and orders them via a stable ordinal (sorted by cross‑section position). |
| `Services/LaneArrowAdapter.cs` | Low‑level adapter to TM:PE (read/set lane arrows per lane). |

### 4) Workflow (Server/Host and Client)
- Host edits arrows in TM:PE
  - Harmony postfix detects the change and determines `(nodeId, segmentId, startNode)`.
  - The listener reads all lanes at that segment end, builds ordinals, and broadcasts `LaneArrowsApplied` with the full list to all.
  - Clients apply the per‑lane values locally (ignore scope prevents loops).

- Client edits arrows in TM:PE
  - Harmony postfix sends `LaneArrowsUpdateRequest` to the server (with the full per‑lane list for that segment end).
  - Server validates (node/segment exist) and applies per lane in TM:PE.
  - Server then reads the end state again and broadcasts `LaneArrowsApplied` to all.
  - Everyone applies locally; ignore scope prevents re‑triggering.

- Rejections (Server)
  - If entities are missing or TM:PE fails to apply, the server returns `RequestRejected` (with reason and entity info).

### 5) Data Exchange (messages/fields)
| Message | Direction | Field | Type | Description |
| --- | --- | --- | --- | --- |
| `LaneArrowsUpdateRequest` | Client → Server | `NodeId` | `ushort` | Node ID of the junction. |
|  |  | `SegmentId` | `ushort` | Segment ID at the node (affected segment end). |
|  |  | `StartNode` | `bool` | `true` = start node of the segment, otherwise end node. |
|  |  | `Items[]` | `List<Entry>` | Per‑lane entries for this segment end. |
|  |  | `Items[i].Ordinal` | `int` | Lane ordinal at the segment end (stable order by `laneInfo.m_position`). |
|  |  | `Items[i].Arrows` | `LaneArrowFlags` | 3‑bit mask: Left=1, Forward=2, Right=4 (combinable). |
| `LaneArrowsAppliedCommand` | Server → All | `NodeId` | `ushort` | Node ID of the junction. |
|  |  | `SegmentId` | `ushort` | Segment ID at the node (target segment end). |
|  |  | `StartNode` | `bool` | Target segment end as above. |
|  |  | `Items[]` | `List<Entry>` | Effective per‑lane entries (ordinal + mask). |
| `RequestRejected` | Server → Client | `EntityType` | `byte` | Entity type: 3=Node, 2=Segment, 1=Lane. |
|  |  | `EntityId` | `uint` | Affected entity. |
|  |  | `Reason` | `string` | Reason, e.g., `entity_missing`, `tmpe_apply_failed`. |

Notes
- LaneIds/LaneIndex are not transmitted; ordinals are derived locally from the current segment state.
- `LaneArrowFlags` is a bitmask and allows combinations such as L+F, F+R, L+F+R.

