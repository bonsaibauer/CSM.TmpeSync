# CSM TM:PE Sync - Timed Traffic Lights / Zeitgesteuerte Ampeln

This document is bilingual. German (DE) first, English (EN) follows.

---

## DE - Deutsch

### 1) Kurzbeschreibung
- Synchronisiert TM:PE Timed Traffic Lights (TTL) zwischen Host und Clients in CSM-Sitzungen.
- Verwendet zwei Streams: Definition (TTL-Struktur) und Runtime (laufender Zustand je Master-Knoten).
- Host ist autoritativ: Clients senden Requests, Host wendet in TM:PE an und broadcastet den effektiv angewandten Zustand.
- Event-getriebener Versand: lokale TM:PE-Events werden gesammelt und pro Frame als gebuendelter Simulation-Flush verarbeitet.
- Master-node-basierter Resync stellt Definition und Runtime bei Abweichungen gezielt wieder her.

### 2) Dateistruktur
- `src/CSM.TmpeSync.TimedTrafficLights/`
  - `TimedTrafficLightsSyncFeature.cs` - Feature-Bootstrap (aktiviert Listener).
  - `Handlers/` - CSM-Command-Handler (Server/Client-Verarbeitung inkl. Resync/Reject).
  - `Messages/` - Netzwerkbefehle (ProtoBuf-Vertraege) fuer Definition, Runtime und Resync.
  - `Services/` - Harmony-Listener, TM:PE-Adapter, Synchronisationslogik, Definition-/Runtime-Caches.

### 3) Dateiuebersicht
| Datei | Zweck |
| --- | --- |
| `TimedTrafficLightsSyncFeature.cs` | Registriert/aktiviert das Feature und den TM:PE-Listener. |
| `Handlers/TimedTrafficLightsDefinitionUpdateRequestHandler.cs` | Server: verarbeitet Definition-Requests von Clients. |
| `Handlers/TimedTrafficLightsDefinitionAppliedCommandHandler.cs` | Client: verarbeitet Host-Definitionen; `OnClientConnect` triggert Host-Resync. |
| `Handlers/TimedTrafficLightsRuntimeUpdateRequestHandler.cs` | Server: verarbeitet Runtime-Requests von Clients. |
| `Handlers/TimedTrafficLightsRuntimeAppliedCommandHandler.cs` | Client: verarbeitet Runtime-Broadcasts vom Host. |
| `Handlers/TimedTrafficLightsResyncRequestHandler.cs` | Server: verarbeitet gezielte Resync-Anfragen pro Master-Knoten. |
| `Handlers/TimedTrafficLightsRequestRejectedHandler.cs` | Client: verarbeitet hostseitige Ablehnungen (`RequestRejected`) fuer Timed-TTL. |
| `Messages/TimedTrafficLightsDefinitionUpdateRequest.cs` | Client -> Server: Aenderungswunsch fuer Definition (`MasterNodeId`, `Removed`, `Definition`). |
| `Messages/TimedTrafficLightsDefinitionAppliedCommand.cs` | Server -> Alle: final angewandte Definition oder Entfernen-Markierung. |
| `Messages/TimedTrafficLightsRuntimeUpdateRequest.cs` | Client -> Server: Aenderungswunsch fuer Runtime (`Runtime`). |
| `Messages/TimedTrafficLightsRuntimeAppliedCommand.cs` | Server -> Alle: finaler Runtime-Status (`Runtime`). |
| `Messages/TimedTrafficLightsResyncRequest.cs` | Client -> Server: gezielter Resync-Request pro `MasterNodeId`. |
| `Messages/TimedTrafficLightsDefinitionState.cs` | Wire-State der TTL-Definition (NodeGroup, Nodes, Steps, Segment-/Vehicle-Lights). |
| `Messages/TimedTrafficLightsRuntimeState.cs` | Wire-State der Runtime (`MasterNodeId`, `IsRunning`, `CurrentStep`, `Epoch`). |
| `Services/TimedTrafficLightsEventListener.cs` | Harmony-Hooks auf relevante TM:PE-TTL-Mutationen (Definition und Runtime). |
| `Services/TimedTrafficLightsSynchronization.cs` | Event-Queue, Flush, Dispatch, Host-Apply, Delta-Ermittlung, Resync/Reject-Flow. |
| `Services/TimedTrafficLightsTmpeAdapter.cs` | Lesen/Anwenden von TTL-Definition/Runtime in TM:PE (inkl. Local-Apply-Scope). |
| `Services/TimedTrafficLightsStateCache.cs` | Cache fuer Definitionen je Master-Knoten inkl. Hash und Node->Master-Mapping. |
| `Services/TimedTrafficLightsRuntimeCache.cs` | Runtime-Cache fuer Broadcast- und Received-Zustaende je Master-Knoten. |

### 4) Workflow (Server/Host und Client)
- **Lokale Aenderung (Host oder Client)**
  - Harmony-Postfix erkennt TTL-Mutationen (`AddStep`, `RemoveStep`, `MoveStep`, `ChangeLightMode`, `Start`, `Stop`, `SkipStep`, `SetTestMode`, `SetUpTimedTrafficLight`, `RemoveNodeFromSimulation`).
  - Aenderungen werden als Definition- oder Runtime-Event je Master-Knoten gequeued.
  - Ein Simulation-Flush verarbeitet alle ausstehenden Events gebuendelt.

- **Definition-Flow**
  - Client sendet bei echtem Delta `TimedTrafficLightsDefinitionUpdateRequest` (oder `Removed=true`) an den Host.
  - Host validiert, lockt relevante Knoten, wendet Definition/Removal an und broadcastet `TimedTrafficLightsDefinitionAppliedCommand`.
  - Client wendet den Host-Stand lokal an; bei Apply-Problemen wird ein masterbezogener Resync angefordert.

- **Runtime-Flow**
  - Client sendet `TimedTrafficLightsRuntimeUpdateRequest`, wenn die lokale Runtime vom letzten Host-Stand abweicht.
  - Host wendet Runtime unter Node-Locks an und broadcastet `TimedTrafficLightsRuntimeAppliedCommand`.
  - Client uebernimmt den Host-Stand; bei relevanter Divergenz wird Resync angefordert.

- **Resync/Reject**
  - Resync laeuft pro Master-Knoten ueber `TimedTrafficLightsResyncRequest`.
  - Host antwortet gezielt mit aktueller Definition und Runtime fuer den angefragten Master.
  - Ablehnungen laufen ueber `RequestRejected` mit `EntityType=3` und `Reason`-Prefix `timed_tl:`.
  - Resync-Requests sind clientseitig per Frame-Cooldown begrenzt.

- **Reconnect-Resync**
  - Beim Client-Reconnect sendet der Host alle bekannten Definitionsstaende und zusaetzlich aktuelle Runtime-Zustaende je Master-Knoten.

- **Readiness-Verhalten**
  - Queue/Flush laeuft nur bei bereiter Synchronisation (`NetworkUtil.IsSynchronizationReady()`), damit keine fruehen Ladephasen-Events dispatcht werden.

### 5) Datenaustausch (Nachrichten/Felder)
| Nachricht | Richtung | Feld | Typ | Beschreibung |
| --- | --- | --- | --- | --- |
| `TimedTrafficLightsDefinitionUpdateRequest` | Client -> Server | `MasterNodeId` | `ushort` | Master-Knoten der TTL-Gruppe. |
|  |  | `Removed` | `bool` | `true` = TTL entfernen, `false` = Definition anwenden. |
|  |  | `Definition` | `TimedTrafficLightsDefinitionState` | Vollstaendige Script-Struktur (bei `Removed=false`). |
| `TimedTrafficLightsDefinitionAppliedCommand` | Server -> Alle | `MasterNodeId` | `ushort` | Autoritativer Master-Knoten. |
|  |  | `Removed` | `bool` | Entfernen-Flag. |
|  |  | `Definition` | `TimedTrafficLightsDefinitionState` | Effektiv angewandte Definition (oder `null` bei Remove). |
| `TimedTrafficLightsRuntimeUpdateRequest` | Client -> Server | `Runtime` | `TimedTrafficLightsRuntimeState` | Gewuenschter Runtime-Status fuer einen Master-Knoten. |
| `TimedTrafficLightsRuntimeAppliedCommand` | Server -> Alle | `Runtime` | `TimedTrafficLightsRuntimeState` | Autoritativ angewandter Runtime-Status. |
| `TimedTrafficLightsResyncRequest` | Client -> Server | `MasterNodeId` | `ushort` | Ziel-Master fuer gezielten Resync. |
| `TimedTrafficLightsDefinitionState` | (payload) | `MasterNodeId` | `ushort` | Master-Knoten der TTL-Gruppe. |
|  |  | `NodeGroup` | `List<ushort>` | Alle Knoten der Gruppe. |
|  |  | `Nodes[]` | `List<NodeState>` | Knotenweise Step-Definitionen. |
|  |  | `Nodes[].Steps[]` | `List<StepState>` | Step-Parameter + Segment-/Vehicle-Lights. |
| `TimedTrafficLightsRuntimeState` | (payload) | `MasterNodeId` | `ushort` | Master-Knoten der Runtime. |
|  |  | `IsRunning` | `bool` | Laufstatus. |
|  |  | `CurrentStep` | `int` | Aktueller Step-Index. |
|  |  | `Epoch` | `uint` | Frame-basierter Zeitanker (Host). |
| `RequestRejected` | Server -> Client | `EntityType` | `byte` | 3 = Node (Master-Knoten). |
|  |  | `EntityId` | `int` | Betroffener Master-Knoten. |
|  |  | `Reason` | `string` | Ablehnungsgrund (Timed-spezifisch getaggt). |

Hinweise
- Definition und Runtime sind getrennt und werden jeweils als Delta pro Master-Knoten dispatcht.
- Definitionen werden im Cache gehasht; unveraenderte Zustaende werden nicht erneut gesendet.
- Runtime nutzt Broadcast-/Received-Caches, damit nur echte Laufzeit-Abweichungen uebertragen werden.
- Node-Locks sowie Ignore-/Local-Apply-Scopes verhindern Echo-Schleifen und race-bedingte Inkonsistenzen.

---

## EN - English

### 1) Summary
- Synchronizes TM:PE timed traffic lights (TTL) between host and clients in CSM sessions.
- Uses two streams: definition (TTL structure) and runtime (running state per master node).
- Host is authoritative: clients send requests, host applies in TM:PE, and broadcasts the effective state.
- Event-driven operation: local TM:PE events are queued and flushed once per frame.
- Master-node-based resync restores definition/runtime state when divergence is detected.

### 2) Directory Layout
- `src/CSM.TmpeSync.TimedTrafficLights/`
  - `TimedTrafficLightsSyncFeature.cs` - feature bootstrap (enables listener).
  - `Handlers/` - CSM command handlers (server/client processing including resync/reject).
  - `Messages/` - network commands (ProtoBuf contracts) for definition, runtime, resync.
  - `Services/` - Harmony listener, TM:PE adapter, synchronization logic, definition/runtime caches.

### 3) File Overview
| File | Purpose |
| --- | --- |
| `TimedTrafficLightsSyncFeature.cs` | Registers/enables the feature and TM:PE listener. |
| `Handlers/TimedTrafficLightsDefinitionUpdateRequestHandler.cs` | Server: processes client definition requests. |
| `Handlers/TimedTrafficLightsDefinitionAppliedCommandHandler.cs` | Client: processes host definitions; `OnClientConnect` triggers host resync. |
| `Handlers/TimedTrafficLightsRuntimeUpdateRequestHandler.cs` | Server: processes client runtime requests. |
| `Handlers/TimedTrafficLightsRuntimeAppliedCommandHandler.cs` | Client: processes host runtime broadcasts. |
| `Handlers/TimedTrafficLightsResyncRequestHandler.cs` | Server: handles targeted resync requests per master node. |
| `Handlers/TimedTrafficLightsRequestRejectedHandler.cs` | Client: handles host-side `RequestRejected` for timed TTL. |
| `Messages/TimedTrafficLightsDefinitionUpdateRequest.cs` | Client -> Server: definition change request (`MasterNodeId`, `Removed`, `Definition`). |
| `Messages/TimedTrafficLightsDefinitionAppliedCommand.cs` | Server -> All: final applied definition or removal marker. |
| `Messages/TimedTrafficLightsRuntimeUpdateRequest.cs` | Client -> Server: runtime change request (`Runtime`). |
| `Messages/TimedTrafficLightsRuntimeAppliedCommand.cs` | Server -> All: final applied runtime (`Runtime`). |
| `Messages/TimedTrafficLightsResyncRequest.cs` | Client -> Server: targeted resync request by `MasterNodeId`. |
| `Messages/TimedTrafficLightsDefinitionState.cs` | Wire state for TTL definition (node group, nodes, steps, segment/vehicle lights). |
| `Messages/TimedTrafficLightsRuntimeState.cs` | Wire state for runtime (`MasterNodeId`, `IsRunning`, `CurrentStep`, `Epoch`). |
| `Services/TimedTrafficLightsEventListener.cs` | Harmony hooks on relevant TM:PE TTL mutations (definition and runtime). |
| `Services/TimedTrafficLightsSynchronization.cs` | Event queue, flush, dispatch, host apply, delta evaluation, resync/reject flow. |
| `Services/TimedTrafficLightsTmpeAdapter.cs` | Reads/applies TTL definition/runtime in TM:PE (including local-apply scopes). |
| `Services/TimedTrafficLightsStateCache.cs` | Per-master definition cache with hash and node-to-master mapping. |
| `Services/TimedTrafficLightsRuntimeCache.cs` | Per-master runtime cache for broadcast and received states. |

### 4) Workflow (Server/Host and Client)
- **Local change (host or client)**
  - Harmony postfix detects TTL mutations (`AddStep`, `RemoveStep`, `MoveStep`, `ChangeLightMode`, `Start`, `Stop`, `SkipStep`, `SetTestMode`, `SetUpTimedTrafficLight`, `RemoveNodeFromSimulation`).
  - Changes are queued as definition or runtime events per master node.
  - A single simulation flush processes all queued changes in batch.

- **Definition flow**
  - Client sends `TimedTrafficLightsDefinitionUpdateRequest` (or `Removed=true`) when a real definition delta exists.
  - Host validates payload, locks relevant nodes, applies definition/removal, then broadcasts `TimedTrafficLightsDefinitionAppliedCommand`.
  - Client applies host definition locally; failures trigger targeted master resync.

- **Runtime flow**
  - Client sends `TimedTrafficLightsRuntimeUpdateRequest` when local runtime differs from last host runtime.
  - Host applies runtime under node locks and broadcasts `TimedTrafficLightsRuntimeAppliedCommand`.
  - Client applies the authoritative runtime; relevant divergence triggers resync.

- **Resync/reject**
  - Resync is master-node scoped via `TimedTrafficLightsResyncRequest`.
  - Host responds with current definition and runtime for the requested master.
  - Rejections use `RequestRejected` with `EntityType=3` and reason prefix `timed_tl:`.
  - Client-side resync requests are throttled with a frame cooldown.

- **Reconnect resync**
  - On client reconnect, host sends all known definition states and current runtime states per master node.

- **Readiness behavior**
  - Queue/flush runs only when synchronization is ready (`NetworkUtil.IsSynchronizationReady()`), preventing dispatch during early loading.

### 5) Data Exchange (messages/fields)
| Message | Direction | Field | Type | Description |
| --- | --- | --- | --- | --- |
| `TimedTrafficLightsDefinitionUpdateRequest` | Client -> Server | `MasterNodeId` | `ushort` | Master node of the TTL group. |
|  |  | `Removed` | `bool` | `true` = remove TTL, `false` = apply definition. |
|  |  | `Definition` | `TimedTrafficLightsDefinitionState` | Full script structure (when `Removed=false`). |
| `TimedTrafficLightsDefinitionAppliedCommand` | Server -> All | `MasterNodeId` | `ushort` | Authoritative master node. |
|  |  | `Removed` | `bool` | Removal flag. |
|  |  | `Definition` | `TimedTrafficLightsDefinitionState` | Effective applied definition (or `null` on remove). |
| `TimedTrafficLightsRuntimeUpdateRequest` | Client -> Server | `Runtime` | `TimedTrafficLightsRuntimeState` | Desired runtime state for one master node. |
| `TimedTrafficLightsRuntimeAppliedCommand` | Server -> All | `Runtime` | `TimedTrafficLightsRuntimeState` | Authoritative applied runtime state. |
| `TimedTrafficLightsResyncRequest` | Client -> Server | `MasterNodeId` | `ushort` | Target master node for resync. |
| `TimedTrafficLightsDefinitionState` | (payload) | `MasterNodeId` | `ushort` | Master node of the TTL group. |
|  |  | `NodeGroup` | `List<ushort>` | All nodes in the group. |
|  |  | `Nodes[]` | `List<NodeState>` | Node-level step definitions. |
|  |  | `Nodes[].Steps[]` | `List<StepState>` | Step parameters + segment/vehicle light states. |
| `TimedTrafficLightsRuntimeState` | (payload) | `MasterNodeId` | `ushort` | Runtime master node. |
|  |  | `IsRunning` | `bool` | Running flag. |
|  |  | `CurrentStep` | `int` | Current step index. |
|  |  | `Epoch` | `uint` | Frame-based host epoch marker. |
| `RequestRejected` | Server -> Client | `EntityType` | `byte` | 3 = node (master node). |
|  |  | `EntityId` | `int` | Affected master node. |
|  |  | `Reason` | `string` | Rejection reason (timed-tagged). |

Notes
- Definition and runtime are separated and dispatched as deltas per master node.
- Definition cache hashing suppresses redundant definition transmissions.
- Runtime broadcast/received caches suppress redundant runtime transmissions.
- Node locks and ignore/local-apply scopes prevent feedback loops and race-related inconsistencies.
