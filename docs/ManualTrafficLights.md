# CSM TM:PE Sync - Manual Traffic Lights / Manuelle Ampeln

This document is bilingual. German (DE) first, English (EN) follows.

---

## DE - Deutsch

### 1) Kurzbeschreibung
- Synchronisiert TM:PE Manual Traffic Lights pro Knoten zwischen Host und Clients in CSM-Sitzungen.
- Verwendet ein Node-Snapshot-Modell: pro betroffenem Knoten wird der komplette manuelle Ampelzustand uebertragen.
- Host ist autoritativ: Clients senden Aenderungswuensche, der Host wendet den Zustand in TM:PE an und broadcastet den effektiv angewandten Snapshot.
- Event-getriebener Versand: lokale TM:PE-Events werden knotenweise gebuendelt und als Simulation-Flush verarbeitet.
- Retry-/Backoff-Apply sorgt fuer robuste Anwendung bei temporaer nicht bereiten TM:PE-Komponenten.

### 2) Dateistruktur
- `src/CSM.TmpeSync.ManualTrafficLights/`
  - `ManualTrafficLightsSyncFeature.cs` - Feature-Bootstrap (aktiviert Listener).
  - `Handlers/` - CSM-Command-Handler (Server/Client-Verarbeitung).
  - `Messages/` - Netzwerkbefehle (ProtoBuf-Vertraege) fuer Node-Snapshots.
  - `Services/` - Harmony-Listener, Synchronisationslogik, TM:PE-Adapter, Apply-Koordinator, State-Cache.

### 3) Dateiuebersicht
| Datei | Zweck |
| --- | --- |
| `ManualTrafficLightsSyncFeature.cs` | Registriert/aktiviert das Feature und den TM:PE-Listener. |
| `Handlers/ManualTrafficLightsUpdateRequestHandler.cs` | Server: validiert Requests, fuehrt Apply unter Node-Lock aus und triggert Host-Broadcast. |
| `Handlers/ManualTrafficLightsAppliedCommandHandler.cs` | Client: verarbeitet Host-Broadcasts und wendet den Snapshot lokal an; `OnClientConnect` triggert Host-Resync. |
| `Messages/ManualTrafficLightsUpdateRequest.cs` | Client -> Server: Aenderungswunsch (`NodeId`, `State`). |
| `Messages/ManualTrafficLightsAppliedCommand.cs` | Server -> Alle: final angewandter Snapshot (`NodeId`, `State`). |
| `Messages/ManualTrafficLightsNodeState.cs` | Wire-State fuer manuelle Ampeln: Node, Segmente, Fussgaengerzustand, Vehicle-Lights. |
| `Services/ManualTrafficLightsEventListener.cs` | Harmony-Hooks auf relevante TM:PE-Methoden; sammelt lokale Aenderungen pro Knoten und flush't gebuendelt. |
| `Services/ManualTrafficLightsSynchronization.cs` | Gemeinsamer Versandweg, Host/Client-Dispatch, Retry/Backoff-Apply, Reconnect-Resync. |
| `Services/ManualTrafficLightsTmpeAdapter.cs` | Lesen/Anwenden des Snapshot-Zustands in TM:PE (SetUp/Remove Manual-Simulation, Segment-/Vehicle-Lights). |
| `Services/ManualTrafficLightsStateCache.cs` | Host-Cache fuer zuletzt angewandte Snapshots inkl. Hashing, Clone und Prune. |

### 4) Workflow (Server/Host und Client)
- **Host aendert manuelle Ampeln in TM:PE**
  - Harmony-Postfix erkennt den betroffenen Knoten (z. B. `ChangeMainLight`, `SetLightMode`, `set_CurrentMode`).
  - Listener queued den Knoten und fuehrt genau einen Flush auf der Simulation aus.
  - Der Host liest den kompletten Node-Snapshot und broadcastet `ManualTrafficLightsAppliedCommand`.

- **Client aendert manuelle Ampeln in TM:PE**
  - Harmony-Postfix queued den betroffenen Knoten und erzeugt beim Flush einen Node-Snapshot.
  - Client sendet `ManualTrafficLightsUpdateRequest` an den Host.
  - Host validiert den Knoten, lockt ihn via `EntityLocks.AcquireNode`, wendet den Snapshot an und broadcastet danach den effektiv angewandten Zustand.

- **Apply-Verhalten**
  - Apply laeuft ueber einen Koordinator mit bis zu 6 Versuchen und Frame-Backoff `5/15/30/60/120/240`.
  - Bei temporaeren TM:PE-Problemen (z. B. uninitialisierte Manager, `NullReference`) wird automatisch erneut versucht.
  - Bei finalem Fehler wird server-/clientseitig geloggt.

- **Reconnect-Resync**
  - Beim Client-Reconnect sendet der Host alle gueltigen gecachten `ManualTrafficLightsAppliedCommand`-Eintraege.
  - Vor dem Versand wird der Cache per `Prune` auf gueltige Knoten bereinigt.

- **Ablehnungen**
  - Dieses Feature verwendet keinen dedizierten `RequestRejected`-Kanal; fehlerhafte Requests werden serverseitig verworfen und geloggt.

### 5) Datenaustausch (Nachrichten/Felder)
| Nachricht | Richtung | Feld | Typ | Beschreibung |
| --- | --- | --- | --- | --- |
| `ManualTrafficLightsUpdateRequest` | Client -> Server | `NodeId` | `ushort` | Knoten-ID der manuellen Ampel. |
|  |  | `State` | `ManualTrafficLightsNodeState` | Gewuenschter kompletter Node-Snapshot. |
| `ManualTrafficLightsAppliedCommand` | Server -> Alle | `NodeId` | `ushort` | Knoten-ID der manuellen Ampel. |
|  |  | `State` | `ManualTrafficLightsNodeState` | Effektiv angewandter kompletter Node-Snapshot. |
| `ManualTrafficLightsNodeState` | (payload) | `NodeId` | `ushort` | Zielknoten. |
|  |  | `IsManualEnabled` | `bool` | Manual-Simulation aktiv/inaktiv. |
|  |  | `Segments[]` | `List<SegmentState>` | Segmentende-Snapshots am Knoten. |
| `SegmentState` | (payload) | `SegmentId` | `ushort` | Betroffenes Segment. |
|  |  | `StartNode` | `bool` | `true` = Startknoten-Ende, `false` = Endknoten-Ende. |
|  |  | `ManualPedestrianMode` | `bool` | Manueller Fussgaenger-Modus aktiv/inaktiv. |
|  |  | `HasPedestrianLightState` | `bool` | Kennzeichnet expliziten Fussgaenger-Lichtzustand. |
|  |  | `PedestrianLightState` | `int` | Serialisierter TM:PE-Lichtzustand fuer Fussgaenger. |
|  |  | `VehicleLights[]` | `List<VehicleLightState>` | Fahrzeug-Lichtzustaende pro VehicleType. |
| `VehicleLightState` | (payload) | `VehicleType` | `int` | Serialisierter TM:PE-VehicleType. |
|  |  | `LightMode` | `int` | Serialisierter TM:PE-LightMode. |
|  |  | `MainLightState` | `int` | Hauptsignal-Zustand. |
|  |  | `LeftLightState` | `int` | Links-Signal-Zustand. |
|  |  | `RightLightState` | `int` | Rechts-Signal-Zustand. |

Hinweise
- Enum-Werte werden als `int` uebertragen, um API-Drift zwischen TM:PE-Versionen robust zu behandeln.
- Client-Dispatch verwendet pro Knoten Hash-Dedupe, damit identische Snapshots nicht erneut gesendet werden.
- Host-Cache verwendet Hash-Dedupe fuer Broadcast/Resync und haelt nur den letzten effektiven Snapshot je Knoten.
- `NetworkUtil.IsSynchronizationReady()` gate't Queue/Flush waehrend der Ladephase.
- Ignore- und Local-Apply-Scopes verhindern Echo-Schleifen zwischen lokalem Apply und Harmony-Listener.

---

## EN - English

### 1) Summary
- Synchronizes TM:PE manual traffic lights per node between host and clients in CSM sessions.
- Uses a node snapshot model: the full manual light state is transferred for each affected node.
- Host is authoritative: clients send update requests, the host applies in TM:PE, then broadcasts the effective snapshot.
- Event-driven dispatch: local TM:PE events are queued per node and processed in a single simulation flush.
- Retry/backoff apply keeps synchronization stable when TM:PE components are temporarily unavailable.

### 2) Directory Layout
- `src/CSM.TmpeSync.ManualTrafficLights/`
  - `ManualTrafficLightsSyncFeature.cs` - feature bootstrap (enables listener).
  - `Handlers/` - CSM command handlers (server/client processing).
  - `Messages/` - network commands (ProtoBuf contracts) for node snapshots.
  - `Services/` - Harmony listener, synchronization logic, TM:PE adapter, apply coordinator, state cache.

### 3) File Overview
| File | Purpose |
| --- | --- |
| `ManualTrafficLightsSyncFeature.cs` | Registers/enables the feature and TM:PE listener. |
| `Handlers/ManualTrafficLightsUpdateRequestHandler.cs` | Server: validates requests, applies under node lock, and triggers host broadcast. |
| `Handlers/ManualTrafficLightsAppliedCommandHandler.cs` | Client: processes host broadcasts and applies snapshots locally; `OnClientConnect` triggers host resync. |
| `Messages/ManualTrafficLightsUpdateRequest.cs` | Client -> Server: change request (`NodeId`, `State`). |
| `Messages/ManualTrafficLightsAppliedCommand.cs` | Server -> All: final applied snapshot (`NodeId`, `State`). |
| `Messages/ManualTrafficLightsNodeState.cs` | Wire state for manual lights: node, segments, pedestrian state, vehicle lights. |
| `Services/ManualTrafficLightsEventListener.cs` | Harmony hooks on relevant TM:PE methods; queues local changes per node and flushes in batch. |
| `Services/ManualTrafficLightsSynchronization.cs` | Common dispatch path, host/client routing, retry/backoff apply, reconnect resync. |
| `Services/ManualTrafficLightsTmpeAdapter.cs` | Reads/applies snapshot state in TM:PE (set up/remove manual simulation, segment/vehicle lights). |
| `Services/ManualTrafficLightsStateCache.cs` | Host cache for latest applied snapshots with hashing, cloning, and prune support. |

### 4) Workflow (Server/Host and Client)
- **Host edits manual lights in TM:PE**
  - Harmony postfix detects the affected node (for example `ChangeMainLight`, `SetLightMode`, `set_CurrentMode`).
  - Listener queues the node and executes exactly one simulation flush.
  - Host reads the full node snapshot and broadcasts `ManualTrafficLightsAppliedCommand`.

- **Client edits manual lights in TM:PE**
  - Harmony postfix queues the affected node and builds a node snapshot during flush.
  - Client sends `ManualTrafficLightsUpdateRequest` to the host.
  - Host validates the node, acquires `EntityLocks.AcquireNode`, applies the snapshot, and then broadcasts the effective state.

- **Apply behavior**
  - Apply runs through a coordinator with up to 6 attempts and frame backoff `5/15/30/60/120/240`.
  - Temporary TM:PE failures (for example uninitialized managers, `NullReference`) automatically trigger retries.
  - Final failures are logged on host/client side.

- **Reconnect resync**
  - On reconnect, the host sends all valid cached `ManualTrafficLightsAppliedCommand` entries to the client.
  - The cache is pruned against current node validity before sending.

- **Rejections**
  - This feature does not use a dedicated `RequestRejected` channel; invalid requests are dropped and logged on the server.

### 5) Data Exchange (messages/fields)
| Message | Direction | Field | Type | Description |
| --- | --- | --- | --- | --- |
| `ManualTrafficLightsUpdateRequest` | Client -> Server | `NodeId` | `ushort` | Node ID of the manual traffic light. |
|  |  | `State` | `ManualTrafficLightsNodeState` | Desired full node snapshot. |
| `ManualTrafficLightsAppliedCommand` | Server -> All | `NodeId` | `ushort` | Node ID of the manual traffic light. |
|  |  | `State` | `ManualTrafficLightsNodeState` | Effective full node snapshot after host apply. |
| `ManualTrafficLightsNodeState` | (payload) | `NodeId` | `ushort` | Target node. |
|  |  | `IsManualEnabled` | `bool` | Manual simulation enabled/disabled. |
|  |  | `Segments[]` | `List<SegmentState>` | Segment-end snapshots at the node. |
| `SegmentState` | (payload) | `SegmentId` | `ushort` | Target segment. |
|  |  | `StartNode` | `bool` | `true` = start-node end, `false` = end-node end. |
|  |  | `ManualPedestrianMode` | `bool` | Manual pedestrian mode enabled/disabled. |
|  |  | `HasPedestrianLightState` | `bool` | Indicates explicit pedestrian light state presence. |
|  |  | `PedestrianLightState` | `int` | Serialized TM:PE pedestrian light state. |
|  |  | `VehicleLights[]` | `List<VehicleLightState>` | Vehicle light states per vehicle type. |
| `VehicleLightState` | (payload) | `VehicleType` | `int` | Serialized TM:PE vehicle type. |
|  |  | `LightMode` | `int` | Serialized TM:PE light mode. |
|  |  | `MainLightState` | `int` | Main light state. |
|  |  | `LeftLightState` | `int` | Left light state. |
|  |  | `RightLightState` | `int` | Right light state. |

Notes
- Enum values are transferred as `int` to keep compatibility resilient across TM:PE enum drift.
- Client dispatch uses per-node hash deduplication to suppress identical snapshots.
- Host cache uses hash deduplication for broadcast/resync and stores only the latest effective snapshot per node.
- `NetworkUtil.IsSynchronizationReady()` gates queue/flush during loading.
- Ignore scopes and local-apply scopes prevent feedback loops between local apply and Harmony listeners.
