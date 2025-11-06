# CSM TM:PE Sync – Priority Signs / Vorfahrtsschilder

This document is bilingual. German (DE) first, English (EN) follows.

---

## DE – Deutsch

### 1) Kurzbeschreibung
- Synchronisiert TM:PE‑Vorfahrtsschilder (Yield, Stop, Priority/Main) zwischen Host und Clients in CSM‑Sitzungen.
- Fängt lokale Änderungen in TM:PE per Harmony ab und überträgt sie via CSM.
- Berücksichtigt TM:PE‑Automatik am Knoten: nach einer Änderung wird der gesamte Knoten‑Zustand übertragen, damit alle automatisch gesetzten Schilder gespiegelt werden.

### 2) Dateistruktur
- `src/CSM.TmpeSync.PrioritySigns/`
  - `PrioritySignSyncFeature.cs` – Feature‑Bootstrap (aktiviert Listener).
  - `Handlers/` – CSM‑Command‑Handler (Server/Client Verarbeitungslogik).
  - `Messages/` – Netzwerkbefehle (ProtoBuf‑Verträge).
  - `Services/` – Brücke zu TM:PE, Harmony‑Listener, Synchronisations‑Helfer.

### 3) Dateiübersicht
| Datei | Zweck |
| --- | --- |
| `PrioritySignSyncFeature.cs` | Registriert/aktiviert das Feature und den TM:PE‑Listener. |
| `Handlers/PrioritySignAppliedCommandHandler.cs` | Client: verarbeitet Server‑Broadcast „Applied“ und setzt das Schild lokal in TM:PE. |
| `Handlers/PrioritySignUpdateRequestHandler.cs` | Server: validiert Request, setzt Schild(e) in TM:PE und broadcastet den gesamten Knoten‑Zustand. |
| `Messages/PrioritySignAppliedCommand.cs` | Server → Alle: endgültiger Zustand je (NodeId, SegmentId, SignType). |
| `Messages/PrioritySignUpdateRequest.cs` | Client → Server: Änderungswunsch (NodeId, SegmentId, SignType). |
| `Services/PrioritySignEventListener.cs` | Harmony‑Hooks auf TM:PE, leitet lokale Änderungen in CSM‑Befehle um; broadcastet knotenweit. |
| `Services/PrioritySignSynchronization.cs` | Gemeinsamer Versandweg (Server→Alle oder Client→Server) und Lese/Anwenden‑Fassade. |
| `Services/PrioritySignTmpeAdapter.cs` | Adapter zu TM:PE API/Implementierung: Lesen/Schreiben der Vorfahrtsschilder (inkl. Reflection‑Fallback).

### 4) Workflow (Server/Host und Client)
- Host ändert Schild in TM:PE
  - Harmony‑Postfix erkennt Änderung und bestimmt `nodeId` (Kreuzung).
  - Listener liest alle Segmentenden am Knoten und sendet für jedes Ende `PrioritySignApplied` an alle.
  - Clients wenden Werte lokal an (Ignore‑Scope verhindert Schleifen).

- Client ändert Schild in TM:PE
  - Harmony‑Postfix sendet `PrioritySignUpdateRequest` an den Server.
  - Server validiert (Node/Segment vorhanden) und setzt das Schild in TM:PE.
  - Server liest danach den gesamten Knoten‑Zustand und broadcastet pro Segmentende `PrioritySignApplied` an alle.
  - Alle wenden Werte lokal an (Client eingeschlossen; Ignore‑Scope verhindert Re‑Trigger).

- Abgelehnte Requests (Server)
  - Bei fehlenden Entitäten oder TM:PE‑Fehlern sendet der Server `RequestRejected` zurück (Grund + Entität).

### 5) Datenaustausch (Nachrichten/Felder)
| Nachricht | Richtung | Feld | Typ | Beschreibung |
| --- | --- | --- | --- | --- |
| `PrioritySignUpdateRequest` | Client → Server | `NodeId` | `ushort` | Knoten‑ID der Kreuzung. |
|  |  | `SegmentId` | `ushort` | Segment‑ID am Knoten (betroffenes Segmentende). |
|  |  | `SignType` | `PrioritySignType` | Gewünschtes Schild (None, Yield, Stop, Priority). |
| `PrioritySignAppliedCommand` | Server → Alle | `NodeId` | `ushort` | Knoten‑ID der Kreuzung. |
|  |  | `SegmentId` | `ushort` | Segment‑ID am Knoten (Ziel‑Segmentende). |
|  |  | `SignType` | `PrioritySignType` | Effektiv angewandtes Schild nach TM:PE (inkl. Automatiken). |
| `RequestRejected` | Server → Client | `EntityType` | `byte` | Entitätstyp: 3=Node, 2=Segment, 1=Lane (kontextabhängig). |
|  |  | `EntityId` | `int` | Betroffene Entität. |
|  |  | `Reason` | `string` | Grund, z. B. `entity_missing`, `tmpe_apply_failed`. |

Hinweise
- `PrioritySignType` ↔ TM:PE `PriorityType` (None, Yield, Stop, Priority/Main).
- Knotenweite Spiegelung liest bis zu 8 Segmentenden aus dem `NetManager`.
- Lokale Applies nutzen Ignore‑Scope, damit Listener nicht erneut feuert.

---

## EN – English

### 1) Summary
- Synchronizes TM:PE priority signs (Yield, Stop, Priority/Main) between host and clients in CSM sessions.
- Hooks local TM:PE changes with Harmony and relays them via CSM.
- Accounts for TM:PE’s node‑level automation: after any change, the full node state is broadcast so all auto‑updated signs are mirrored.

### 2) Directory Layout
- `src/CSM.TmpeSync.PrioritySigns/`
  - `PrioritySignSyncFeature.cs` – Feature bootstrap (enables listener).
  - `Handlers/` – CSM command handlers (server/client processing).
  - `Messages/` – Network commands (ProtoBuf contracts).
  - `Services/` – TM:PE bridge, Harmony listener, sync helpers.

### 3) File Overview
| File | Purpose |
| --- | --- |
| `PrioritySignSyncFeature.cs` | Registers/enables the feature and the TM:PE listener. |
| `Handlers/PrioritySignAppliedCommandHandler.cs` | Client: handles server “Applied” broadcast and applies locally in TM:PE. |
| `Handlers/PrioritySignUpdateRequestHandler.cs` | Server: validates request, applies in TM:PE, and broadcasts the whole node state. |
| `Messages/PrioritySignAppliedCommand.cs` | Server → All: final state per (NodeId, SegmentId, SignType). |
| `Messages/PrioritySignUpdateRequest.cs` | Client → Server: change request (NodeId, SegmentId, SignType). |
| `Services/PrioritySignEventListener.cs` | Harmony hooks into TM:PE, translates local changes into CSM commands; broadcasts per node. |
| `Services/PrioritySignSynchronization.cs` | Common dispatch path (Server→All or Client→Server) and read/apply facade. |
| `Services/PrioritySignTmpeAdapter.cs` | Adapter to TM:PE API/implementation: read/write priority signs (with reflection fallback).

### 4) Workflow (Server/Host and Client)
- Host edits sign in TM:PE
  - Harmony postfix detects the change and determines `nodeId` (junction).
  - Listener reads all segment ends at the node and sends `PrioritySignApplied` for each end to all.
  - Clients apply locally (ignore scope prevents feedback loops).

- Client edits sign in TM:PE
  - Harmony postfix sends a `PrioritySignUpdateRequest` to the server.
  - Server validates (node/segment exist) and applies in TM:PE.
  - Server then reads the full node state and broadcasts `PrioritySignApplied` for each segment end to all.
  - Everyone (including the originating client) applies locally; ignore scope prevents re‑triggering.

- Rejections (Server)
  - If entities are missing or TM:PE fails to apply, the server returns `RequestRejected` (with reason and entity info).

### 5) Data Exchange (messages/fields)
| Message | Direction | Field | Type | Description |
| --- | --- | --- | --- | --- |
| `PrioritySignUpdateRequest` | Client → Server | `NodeId` | `ushort` | Node ID of the junction. |
|  |  | `SegmentId` | `ushort` | Segment ID at the node (the affected segment end). |
|  |  | `SignType` | `PrioritySignType` | Desired sign (None, Yield, Stop, Priority). |
| `PrioritySignAppliedCommand` | Server → All | `NodeId` | `ushort` | Node ID of the junction. |
|  |  | `SegmentId` | `ushort` | Segment ID at the node (the target segment end). |
|  |  | `SignType` | `PrioritySignType` | Effective sign after TM:PE (may reflect automation). |
| `RequestRejected` | Server → Client | `EntityType` | `byte` | Entity type: 3=Node, 2=Segment, 1=Lane (context‑dependent). |
|  |  | `EntityId` | `int` | Affected entity. |
|  |  | `Reason` | `string` | Reason, e.g., `entity_missing`, `tmpe_apply_failed`. |

Notes
- `PrioritySignType` maps to TM:PE’s `PriorityType` (None, Yield, Stop, Priority/Main).
- Node‑wide mirroring reads up to 8 segment ends from `NetManager`.
- Local applies use an ignore scope so listener hooks do not fire again.
