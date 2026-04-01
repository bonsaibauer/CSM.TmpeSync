# CSM TM:PE Sync - Manual Traffic Lights / Manuelle Ampeln

This document is bilingual. German (DE) first, English (EN) follows.

---

## DE - Deutsch

### 1) Kurzbeschreibung
- Synchronisiert TM:PE Manual-Traffic-Lights (manuelle Ampeln) pro Knoten zwischen Host und Clients in CSM-Sitzungen.
- Verwendet einen Node-Snapshot-Ansatz: nach einer lokalen Aenderung wird der komplette manuelle Lichtzustand des Knotens (bis zu 8 Segmentenden inkl. Vehicle-Lights) uebertragen.
- Host bleibt autoritativ: Clients senden Update-Requests, der Host wendet den Zustand in TM:PE an und broadcastet den effektiv angewandten Snapshot.
- Enthält Retry/Backoff beim Anwenden (5/15/30/60/120/240 Frames) fuer temporäre TM:PE-Abhaengigkeiten.

### 2) Dateistruktur
- `src/CSM.TmpeSync.ManualTrafficLights/`
  - `ManualTrafficLightsSyncFeature.cs` - Feature-Bootstrap (aktiviert Listener).
  - `Handlers/` - CSM-Command-Handler (Server/Client-Verarbeitung).
  - `Messages/` - Netzwerkbefehle (ProtoBuf-Vertraege) fuer Node-Snapshots.
  - `Services/` - Harmony-Listener, Synchronisationslogik, TM:PE-Adapter und State-Cache.

### 3) Dateiuebersicht
| Datei | Zweck |
| --- | --- |
| `ManualTrafficLightsSyncFeature.cs` | Registriert/aktiviert das Feature und den TM:PE-Listener. |
| `Handlers/ManualTrafficLightsUpdateRequestHandler.cs` | Server: validiert Requests, wendet den Snapshot an und triggert Host-Broadcast. |
| `Handlers/ManualTrafficLightsAppliedCommandHandler.cs` | Client: verarbeitet Host-Broadcasts und wendet den Snapshot lokal an; `OnClientConnect` startet Host-Resync. |
| `Messages/ManualTrafficLightsUpdateRequest.cs` | Client -> Server: Aenderungswunsch (`NodeId`, `State`). |
| `Messages/ManualTrafficLightsAppliedCommand.cs` | Server -> Alle: final angewandter Snapshot (`NodeId`, `State`). |
| `Messages/ManualTrafficLightsNodeState.cs` | Wire-State fuer manuelle Ampeln: Node, Segmente, Pedestrian-Zustand, Vehicle-Lights. |
| `Services/ManualTrafficLightsEventListener.cs` | Harmony-Hooks auf relevante TM:PE-Methoden; erkennt lokale Aenderungen und broadcastet knotenweise. |
| `Services/ManualTrafficLightsSynchronization.cs` | Gemeinsamer Versandweg, Host/Client-Dispatch, Retry/Backoff-Apply, Reconnect-Resync. |
| `Services/ManualTrafficLightsTmpeAdapter.cs` | Lesen/Anwenden des Snapshot-Zustands in TM:PE (Setup/Remove Manual-Simulation, Segment- und Vehicle-Lights). |
| `Services/ManualTrafficLightsStateCache.cs` | Cache letzter Host-Snapshots inkl. Hashing, Clone, Remove/Prune fuer robusten Resync. |

### 4) Workflow (Server/Host und Client)
- **Host aendert manuelle Ampel in TM:PE**
  - Harmony-Postfix erkennt die Aenderung (z. B. `ChangeMainLight`, `set_CurrentMode`, `set_PedestrianLightState`, `SetLightMode`).
  - Listener liest den kompletten Node-Snapshot und sendet `ManualTrafficLightsAppliedCommand` an alle.
  - Clients wenden den Snapshot lokal an (Ignore-Scope + Local-Apply-Scope verhindern Echo-Schleifen).

- **Client aendert manuelle Ampel in TM:PE**
  - Harmony-Postfix liest den kompletten Node-Snapshot und sendet `ManualTrafficLightsUpdateRequest` an den Server.
  - Server validiert den Knoten, wendet den Snapshot in TM:PE unter Node-Lock an und startet bei Bedarf Retry/Backoff.
  - Nach erfolgreichem Apply broadcastet der Server den effektiv angewandten Snapshot als `ManualTrafficLightsAppliedCommand`.

- **Reconnect-Resync**
  - Beim Client-Reconnect sendet der Host alle gecachten Snapshot-Kommandos fuer manuelle Ampeln.
  - Vor dem Versand werden invalide Knoten aus dem Cache entfernt (`Prune` + Node-Filter), damit nur gueltige Eintraege gesendet werden.

- **Ablehnungen/Fehlerbehandlung**
  - Fehlende Entitaeten oder Apply-Fehler werden derzeit serverseitig geloggt und nicht als separates `RequestRejected` fuer dieses Feature versendet.
  - Bei temporaeren TM:PE-Fehlern (`NullReference`, nicht initialisierte Manager) nutzt der Apply-Koordinator Retry mit Frame-Backoff.

### 5) Datenaustausch (Nachrichten/Felder)
| Nachricht | Richtung | Feld | Typ | Beschreibung |
| --- | --- | --- | --- | --- |
| `ManualTrafficLightsUpdateRequest` | Client -> Server | `NodeId` | `ushort` | Knoten-ID der manuellen Ampel. |
|  |  | `State` | `ManualTrafficLightsNodeState` | Gewuenschter kompletter Manual-TL-Snapshot fuer den Knoten. |
| `ManualTrafficLightsAppliedCommand` | Server -> Alle | `NodeId` | `ushort` | Knoten-ID der manuellen Ampel. |
|  |  | `State` | `ManualTrafficLightsNodeState` | Effektiv angewandter kompletter Snapshot. |
| `ManualTrafficLightsNodeState` | (payload) | `NodeId` | `ushort` | Zielknoten. |
|  |  | `IsManualEnabled` | `bool` | Manual-Simulation aktiv/inaktiv. |
|  |  | `Segments[]` | `List<SegmentState>` | Segmentende-Snapshots am Knoten. |
| `SegmentState` | (payload) | `SegmentId` | `ushort` | Betroffenes Segment. |
|  |  | `StartNode` | `bool` | `true` = Startknoten-Ende, `false` = Endknoten-Ende. |
|  |  | `ManualPedestrianMode` | `bool` | Manueller Fussgaenger-Modus aktiv. |
|  |  | `HasPedestrianLightState` | `bool` | Kennzeichnet, ob ein expliziter Fussgaenger-Lichtzustand vorhanden ist. |
|  |  | `PedestrianLightState` | `int` | Serialisierter TM:PE-Lichtzustand fuer Fussgaenger. |
|  |  | `VehicleLights[]` | `List<VehicleLightState>` | Fahrzeug-Lichtzustaende je VehicleType. |
| `VehicleLightState` | (payload) | `VehicleType` | `int` | Serialisierter TM:PE-VehicleType. |
|  |  | `LightMode` | `int` | Serialisierter TM:PE-LightMode. |
|  |  | `MainLightState` | `int` | Hauptsignal-Zustand. |
|  |  | `LeftLightState` | `int` | Links-Signal-Zustand. |
|  |  | `RightLightState` | `int` | Rechts-Signal-Zustand. |

Hinweise
- Enum-Werte werden als `int` uebertragen, um API-Drift bei TM:PE-Enums robuster zu handhaben.
- Clientseitig wird ein Dispatch-Hash pro Node verwendet, um identische Snapshots nicht wiederholt zu senden.
- Event-Abdeckung umfasst sowohl UI-nahe Methoden (`Change*`, `ToggleMode`) als auch direkte Setter-/Manager-Pfade (`set_CurrentMode`, `set_PedestrianLightState`, `SetLightMode`, `ApplyLightModes`).

---

## EN - English

### 1) Summary
- Synchronizes TM:PE manual traffic lights per node between host and clients in CSM sessions.
- Uses a node snapshot model: after a local change, the full manual light state for that node (up to 8 segment ends including vehicle lights) is transferred.
- Host remains authoritative: clients send update requests, host applies in TM:PE and broadcasts the effective snapshot.
- Includes retry/backoff for apply operations (5/15/30/60/120/240 frames) when TM:PE dependencies are temporarily unavailable.

### 2) Directory Layout
- `src/CSM.TmpeSync.ManualTrafficLights/`
  - `ManualTrafficLightsSyncFeature.cs` - feature bootstrap (enables listener).
  - `Handlers/` - CSM command handlers (server/client processing).
  - `Messages/` - network commands (ProtoBuf contracts) for node snapshots.
  - `Services/` - Harmony listener, synchronization logic, TM:PE adapter, and state cache.

### 3) File Overview
| File | Purpose |
| --- | --- |
| `ManualTrafficLightsSyncFeature.cs` | Registers/enables the feature and TM:PE listener. |
| `Handlers/ManualTrafficLightsUpdateRequestHandler.cs` | Server: validates requests, applies snapshot, then triggers host broadcast. |
| `Handlers/ManualTrafficLightsAppliedCommandHandler.cs` | Client: processes host broadcasts and applies snapshot locally; `OnClientConnect` triggers host resync. |
| `Messages/ManualTrafficLightsUpdateRequest.cs` | Client -> Server: change request (`NodeId`, `State`). |
| `Messages/ManualTrafficLightsAppliedCommand.cs` | Server -> All: final applied snapshot (`NodeId`, `State`). |
| `Messages/ManualTrafficLightsNodeState.cs` | Wire state for manual lights: node, segments, pedestrian state, vehicle lights. |
| `Services/ManualTrafficLightsEventListener.cs` | Harmony hooks on relevant TM:PE methods; captures local changes and broadcasts by node. |
| `Services/ManualTrafficLightsSynchronization.cs` | Common dispatch path, host/client routing, retry/backoff apply, reconnect resync. |
| `Services/ManualTrafficLightsTmpeAdapter.cs` | Reads/applies snapshot state in TM:PE (setup/remove manual simulation, segment and vehicle lights). |
| `Services/ManualTrafficLightsStateCache.cs` | Cache of last host snapshots including hashing, cloning, remove/prune for robust resync. |

### 4) Workflow (Server/Host and Client)
- **Host edits manual lights in TM:PE**
  - Harmony postfix detects the change (for example `ChangeMainLight`, `set_CurrentMode`, `set_PedestrianLightState`, `SetLightMode`).
  - Listener reads the full node snapshot and sends `ManualTrafficLightsAppliedCommand` to all.
  - Clients apply locally (ignore scope + local apply scope prevent echo loops).

- **Client edits manual lights in TM:PE**
  - Harmony postfix reads the full node snapshot and sends `ManualTrafficLightsUpdateRequest` to the server.
  - Server validates node existence, applies under node lock, and retries with backoff if needed.
  - After successful apply, server broadcasts effective snapshot via `ManualTrafficLightsAppliedCommand`.

- **Reconnect resync**
  - On reconnect, host sends all cached manual-traffic-light snapshots.
  - Before sending, invalid nodes are removed from cache (`Prune` + node filter), so only valid entries are sent.

- **Rejection/error handling**
  - Missing entities or apply failures are currently logged on the server and not emitted as a dedicated `RequestRejected` flow for this feature.
  - Temporary TM:PE failures (`NullReference`, uninitialized managers) are handled through retry/backoff.

### 5) Data Exchange (messages/fields)
| Message | Direction | Field | Type | Description |
| --- | --- | --- | --- | --- |
| `ManualTrafficLightsUpdateRequest` | Client -> Server | `NodeId` | `ushort` | Node ID of the manual traffic light. |
|  |  | `State` | `ManualTrafficLightsNodeState` | Desired full manual-TL snapshot for the node. |
| `ManualTrafficLightsAppliedCommand` | Server -> All | `NodeId` | `ushort` | Node ID of the manual traffic light. |
|  |  | `State` | `ManualTrafficLightsNodeState` | Effective full snapshot after host apply. |
| `ManualTrafficLightsNodeState` | (payload) | `NodeId` | `ushort` | Target node. |
|  |  | `IsManualEnabled` | `bool` | Manual simulation enabled/disabled. |
|  |  | `Segments[]` | `List<SegmentState>` | Segment-end snapshots at the node. |
| `SegmentState` | (payload) | `SegmentId` | `ushort` | Target segment. |
|  |  | `StartNode` | `bool` | `true` = start-node end, `false` = end-node end. |
|  |  | `ManualPedestrianMode` | `bool` | Manual pedestrian mode enabled. |
|  |  | `HasPedestrianLightState` | `bool` | Indicates whether an explicit pedestrian light state is present. |
|  |  | `PedestrianLightState` | `int` | Serialized TM:PE pedestrian light state. |
|  |  | `VehicleLights[]` | `List<VehicleLightState>` | Vehicle light states per vehicle type. |
| `VehicleLightState` | (payload) | `VehicleType` | `int` | Serialized TM:PE vehicle type. |
|  |  | `LightMode` | `int` | Serialized TM:PE light mode. |
|  |  | `MainLightState` | `int` | Main light state. |
|  |  | `LeftLightState` | `int` | Left light state. |
|  |  | `RightLightState` | `int` | Right light state. |

Notes
- Enum values are transferred as `int` to reduce coupling to TM:PE enum shape changes.
- Client dispatch uses per-node snapshot hashes to suppress duplicate sends.
- Event coverage includes both UI-like methods (`Change*`, `ToggleMode`) and direct setter/manager paths (`set_CurrentMode`, `set_PedestrianLightState`, `SetLightMode`, `ApplyLightModes`).
