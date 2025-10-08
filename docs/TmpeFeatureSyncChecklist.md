# TM:PE Synchronisationsplan

Die folgenden TM:PE-Funktionen müssen laut Referenzmod **Traffic Manager: President Edition** im Multiplayer synchronisiert werden. Für jede Funktion existiert nun ein dedizierter Netzwerkbefehl, Snapshot-Export sowie eine Adapter-API zur Übergabe an TM:PE.

1. **Geschwindigkeitsbegrenzungen** – weiterhin über `SpeedLimitApplied` / `SetSpeedLimitRequest` synchronisiert. Grundlage für die übrigen Werkzeuge.【F:src/Net/Handlers/SetSpeedLimitRequestHandler.cs†L14-L55】【F:src/Tmpe/TmpeAdapter.cs†L36-L73】
2. **Spurpfeile** – linke/gerade/rechte Abbiegevorgaben je Fahrspur, inklusive Snapshot-Export und Deferred Apply.【F:src/Net/Handlers/SetLaneArrowRequestHandler.cs†L1-L74】【F:src/Snapshot/LaneArrowSnapshotProvider.cs†L1-L23】
3. **Spurverbindungen (Lane Connector)** – individuelle Spurzuweisungen zwischen Ein- und Ausfahrten.【F:src/Net/Handlers/SetLaneConnectionsRequestHandler.cs†L1-L65】【F:src/Snapshot/LaneConnectionsSnapshotProvider.cs†L1-L22】
4. **Fahrzeugbeschränkungen** – erlaubte Fahrzeugkategorien pro Fahrspur (PKW, LKW, Bus usw.).【F:src/Net/Handlers/SetVehicleRestrictionsRequestHandler.cs†L1-L53】【F:src/Net/Contracts/States/TmpeStates.cs†L16-L33】
5. **Kreuzungsbeschränkungen** – U-Turns, Spurwechsel, Blockieren, Fußgängerquerungen und Rechtsabbiegen bei Rot.【F:src/Net/Handlers/SetJunctionRestrictionsRequestHandler.cs†L1-L53】【F:src/Net/Contracts/States/TmpeStates.cs†L35-L56】
6. **Vorfahrtsschilder** – Stop, Vorfahrt beachten und Hauptstraße pro Segment-Node-Kombination.【F:src/Net/Handlers/SetPrioritySignRequestHandler.cs†L1-L53】【F:src/Tmpe/TmpeAdapter.cs†L146-L196】
7. **Parkverbote** – Parken erlauben / verbieten pro Segmentrichtung.【F:src/Net/Handlers/SetParkingRestrictionRequestHandler.cs†L1-L51】【F:src/Tmpe/TmpeAdapter.cs†L198-L241】
8. **Zeitgesteuerte Ampeln** – Aktivierung, Phasenanzahl und Zykluslänge je Knoten.【F:src/Net/Handlers/SetTimedTrafficLightRequestHandler.cs†L1-L53】【F:src/Tmpe/TmpeAdapter.cs†L243-L287】

Alle Funktionen werden zusätzlich über Deferred-Operationen abgesichert, damit verspätet geladene Netzelemente korrekt nachgezogen werden.【F:src/Net/Handlers/LaneArrowDeferredOp.cs†L1-L34】【F:src/Net/Handlers/TimedTrafficLightDeferredOp.cs†L1-L32】
