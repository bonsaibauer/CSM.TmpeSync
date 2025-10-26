# CSM TM:PE Sync - Speed Limits / Geschwindigkeitsbegrenzungen

This document is bilingual. German (DE) first, English (EN) follows.

---

## DE - Deutsch

### 1) Kurzbeschreibung
- Synchronisiert TM:PE-Geschwindigkeitslimits zwischen Host und Clients in CSM-Sitzungen.
- Nutzt ein segmentbasiertes Modell wie Vehicle Restrictions: je Segment werden pro konfigurierbarer Fahrspur ordinale Indexe und eine Lane-Signatur übertragen (stabiler als laneId/laneIndex).
- Lokale Änderungen werden via Harmony abgefangen, konsolidiert und gemäß Host-Autorität verteilt.

### 2) Dateistruktur
- `src/CSM.TmpeSync.SpeedLimits/`
  - `SpeedLimitSyncFeature.cs` – Feature-Bootstrap (aktiviert Listener).
  - `Handlers/` – CSM-Command-Handler (Server/Client Verarbeitungslogik).
  - `Messages/` – Netzwerkbefehle (ProtoBuf-Verträge).
  - `Services/` – TM:PE-Adapter, Harmony-Listener, Synchronisations-Fassade und Codec.

### 3) Dateiübersicht
| Datei | Zweck |
| --- | --- |
| `SpeedLimitSyncFeature.cs` | Registriert/aktiviert das Feature und den TM:PE-Listener. |
| `Handlers/SpeedLimitsAppliedCommandHandler.cs` | Client: verarbeitet Server-Broadcast „Applied“ segmentweise und setzt Limits lokal in TM:PE. |
| `Handlers/SpeedLimitsUpdateRequestHandler.cs` | Server: validiert Request, setzt Limits segmentweise in TM:PE und broadcastet den angewendeten Zustand. |
| `Messages/SpeedLimitsAppliedCommand.cs` | Server → Alle: angewendeter Zustand je Segment mit Spur-Einträgen (Ordinal, Signatur, Speed). |
| `Messages/SpeedLimitsUpdateRequest.cs` | Client → Server: Änderungswunsch je Segment mit Spur-Einträgen (Ordinal, Signatur, Speed). |
| `Services/SpeedLimitEventListener.cs` | Harmony-Hooks auf TM:PE (SetLaneSpeedLimit…), liest Segmentzustand und leitet in CSM-Befehle um. |
| `Services/SpeedLimitSynchronization.cs` | Gemeinsamer Versandweg (Server→Alle oder Client→Server) und Lese/Anwenden-Fassade. |
| `Services/SpeedLimitTmpeAdapter.cs` | Adapter zu TM:PE API/Implementierung: Lesen/Schreiben der Limits je Segment (mit Ignore-Scope gegen Echo). |
| `Services/SpeedLimitAdapter.cs` | Reflektierte Brücke in TM:PE SpeedLimitManager (Setzen/Lesen einzelner Lanes). |
| `Services/SpeedLimitCodec.cs` | Palette-/Raw‑Codec für `SpeedLimitValue` (km/h, mph, unlimited, default). |

### 4) Workflow (Server/Host und Client)
- Host ändert ein Limit in TM:PE
  - Harmony-Postfix im Listener erkennt die Änderung (auf `SetLaneSpeedLimit`), rekonstruiert den Segmentzustand (`TryRead`).
  - Listener sendet `SpeedLimitsAppliedCommand` für das Segment an alle.
  - Clients wenden die Limits lokal an (Ignore-Scope verhindert Schleifen).

- Client ändert ein Limit in TM:PE
  - Harmony-Postfix sendet `SpeedLimitsUpdateRequest` mit SegmentId und Spur-Einträgen (Ordinal/Signatur/Speed) an den Server.
  - Server validiert das Segment, setzt die Limits spurweise in TM:PE (Adapter, Ignore-Scope).
  - Server liest den resultierenden Segmentzustand und broadcastet `SpeedLimitsAppliedCommand` an alle.
  - Alle wenden Werte lokal an (Client eingeschlossen; Ignore-Scope verhindert Re-Trigger).

- Abgelehnte Requests (Server)
  - Bei fehlenden Entitäten oder TM:PE-Fehlern sendet der Server `RequestRejected` zurück (Grund + Entität, Segment=2).

### 5) Datenaustausch (Nachrichten/Felder)
| Nachricht | Richtung | Feld | Typ | Beschreibung |
| --- | --- | --- | --- | --- |
| `SpeedLimitsUpdateRequest` | Client → Server | `SegmentId` | `ushort` | Segment-ID der betroffenen Straße. |
|  |  | `Items[]` | `Entry` | Liste der Spur-Einträge (siehe unten). |
| `SpeedLimitsAppliedCommand` | Server → Alle | `SegmentId` | `ushort` | Segment-ID der betroffenen Straße. |
|  |  | `Items[]` | `Entry` | Liste der Spur-Einträge (siehe unten). |
| `RequestRejected` | Server → Client | `EntityType` | `byte` | Entitätstyp: 2 = Segment. |
|  |  | `EntityId` | `int` | Betroffene Entität. |
|  |  | `Reason` | `string` | Grund, z. B. `entity_missing`, `tmpe_apply_failed`. |

- `Entry` (Spur-Eintrag)
  - `LaneOrdinal` (`int`): Ordinal der Spur im `NetInfo`-Lane-Array des Segments.
  - `Signature` (`LaneSignature`): Struktur zur Stabilisierung über Host/Client hinweg:
    - `LaneTypeRaw` (`int`) – `NetInfo.LaneType` der Spur
    - `VehicleTypeRaw` (`int`) – `VehicleInfo.VehicleType` der Spur
    - `DirectionRaw` (`int`) – `NetInfo.Direction` der Spur
  - `Speed` (`SpeedLimitValue`): Kodiertes Limit

- `SpeedLimitValue`
  - `Type` (`SpeedLimitValueType`): `Default`, `KilometresPerHour`, `MilesPerHour`, `Unlimited`
  - `Index` (`byte`): Palettenindex je Einheit (km/h, mph)
  - `RawSpeedKmh` (`float`): Rohwert in km/h als verlustfreier Kanal
  - `Pending` (`bool`): Markierung für „noch im Anwenden“, intern genutzt

Hinweise
- Lane-Identifikation erfolgt ausschließlich über `(LaneOrdinal, LaneSignature)` innerhalb eines Segments – unabhängig von `laneId`/`laneIndex` zur Laufzeit.
- Der Adapter setzt/liest Limits über TM:PEs `SpeedLimitManager` (Reflection), der Listener patcht `SetLaneSpeedLimit`-Varianten.
- Ignore-Scope verhindert, dass eigene Applies erneut Listener-Events auslösen.

---

## EN - English

### 1) Summary
- Synchronizes TM:PE speed limits between host and clients in CSM sessions.
- Uses a segment-based model like Vehicle Restrictions: for each configurable lane, the ordinal index and a lane signature are transmitted (more stable than laneId/laneIndex).
- Local edits are hooked via Harmony, consolidated, and dispatched according to host authority.

### 2) Directory Layout
- `src/CSM.TmpeSync.SpeedLimits/`
  - `SpeedLimitSyncFeature.cs` – Feature bootstrap (enables listener).
  - `Handlers/` – CSM command handlers (server/client processing).
  - `Messages/` – Network commands (ProtoBuf contracts).
  - `Services/` – TM:PE adapter, Harmony listener, sync façade and codec.

### 3) File Overview
| File | Purpose |
| --- | --- |
| `SpeedLimitSyncFeature.cs` | Registers/enables the feature and the TM:PE listener. |
| `Handlers/SpeedLimitsAppliedCommandHandler.cs` | Client: handles server "Applied" broadcast per segment and applies locally in TM:PE. |
| `Handlers/SpeedLimitsUpdateRequestHandler.cs` | Server: validates request, applies per segment in TM:PE, then broadcasts the applied state. |
| `Messages/SpeedLimitsAppliedCommand.cs` | Server → All: applied state per segment with lane entries (ordinal, signature, speed). |
| `Messages/SpeedLimitsUpdateRequest.cs` | Client → Server: change request per segment with lane entries (ordinal, signature, speed). |
| `Services/SpeedLimitEventListener.cs` | Harmony hooks into TM:PE (SetLaneSpeedLimit…), reads segment state and translates to CSM commands. |
| `Services/SpeedLimitSynchronization.cs` | Common dispatch path (Server→All or Client→Server) and read/apply façade. |
| `Services/SpeedLimitTmpeAdapter.cs` | Adapter to TM:PE API/implementation: read/write limits per segment (with ignore scope to avoid echo). |
| `Services/SpeedLimitAdapter.cs` | Reflected bridge to TM:PE SpeedLimitManager (set/read single lanes). |
| `Services/SpeedLimitCodec.cs` | Palette/raw codec for `SpeedLimitValue` (km/h, mph, unlimited, default). |

### 4) Workflow (Server/Host and Client)
- Host edits a limit in TM:PE
  - Harmony postfix on `SetLaneSpeedLimit` detects the change and reconstructs current segment state (`TryRead`).
  - Listener sends `SpeedLimitsAppliedCommand` for the segment to all.
  - Clients apply locally (ignore scope prevents loops).

- Client edits a limit in TM:PE
  - Harmony postfix sends `SpeedLimitsUpdateRequest` with segmentId and lane entries (ordinal/signature/speed) to the server.
  - Server validates the segment, applies per-lane in TM:PE (adapter, ignore scope).
  - Server reads back resulting segment state and broadcasts `SpeedLimitsAppliedCommand` to all.
  - Everyone applies locally (including the originating client); ignore scope prevents re-triggering.

- Rejections (Server)
  - If entities are missing or TM:PE fails to apply, the server returns `RequestRejected` (with reason and entity info; segment=2).

### 5) Data Exchange (messages/fields)
| Message | Direction | Field | Type | Description |
| --- | --- | --- | --- | --- |
| `SpeedLimitsUpdateRequest` | Client → Server | `SegmentId` | `ushort` | Segment ID of the affected road. |
|  |  | `Items[]` | `Entry` | List of lane entries (see below). |
| `SpeedLimitsAppliedCommand` | Server → All | `SegmentId` | `ushort` | Segment ID of the affected road. |
|  |  | `Items[]` | `Entry` | List of lane entries (see below). |
| `RequestRejected` | Server → Client | `EntityType` | `byte` | Entity type: 2 = Segment. |
|  |  | `EntityId` | `int` | Affected entity. |
|  |  | `Reason` | `string` | Reason, e.g., `entity_missing`, `tmpe_apply_failed`. |

- `Entry` (lane entry)
  - `LaneOrdinal` (`int`): Ordinal of the lane in the segment’s `NetInfo` lanes array.
  - `Signature` (`LaneSignature`): Stabilizes identity across peers:
    - `LaneTypeRaw` (`int`) – lane’s `NetInfo.LaneType`
    - `VehicleTypeRaw` (`int`) – lane’s `VehicleInfo.VehicleType`
    - `DirectionRaw` (`int`) – lane’s `NetInfo.Direction`
  - `Speed` (`SpeedLimitValue`): encoded limit value

- `SpeedLimitValue`
  - `Type` (`SpeedLimitValueType`): `Default`, `KilometresPerHour`, `MilesPerHour`, `Unlimited`
  - `Index` (`byte`): palette index per unit (km/h, mph)
  - `RawSpeedKmh` (`float`): raw km/h for lossless channel
  - `Pending` (`bool`): flag for “still applying”, used internally

Notes
- Lane identification uses `(LaneOrdinal, LaneSignature)` within a segment only — independent of runtime `laneId`/`laneIndex`.
- The adapter sets/reads via TM:PE’s `SpeedLimitManager` (reflection), and the listener patches the `SetLaneSpeedLimit` variants.
- The ignore scope prevents local applies from re-firing listener hooks.