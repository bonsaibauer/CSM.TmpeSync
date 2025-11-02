# CSM TM:PE Sync - Speed Limits / Geschwindigkeitsbegrenzungen

This document is bilingual. German (DE) first, English (EN) follows.

---

## DE - Deutsch

### 1) Kurzbeschreibung
- Synchronisiert TM:PE-Geschwindigkeitslimits – sowohl Segment-Overrides als auch NetInfo-Defaults (Prefab-Standardwerte) – zwischen Host und Clients in CSM-Sitzungen.
- Segment-Modell wie Vehicle Restrictions: je konfigurierbarer Fahrspur werden Ordinalindex und Lane-Signatur übertragen; die tatsächliche `laneId` wird lokal ermittelt.
- Robuste Apply-Pipeline mit Warteschlange und Retry: Harmony-Hooks aggregieren Änderungen pro Segment/Prefab, der Apply-Koordinator versucht TM:PE-Calls mehrfach mit wachsendem Frame-Abstand, bis Manager und Optionen bereit sind.

### 2) Dateistruktur
- `src/CSM.TmpeSync.SpeedLimits/`
  - `SpeedLimitSyncFeature.cs` – Feature-Bootstrap (aktiviert Listener).
  - `Handlers/` – CSM-Command-Handler (Server/Client Verarbeitungslogik, inkl. Default-Handler).
  - `Messages/` – Netzwerkbefehle (ProtoBuf-Verträge) für Segment- und Default-Limits.
  - `Services/` – TM:PE-Adapter, Harmony-Listener, Synchronisations-Fassade, Codec sowie Apply-Koordinator.

### 3) Dateiübersicht
| Datei | Zweck |
| --- | --- |
| `SpeedLimitSyncFeature.cs` | Registriert/aktiviert das Feature und den TM:PE-Listener. |
| `Handlers/SpeedLimitsAppliedCommandHandler.cs` | Client: verarbeitet Server-Broadcast „Applied“ segmentweise und triggert das Retry-fähige Apply lokal. |
| `Handlers/SpeedLimitsUpdateRequestHandler.cs` | Server: validiert Request, sperrt das Segment (`EntityLocks`), leitet an den Apply-Koordinator weiter und broadcastet nach Erfolg. |
| `Handlers/DefaultSpeedLimitAppliedCommandHandler.cs` | Client: verarbeitet Default-Broadcasts (`NetInfo`-Name, Wert) und setzt sie lokal via Adapter. |
| `Handlers/DefaultSpeedLimitUpdateRequestHandler.cs` | Server: verarbeitet Client-Requests für NetInfo-Defaults, meldet Fehler und broadcastet angewandte Defaults. |
| `Messages/SpeedLimitsAppliedCommand.cs` | Server → Alle: angewendeter Segmentzustand mit Spur-Einträgen (Ordinal, Signatur, `SpeedLimitValue`, Pending-Flag). |
| `Messages/SpeedLimitsUpdateRequest.cs` | Client → Server: Änderungswunsch je Segment mit Spur-Einträgen (Ordinal, Signatur, `SpeedLimitValue`). |
| `Messages/DefaultSpeedLimitAppliedCommand.cs` | Server → Alle: angewandter NetInfo-Default (Prefab-Name, Custom-Flag, Wert). |
| `Messages/DefaultSpeedLimitUpdateRequest.cs` | Client → Server: Änderungswunsch für NetInfo-Defaults. |
| `Services/SpeedLimitEventListener.cs` | Harmony-Hooks auf TM:PE (`SetLaneSpeedLimit*`, `Set/ResetCustomNetinfoSpeedLimit`) und Übergabe an die Synchronisation. |
| `Services/SpeedLimitSynchronization.cs` | Gemeinsamer Versandweg, Apply-Koordinator mit Retry/Backoff, Default-Anwendung und Local-Apply-Scopes. |
| `Services/SpeedLimitTmpeAdapter.cs` | Segment-Lese-/Schreibadapter (Ordinals, Signaturprüfung, LaneId-Auflösung, Ignore-Scope). |
| `Services/SpeedLimitAdapter.cs` | Reflection-Brücke zum TM:PE-SpeedLimitManager inkl. NetInfo-Default-APIs. |
| `Services/SpeedLimitCodec.cs` | Palette-/Raw-Codec für `SpeedLimitValue` (km/h, mph, unlimited, default, Pending-Flag). |

### 4) Workflow (Server/Host und Client)
- **Host ändert Segment-Limits in TM:PE**
  - Harmony-Postfix auf den `SetLaneSpeedLimit*`-Varianten erkennt die Änderung, liest den kompletten Segmentzustand (`TryRead`).
  - Die Synchronisation broadcastet `SpeedLimitsAppliedCommand`; Clients wenden die Werte lokal über den Apply-Koordinator an (mit Retry, falls TM:PE noch initialisiert).

- **Client ändert Segment-Limits in TM:PE**
  - Harmony-Postfix sendet `SpeedLimitsUpdateRequest` (SegmentId + Spur-Einträge) an den Server.
  - Der Server validiert Segmentexistenz, erwirbt ein Segment-Lock und übergibt den Request an den Apply-Koordinator.
  - Apply-Koordinator versucht sofort anzuwenden; bei fehlenden TM:PE-Managern/NullReferenceExceptions wird der Vorgang mit wachsendem Frame-Delay (5 → 240 Frames, max. 6 Versuche) erneut eingeplant.
  - Nach erfolgreicher Anwendung wird der Segmentzustand erneut gelesen und als `SpeedLimitsAppliedCommand` an alle broadcastet.

- **Default-/NetInfo-Limits (Prefab)**
  - Listener patcht `SetCustomNetinfoSpeedLimit`/`ResetCustomNetinfoSpeedLimit` und broadcastet `DefaultSpeedLimitAppliedCommand`, wenn der Host Defaults verändert.
  - Clients wenden Defaults via `TryApplyDefault` an (Prefab-Lookup, Ignore-Scope, Fehlercode `DefaultApplyError`).
  - Clientseitige Änderungen senden `DefaultSpeedLimitUpdateRequest`; der Server wendet den Wert an, verschickt ggf. `RequestRejected` (`entity_missing` oder `tmpe_apply_failed`), danach folgt ein Broadcast des angewandten Zustands.

- **Abgelehnte Requests (Server)**
  - Bei fehlenden Entitäten oder nicht erfolgreichen TM:PE-Calls wird `RequestRejected` an den auslösenden Client geschickt (`EntityType` 2 = Segment, 0 = NetInfo).

### 5) Datenaustausch (Nachrichten/Felder)
| Nachricht | Richtung | Feld | Typ | Beschreibung |
| --- | --- | --- | --- | --- |
| `SpeedLimitsUpdateRequest` | Client → Server | `SegmentId` | `ushort` | Segment-ID der betroffenen Straße. |
|  |  | `Items[]` | `Entry` | Spur-Einträge (siehe unten). |
| `SpeedLimitsAppliedCommand` | Server → Alle | `SegmentId` | `ushort` | Segment-ID der Straße. |
|  |  | `Items[]` | `Entry` | Effektiv angewandte Spur-Werte (inkl. Pending-Flag). |
| `DefaultSpeedLimitUpdateRequest` | Client → Server | `NetInfoName` | `string` | Prefab-Name (NetInfo), dessen Default geändert wird. |
|  |  | `HasCustomSpeed` | `bool` | `true`: eigener Wert, `false`: Reset auf Default. |
|  |  | `CustomGameSpeed` | `float` | TM:PE-Spielgeschwindigkeit (Game Units) bei Custom. |
| `DefaultSpeedLimitAppliedCommand` | Server → Alle | `NetInfoName` | `string` | Prefab-Name (NetInfo). |
|  |  | `HasCustomSpeed` | `bool` | Gibt an, ob ein Custom-Default aktiv ist. |
|  |  | `CustomGameSpeed` | `float` | Angewandte Spielgeschwindigkeit. |
| `RequestRejected` | Server → Client | `EntityType` | `byte` | 2 = Segment, 0 = NetInfo (Default). |
|  |  | `EntityId` | `int` | Segment-ID oder 0 für Prefabs. |
|  |  | `Reason` | `string` | Grund, z. B. `entity_missing`, `tmpe_apply_failed`, `tmpe_apply_failed_default`. |

- `Entry` (Spur-Eintrag)
  - `LaneOrdinal` (`int`): Ordinal der Spur im `NetInfo`-Lane-Array des Segments.
  - `Signature` (`LaneSignature`): Rohwerte (`LaneTypeRaw`, `VehicleTypeRaw`, `DirectionRaw`) zur Stabilisierung zwischen Host/Client.
  - `Speed` (`SpeedLimitValue`): Kodiertes Limit (siehe unten).

- `SpeedLimitValue`
  - `Type` (`SpeedLimitValueType`): `Default`, `KilometresPerHour`, `MilesPerHour`, `Unlimited`.
  - `Index` (`byte`): Palettenindex je Einheit (km/h, mph).
  - `RawSpeedKmh` (`float`): Rohwert in km/h (Lossless-Kanal, falls TM:PE-Wert nicht exakt in die Palette fällt).
  - `Pending` (`bool`): Kennzeichnet „Apply läuft noch“; wird vom Apply-Koordinator gesetzt und nach erfolgreichem Apply entfernt.

Hinweise
- Lane-Identifikation ausschließlich über `(LaneOrdinal, LaneSignature)` – unabhängig von `laneId`/`laneIndex` zur Laufzeit.
- Apply-Koordinator fasst parallele Requests je Segment zusammen, nutzt Segment-Locks und Backoff-Retry (5/15/30/60/120/240 Frames) zur Abwehr von TM:PE-Initialisierungsproblemen.
- `SpeedLimitAdapter` spiegelt TM:PEs `SpeedLimitManager` per Reflection, inkl. NetInfo-Defaults; `SpeedLimitCodec` kodiert Palette- und Rohwerte.
- Ignore-Scopes (`LocalApplyScope`, `SpeedLimitTmpeAdapter.IsLocalApplyActive`) verhindern Feedback-Schleifen zwischen Listener und Apply.

---

## EN - English

### 1) Summary
- Synchronises TM:PE speed limits – segment overrides and NetInfo defaults (prefab base speeds) – between host and clients in CSM sessions.
- Segment-based schema like Vehicle Restrictions: each configurable lane sends its ordinal and lane signature; the runtime `laneId` is resolved locally.
- Resilient apply pipeline with queueing and retries: Harmony hooks aggregate per segment/prefab, the apply coordinator keeps retrying with increasing frame delays until TM:PE managers/options are ready.

### 2) Directory Layout
- `src/CSM.TmpeSync.SpeedLimits/`
  - `SpeedLimitSyncFeature.cs` – Feature bootstrap (enables listener).
  - `Handlers/` – CSM command handlers (server/client processing, including default handlers).
  - `Messages/` – Network commands (ProtoBuf contracts) for segment and default limits.
  - `Services/` – TM:PE adapter, Harmony listener, sync façade, codec, and apply coordinator.

### 3) File Overview
| File | Purpose |
| --- | --- |
| `SpeedLimitSyncFeature.cs` | Registers/enables the feature and the TM:PE listener. |
| `Handlers/SpeedLimitsAppliedCommandHandler.cs` | Client: handles server “Applied” broadcasts per segment and triggers the retry-aware local apply. |
| `Handlers/SpeedLimitsUpdateRequestHandler.cs` | Server: validates requests, locks the segment (`EntityLocks`), forwards to the apply coordinator, then broadcasts after success. |
| `Handlers/DefaultSpeedLimitAppliedCommandHandler.cs` | Client: processes default broadcasts (`NetInfo` name/value) and applies them locally via the adapter. |
| `Handlers/DefaultSpeedLimitUpdateRequestHandler.cs` | Server: handles client requests for NetInfo defaults, reports failures, and broadcasts the applied defaults. |
| `Messages/SpeedLimitsAppliedCommand.cs` | Server → All: applied per-segment state with lane entries (ordinal, signature, `SpeedLimitValue`, pending flag). |
| `Messages/SpeedLimitsUpdateRequest.cs` | Client → Server: desired per-segment state with lane entries (ordinal, signature, `SpeedLimitValue`). |
| `Messages/DefaultSpeedLimitAppliedCommand.cs` | Server → All: applied NetInfo default (prefab name, custom flag, value). |
| `Messages/DefaultSpeedLimitUpdateRequest.cs` | Client → Server: change request for NetInfo defaults. |
| `Services/SpeedLimitEventListener.cs` | Harmony hooks (`SetLaneSpeedLimit*`, `Set/ResetCustomNetinfoSpeedLimit`) and dispatch to synchronisation. |
| `Services/SpeedLimitSynchronization.cs` | Common dispatch path, apply coordinator with retry/backoff, default application and local apply scopes. |
| `Services/SpeedLimitTmpeAdapter.cs` | Segment read/apply adapter (ordinals, signature checks, laneId resolution, ignore scope). |
| `Services/SpeedLimitAdapter.cs` | Reflection bridge to TM:PE’s SpeedLimitManager including NetInfo default APIs. |
| `Services/SpeedLimitCodec.cs` | Palette/raw codec for `SpeedLimitValue` (km/h, mph, unlimited, default, pending flag). |

### 4) Workflow (Server/Host and Client)
- **Host edits segment limits in TM:PE**
  - Harmony postfix on the `SetLaneSpeedLimit*` overloads detects the change and reads the full segment state (`TryRead`).
  - Synchronisation broadcasts `SpeedLimitsAppliedCommand`; clients feed it into the retry-capable apply coordinator until TM:PE accepts it.

- **Client edits segment limits in TM:PE**
  - Harmony postfix sends `SpeedLimitsUpdateRequest` (segmentId + lane entries) to the server.
  - The server validates the segment, acquires an `EntityLocks` segment lock, and hands the request to the apply coordinator.
  - Apply coordinator tries immediately; if TM:PE managers/options are unavailable or throw `NullReferenceException`, it requeues with growing frame delays (5 → 240 frames, up to six attempts).
  - After a successful apply the server reads back the state and broadcasts `SpeedLimitsAppliedCommand` to everyone.

- **Default / NetInfo limits (prefabs)**
  - Listener patches `SetCustomNetinfoSpeedLimit` / `ResetCustomNetinfoSpeedLimit` and broadcasts `DefaultSpeedLimitAppliedCommand` when the host changes defaults.
  - Clients call `TryApplyDefault` (prefab lookup, ignore scope, detailed error enum) to mirror defaults locally.
  - Client-originated changes send `DefaultSpeedLimitUpdateRequest`; the server applies them, emits `RequestRejected` on failures, and then broadcasts the applied state.

- **Rejections (Server)**
  - Missing entities or failed TM:PE calls result in `RequestRejected` (segment requests use `EntityType = 2`, defaults use `EntityType = 0`).

### 5) Data Exchange (messages/fields)
| Message | Direction | Field | Type | Description |
| --- | --- | --- | --- | --- |
| `SpeedLimitsUpdateRequest` | Client → Server | `SegmentId` | `ushort` | Segment ID of the affected road. |
|  |  | `Items[]` | `Entry` | Lane entries (see below). |
| `SpeedLimitsAppliedCommand` | Server → All | `SegmentId` | `ushort` | Segment ID of the road. |
|  |  | `Items[]` | `Entry` | Applied lane entries (including pending flag). |
| `DefaultSpeedLimitUpdateRequest` | Client → Server | `NetInfoName` | `string` | Prefab name (NetInfo) whose default changes. |
|  |  | `HasCustomSpeed` | `bool` | `true`: keep custom value, `false`: reset to default. |
|  |  | `CustomGameSpeed` | `float` | TM:PE game speed when custom is requested. |
| `DefaultSpeedLimitAppliedCommand` | Server → All | `NetInfoName` | `string` | Prefab name (NetInfo). |
|  |  | `HasCustomSpeed` | `bool` | Indicates whether a custom default is active. |
|  |  | `CustomGameSpeed` | `float` | Applied TM:PE game speed. |
| `RequestRejected` | Server → Client | `EntityType` | `byte` | 2 = segment, 0 = NetInfo (default). |
|  |  | `EntityId` | `int` | Segment ID or 0 for prefabs. |
|  |  | `Reason` | `string` | Reason, e.g. `entity_missing`, `tmpe_apply_failed`, `tmpe_apply_failed_default`. |

- `Entry` (lane entry)
  - `LaneOrdinal` (`int`): ordinal within the segment’s `NetInfo` lanes array.
  - `Signature` (`LaneSignature`): raw values (`LaneTypeRaw`, `VehicleTypeRaw`, `DirectionRaw`) to stabilise identity across peers.
  - `Speed` (`SpeedLimitValue`): encoded limit value (see below).

- `SpeedLimitValue`
  - `Type` (`SpeedLimitValueType`): `Default`, `KilometresPerHour`, `MilesPerHour`, `Unlimited`.
  - `Index` (`byte`): palette index per unit (km/h, mph).
  - `RawSpeedKmh` (`float`): raw km/h (lossless channel when TM:PE does not match the palette exactly).
  - `Pending` (`bool`): indicates “apply still running”; set by the retry pipeline and cleared once TM:PE accepts the value.

Notes
- Lane identification relies exclusively on `(LaneOrdinal, LaneSignature)` – independent of runtime `laneId`/`laneIndex`.
- The apply coordinator merges concurrent requests per segment, uses segment locks, and retries with exponential frame backoff (5/15/30/60/120/240) to ride out TM:PE initialisation issues.
- `SpeedLimitAdapter` reflects TM:PE’s `SpeedLimitManager`, including NetInfo default APIs; `SpeedLimitCodec` encodes palette and raw values.
- Ignore scopes (`LocalApplyScope`, `SpeedLimitTmpeAdapter.IsLocalApplyActive`) prevent feedback loops between listener hooks and local applies.
