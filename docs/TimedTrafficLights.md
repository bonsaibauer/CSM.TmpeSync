# CSM TM:PE Sync - Timed Traffic Lights / Zeitgesteuerte Ampeln

This document is bilingual. German (DE) first, English (EN) follows.

---

## DE - Deutsch

### 1) Kurzbeschreibung
- Synchronisiert TM:PE-Timed-Traffic-Lights (TTL) zwischen Host und Clients in CSM-Sitzungen.
- Verwendet zwei Streams: Definitions-Stream (Script-Struktur) und Runtime-Stream (laufender Step/Start-Stop).
- Host bleibt autoritativ: Clients senden Requests, der Host wendet an und broadcastet den finalen Zustand.
- Vermeidet Live-Traffic-Spitzen: keine dauerhafte Übertragung einzelner Lane-Lichtzustände pro Frame, sondern Delta-/Event-basierte Synchronisation mit Keyframes.

### 2) Dateistruktur
- `src/CSM.TmpeSync.TimedTrafficLights/`
  - `TimedTrafficLightsSyncFeature.cs` - Feature-Bootstrap (aktiviert Listener + Runtime-Tracker).
  - `Handlers/` - CSM-Command-Handler (Server/Client-Verarbeitung inkl. Resync/Reject).
  - `Messages/` - Netzwerkbefehle (ProtoBuf-Verträge) für Definition, Runtime und Resync.
  - `Services/` - Harmony-Listener, TM:PE-Adapter, Synchronisations-Logik, Definition-/Runtime-Caches.

### 3) Dateiübersicht
| Datei | Zweck |
| --- | --- |
| `TimedTrafficLightsSyncFeature.cs` | Registriert/aktiviert das Feature, startet Definition-Listener und Runtime-Tracker. |
| `Handlers/TimedTrafficLightsDefinitionUpdateRequestHandler.cs` | Server: verarbeitet Definition-Requests von Clients. |
| `Handlers/TimedTrafficLightsDefinitionAppliedCommandHandler.cs` | Client: verarbeitet Host-Definitionen; `OnClientConnect` triggert Host-Resync. |
| `Handlers/TimedTrafficLightsRuntimeUpdateRequestHandler.cs` | Server: verarbeitet Runtime-Requests von Clients (Start/Stop/Step/TestMode). |
| `Handlers/TimedTrafficLightsRuntimeAppliedCommandHandler.cs` | Client: verarbeitet Runtime-Broadcasts vom Host. |
| `Handlers/TimedTrafficLightsResyncRequestHandler.cs` | Server: verarbeitet gezielte Resync-Anfragen pro Master-Knoten. |
| `Handlers/TimedTrafficLightsRequestRejectedHandler.cs` | Client: verarbeitet hostseitige Ablehnungen (`RequestRejected`) für Timed-TTL. |
| `Messages/TimedTrafficLightsDefinitionUpdateRequest.cs` | Client -> Server: Änderungswunsch für Definition (`MasterNodeId`, `Removed`, `Definition`). |
| `Messages/TimedTrafficLightsDefinitionAppliedCommand.cs` | Server -> Alle: final angewandte Definition oder Entfernen-Markierung. |
| `Messages/TimedTrafficLightsRuntimeUpdateRequest.cs` | Client -> Server: Änderungswunsch für Runtime (`Runtime`). |
| `Messages/TimedTrafficLightsRuntimeAppliedCommand.cs` | Server -> Alle: finaler Runtime-Status (`Runtime`). |
| `Messages/TimedTrafficLightsResyncRequest.cs` | Client -> Server: gezielter Resync-Request pro `MasterNodeId`. |
| `Messages/TimedTrafficLightsDefinitionState.cs` | Wire-State der TTL-Definition (NodeGroup, Nodes, Steps, Segment-/Vehicle-Lights). |
| `Messages/TimedTrafficLightsRuntimeState.cs` | Wire-State der Runtime (`MasterNodeId`, `IsRunning`, `CurrentStep`, `Epoch`). |
| `Services/TimedTrafficLightsEventListener.cs` | Harmony-Hooks auf TM:PE-TTL-UI/Mutationen; markiert lokale Definition-/Runtime-Änderungen als dirty. |
| `Services/TimedTrafficLightsSynchronization.cs` | Kernlogik für beide Streams: Polling, Delta-Ermittlung, Dispatch, Host-Apply, Pending/Timeout, Resync, Reject-Handling. |
| `Services/TimedTrafficLightsTmpeAdapter.cs` | Lesen/Anwenden von TTL-Definition/Runtime in TM:PE (inkl. Local-Apply-Scopes). |
| `Services/TimedTrafficLightsStateCache.cs` | Host-/Client-Cache für Definitionszustände + Hashes + Node->Master-Mapping. |
| `Services/TimedTrafficLightsRuntimeCache.cs` | Cache für Runtime-Broadcast- und Received-Stände pro Master-Knoten. |
| `Services/TimedTrafficLightsRuntimeTracker.cs` | Periodisches Runtime-Polling (Host-Broadcast / Client-Runtime-Requests). |

### 4) Workflow (Server/Host und Client)
- **Host ändert TTL-Definition in TM:PE**
  - Harmony/Polling erkennt Definitionsänderungen.
  - Host erfasst aktuelle Definitionen via Adapter, vergleicht Hashes und broadcastet nur Deltas als `TimedTrafficLightsDefinitionAppliedCommand`.
  - Clients wenden Definitionen lokal an; bei Fehlern wird ein gezielter Resync angefordert.

- **Client ändert TTL-Definition in TM:PE**
  - Listener markiert den betroffenen Master-Knoten als dirty.
  - Client-Polling erzeugt bei echtem Hash-Delta einen `TimedTrafficLightsDefinitionUpdateRequest`.
  - Host validiert/anwendet unter Node-Locks, sendet danach den autoritativen `DefinitionApplied`-Broadcast an alle.

- **Host ändert TTL-Runtime in TM:PE (Start/Stop/Step)**
  - Runtime-Tracker pollt zyklisch.
  - Host sendet `TimedTrafficLightsRuntimeAppliedCommand` nur bei Runtime-Delta (IsRunning/CurrentStep) oder Keyframe-Intervall.

- **Client ändert TTL-Runtime in TM:PE**
  - Listener markiert Runtime als dirty (z. B. `Start`, `Stop`, `SkipStep`, `SetTestMode`).
  - Client-Polling sendet `TimedTrafficLightsRuntimeUpdateRequest` nur bei Delta gegenüber zuletzt empfangener Host-Runtime.
  - Host validiert/anwendet Runtime und broadcastet die effektiv angewandte Runtime als `RuntimeApplied`.

- **Resync, Pending, Reject**
  - Definition- und Runtime-Requests werden clientseitig mit Pending-Tracking/Timeout verwaltet.
  - Bei Apply-Fehlern, Divergenzen oder verspäteten Zuständen sendet der Client `TimedTrafficLightsResyncRequest`.
  - Host antwortet masterbezogen mit aktueller Definition + Runtime (oder Removed-Definition).
  - Ablehnungen laufen über `RequestRejected` (EntityType=3/Node, Timed-Prefix im Reason).

### 5) Datenaustausch (Nachrichten/Felder)
| Nachricht | Richtung | Feld | Typ | Beschreibung |
| --- | --- | --- | --- | --- |
| `TimedTrafficLightsDefinitionUpdateRequest` | Client -> Server | `MasterNodeId` | `ushort` | Master-Knoten der TTL-Gruppe. |
|  |  | `Removed` | `bool` | `true` = TTL entfernen, `false` = Definition anwenden. |
|  |  | `Definition` | `TimedTrafficLightsDefinitionState` | Vollständige Script-Struktur (bei `Removed=false`). |
| `TimedTrafficLightsDefinitionAppliedCommand` | Server -> Alle | `MasterNodeId` | `ushort` | Autoritativer Master-Knoten. |
|  |  | `Removed` | `bool` | Entfernen-Flag. |
|  |  | `Definition` | `TimedTrafficLightsDefinitionState` | Effektiv angewandte Definition (oder `null` bei Remove). |
| `TimedTrafficLightsRuntimeUpdateRequest` | Client -> Server | `Runtime` | `TimedTrafficLightsRuntimeState` | Gewünschter Runtime-Status für einen Master-Knoten. |
| `TimedTrafficLightsRuntimeAppliedCommand` | Server -> Alle | `Runtime` | `TimedTrafficLightsRuntimeState` | Autoritativ angewandter Runtime-Status. |
| `TimedTrafficLightsResyncRequest` | Client -> Server | `MasterNodeId` | `ushort` | Ziel-Master für gezielten Resync. |
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
- Zwei getrennte Streams reduzieren NetTraffic deutlich: seltene Definitionsdeltas + Runtime-Ereignisse/Keyframes statt Live-Lane-Streaming.
- Runtime-Keyframes dienen als Drift-Korrektur, auch wenn zwischenzeitlich keine Step-Änderung auftritt.
- Node-Locks + Ignore-/Local-Apply-Scopes verhindern Re-Trigger-Schleifen und race-bedingte Inkonsistenzen.

---

## EN - English

### 1) Summary
- Synchronises TM:PE timed traffic lights (TTL) between host and clients in CSM sessions.
- Uses two streams: definition stream (script structure) and runtime stream (running step/start-stop).
- Host stays authoritative: clients send requests, host applies and broadcasts final state.
- Avoids live traffic spikes: no per-lane light streaming every frame, only delta/event sync plus keyframes.

### 2) Directory Layout
- `src/CSM.TmpeSync.TimedTrafficLights/`
  - `TimedTrafficLightsSyncFeature.cs` - feature bootstrap (enables listener + runtime tracker).
  - `Handlers/` - CSM command handlers (server/client processing incl. resync/reject).
  - `Messages/` - network commands (ProtoBuf contracts) for definition, runtime, resync.
  - `Services/` - Harmony listener, TM:PE adapter, synchronization logic, definition/runtime caches.

### 3) File Overview
| File | Purpose |
| --- | --- |
| `TimedTrafficLightsSyncFeature.cs` | Registers/enables the feature; starts definition listener and runtime tracker. |
| `Handlers/TimedTrafficLightsDefinitionUpdateRequestHandler.cs` | Server: processes client definition requests. |
| `Handlers/TimedTrafficLightsDefinitionAppliedCommandHandler.cs` | Client: processes host definitions; `OnClientConnect` triggers host resync. |
| `Handlers/TimedTrafficLightsRuntimeUpdateRequestHandler.cs` | Server: processes client runtime requests (start/stop/step/test mode). |
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
| `Services/TimedTrafficLightsEventListener.cs` | Harmony hooks on TM:PE TTL UI/mutations; marks local definition/runtime edits as dirty. |
| `Services/TimedTrafficLightsSynchronization.cs` | Core for both streams: polling, deltas, dispatch, host apply, pending/timeout, resync, reject handling. |
| `Services/TimedTrafficLightsTmpeAdapter.cs` | Read/apply TTL definition/runtime in TM:PE (with local-apply scopes). |
| `Services/TimedTrafficLightsStateCache.cs` | Host/client cache for definitions + hashes + node-to-master mapping. |
| `Services/TimedTrafficLightsRuntimeCache.cs` | Cache for runtime broadcast/received states per master node. |
| `Services/TimedTrafficLightsRuntimeTracker.cs` | Periodic runtime polling (host broadcasts / client runtime requests). |

### 4) Workflow (Server/Host and Client)
- **Host edits TTL definition in TM:PE**
  - Harmony/polling detects definition changes.
  - Host captures current definitions via adapter, compares hashes, and broadcasts only deltas as `TimedTrafficLightsDefinitionAppliedCommand`.
  - Clients apply definitions locally; on failure they trigger targeted resync.

- **Client edits TTL definition in TM:PE**
  - Listener marks the affected master node as dirty.
  - Client polling emits `TimedTrafficLightsDefinitionUpdateRequest` only when hash differs from cached host state.
  - Host validates/applies under node locks, then broadcasts authoritative `DefinitionApplied` to all peers.

- **Host edits TTL runtime in TM:PE (start/stop/step)**
  - Runtime tracker polls periodically.
  - Host sends `TimedTrafficLightsRuntimeAppliedCommand` only on runtime delta (`IsRunning`/`CurrentStep`) or keyframe interval.

- **Client edits TTL runtime in TM:PE**
  - Listener marks runtime dirty (for example `Start`, `Stop`, `SkipStep`, `SetTestMode`).
  - Client polling sends `TimedTrafficLightsRuntimeUpdateRequest` only on delta vs last received host runtime.
  - Host validates/applies runtime and broadcasts effective runtime with `RuntimeApplied`.

- **Resync, pending, reject**
  - Definition and runtime requests are tracked with pending/timeout on the client.
  - On apply failures, divergence, or stale states, client sends `TimedTrafficLightsResyncRequest`.
  - Host responds per master node with current definition + runtime (or removed definition).
  - Rejections use `RequestRejected` (EntityType=3/node, timed-tagged reason).

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
- Two separate streams keep network traffic low: infrequent definition deltas plus runtime events/keyframes instead of live per-lane streaming.
- Runtime keyframes correct drift even when no immediate step transition occurs.
- Node locks plus ignore/local-apply scopes prevent feedback loops and race-related inconsistencies.
