# CSM TM:PE Sync – Toggle Traffic Lights / Ampeln umschalten

This document is bilingual. German (DE) first, English (EN) follows.

---

## DE – Deutsch

### 1) Kurzbeschreibung
- Synchronisiert das TM:PE‑Umschalten von Ampeln an Knoten zwischen Host und Clients in CSM‑Sitzungen.
- Fängt lokale Änderungen in TM:PE per Harmony ab und überträgt sie via CSM.
- Liest nach der Anwendung den tatsächlichen Zustand (An/Aus) zurück und broadcastet ihn, um alle Seiten zu vereinheitlichen.

### 2) Dateistruktur
- `src/CSM.TmpeSync.ToggleTrafficLights/`
  - `ToggleTrafficLightsSyncFeature.cs` – Feature‑Bootstrap (aktiviert Listener).
  - `Handlers/` – CSM‑Command‑Handler (Server/Client Verarbeitungslogik).
  - `Messages/` – Netzwerkbefehle (ProtoBuf‑Verträge).
  - `Services/` – Adapter zu TM:PE, Harmony‑Listener, Synchronisations‑Helfer.

### 3) Dateiübersicht
| Datei | Zweck |
| --- | --- |
| `ToggleTrafficLightsSyncFeature.cs` | Registriert/aktiviert das Feature und den TM:PE‑Listener. |
| `Handlers/ToggleTrafficLightsAppliedCommandHandler.cs` | Client: verarbeitet Server‑Broadcast „Applied“ und setzt Ampelzustand lokal in TM:PE. |
| `Handlers/ToggleTrafficLightsUpdateRequestHandler.cs` | Server: validiert Request, setzt Ampelzustand in TM:PE und broadcastet den resultierenden Zustand. |
| `Messages/ToggleTrafficLightsAppliedCommand.cs` | Server → Alle: endgültiger Zustand je `NodeId` + `Enabled`. |
| `Messages/ToggleTrafficLightsUpdateRequest.cs` | Client → Server: Änderungswunsch (`NodeId`, `Enabled`). |
| `Services/ToggleTrafficLightsEventListener.cs` | Harmony‑Hook auf TM:PE‑`ToggleTrafficLight(ushort)`, leitet lokale Änderungen in CSM‑Befehle um. |
| `Services/ToggleTrafficLightsSynchronization.cs` | Gemeinsamer Versandweg (Server→Alle oder Client→Server) und Lese/Anwenden‑Fassade. |
| `Services/ToggleTrafficLightsTmpeAdapter.cs` | Adapter zur TM:PE API: Lesen/Schreiben des Ampelzustands (mit lokalem Ignore‑Scope). |

### 4) Workflow (Server/Host und Client)
- Host toggelt Ampel in TM:PE
  - Harmony‑Postfix erkennt Änderung und liest den aktuellen Zustand (`Enabled`) mit TM:PE.
  - Listener sendet `ToggleTrafficLightsAppliedCommand` an alle.
  - Clients wenden Zustand lokal an (Ignore‑Scope verhindert Schleifen).

- Client toggelt Ampel in TM:PE
  - Harmony‑Postfix liest den aktuellen Zustand und sendet `ToggleTrafficLightsUpdateRequest` an den Server.
  - Server validiert (Node vorhanden) und setzt den Zustand in TM:PE (unter Ignore‑Scope).
  - Server liest danach den tatsächlichen Zustand zurück und broadcastet `ToggleTrafficLightsAppliedCommand` an alle.
  - Alle wenden den Zustand lokal an (Client eingeschlossen; Ignore‑Scope verhindert Re‑Trigger).

- Abgelehnte Requests (Server)
  - Bei fehlenden Entitäten oder TM:PE‑Fehlern sendet der Server `RequestRejected` zurück (Grund + Entität).

### 5) Datenaustausch (Nachrichten/Felder)
| Nachricht | Richtung | Feld | Typ | Beschreibung |
| --- | --- | --- | --- | --- |
| `ToggleTrafficLightsUpdateRequest` | Client → Server | `NodeId` | `ushort` | Knoten‑ID der Kreuzung. |
|  |  | `Enabled` | `bool` | Gewünschter Ampelzustand am Knoten (An/Aus). |
| `ToggleTrafficLightsAppliedCommand` | Server → Alle | `NodeId` | `ushort` | Knoten‑ID der Kreuzung. |
|  |  | `Enabled` | `bool` | Effektiv angewandter Ampelzustand nach TM:PE. |
| `RequestRejected` | Server → Client | `EntityType` | `byte` | Entitätstyp: 3=Node (hier relevant). |
|  |  | `EntityId` | `int` | Betroffene Entität. |
|  |  | `Reason` | `string` | Grund, z. B. `entity_missing`, `tmpe_apply_failed`. |

Hinweise
- Es werden ausschließlich Knoten (Nodes) synchronisiert; es gibt kein Segmentfeld.
- Der Adapter verwendet einen lokalen Ignore‑Scope, um Rückkopplungen zu verhindern.

---

## EN – English

### 1) Summary
- Synchronizes TM:PE traffic‑light toggling at nodes between host and clients in CSM sessions.
- Hooks local TM:PE changes with Harmony and relays them via CSM.
- Reads back the effective state after apply and broadcasts it so all peers converge.

### 2) Directory Layout
- `src/CSM.TmpeSync.ToggleTrafficLights/`
  - `ToggleTrafficLightsSyncFeature.cs` – Feature bootstrap (enables listener).
  - `Handlers/` – CSM command handlers (server/client processing).
  - `Messages/` – Network commands (ProtoBuf contracts).
  - `Services/` – TM:PE adapter, Harmony listener, sync helpers.

### 3) File Overview
| File | Purpose |
| --- | --- |
| `ToggleTrafficLightsSyncFeature.cs` | Registers/enables the feature and the TM:PE listener. |
| `Handlers/ToggleTrafficLightsAppliedCommandHandler.cs` | Client: handles server “Applied” broadcast and applies state locally in TM:PE. |
| `Handlers/ToggleTrafficLightsUpdateRequestHandler.cs` | Server: validates request, applies in TM:PE, and broadcasts the resulting state. |
| `Messages/ToggleTrafficLightsAppliedCommand.cs` | Server → All: final state per `NodeId` + `Enabled`. |
| `Messages/ToggleTrafficLightsUpdateRequest.cs` | Client → Server: change request (`NodeId`, `Enabled`). |
| `Services/ToggleTrafficLightsEventListener.cs` | Harmony hook on TM:PE `ToggleTrafficLight(ushort)`, turns local edits into CSM commands. |
| `Services/ToggleTrafficLightsSynchronization.cs` | Common dispatch path (Server→All or Client→Server) and read/apply facade. |
| `Services/ToggleTrafficLightsTmpeAdapter.cs` | Adapter to TM:PE API: read/write traffic‑light state (with local ignore scope).

### 4) Workflow (Server/Host and Client)
- Host toggles in TM:PE
  - Harmony postfix detects the change and reads current `Enabled` via TM:PE.
  - Listener sends `ToggleTrafficLightsAppliedCommand` to all.
  - Clients apply locally (ignore scope prevents feedback loops).

- Client toggles in TM:PE
  - Harmony postfix reads current state and sends `ToggleTrafficLightsUpdateRequest` to the server.
  - Server validates (node exists) and applies in TM:PE (under ignore scope).
  - Server then reads back the effective state and broadcasts `ToggleTrafficLightsAppliedCommand` to all.
  - Everyone (including the originating client) applies locally; ignore scope prevents re‑triggering.

- Rejections (Server)
  - If entities are missing or TM:PE fails to apply, the server returns `RequestRejected` (with reason and entity info).

### 5) Data Exchange (messages/fields)
| Message | Direction | Field | Type | Description |
| --- | --- | --- | --- | --- |
| `ToggleTrafficLightsUpdateRequest` | Client → Server | `NodeId` | `ushort` | Node ID of the junction. |
|  |  | `Enabled` | `bool` | Desired traffic‑light state at the node (On/Off). |
| `ToggleTrafficLightsAppliedCommand` | Server → All | `NodeId` | `ushort` | Node ID of the junction. |
|  |  | `Enabled` | `bool` | Effective state after TM:PE application. |
| `RequestRejected` | Server → Client | `EntityType` | `byte` | Entity type: 3=Node (relevant here). |
|  |  | `EntityId` | `int` | Affected entity. |
|  |  | `Reason` | `string` | Reason, e.g., `entity_missing`, `tmpe_apply_failed`.

Notes
- Only nodes are synchronized; there’s no segment field.
- Adapter uses a local ignore scope to avoid echoing changes back.

