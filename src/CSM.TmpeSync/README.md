# CSM TM:PE Sync – Core Module / Kernmodul

This document is bilingual. German (DE) first, English (EN) follows.

---

## DE - Deutsch

### 1) Kurzbeschreibung
- Gemeinsames Kernmodul für alle TM:PE-Sync-Features (z. B. Vorfahrtsschilder, Geschwindigkeitsbegrenzungen, Parkbeschränkungen, Lane Connector, Ampeln, Knotenbeschränkungen, Clear Traffic).
- Stellt gemeinsame Dienste bereit: Logging, Netzwerk-Brücke (CSM), Entitäts-Locks, Utility- und Reflection-Helfer.
- Lädt/registriert die einzelnen Feature-Assemblys und aktiviert deren Harmony-Listener.

### 2) Dateistruktur
- `src/CSM.TmpeSync/`
  - `Mod/` – Mod-Bootstrap und CSM-Integration.
    - `MyUserMod.cs` – Eintragspunkt für Cities: Skylines (IUserMod, Einstellungen).
    - `FeatureBootstrapper.cs` – Registriert alle TM:PE-Sync-Features und aktiviert deren Listener.
    - `TmpeSyncConnection.cs` – Bindet die CSM-Command-Assemblies ein und initialisiert die Netzwerkseite.
    - `MultiplayerThreadingExtension.cs` – Reagiert auf Rollenwechsel/Mehrspieler-Threading.
  - `Services/` – Gemeinsame Infrastruktur.
    - `Log.cs` – Tag-basierte, tagesdatierte Datei-Logs (Client/Server).
    - `CsmBridge.cs` – Brücke zu CSM (`SendToAll/Server/Client`, Ignore-Scope, Rollenbeschreibung).
    - `NetworkUtil.cs` – Prüf-/Helper für Existenz/Ortsauflösung von Netz-Entitäten (Node/Segment/Lane) inkl. Simulation-Dispatch.
    - `EntityLocks.cs`, `LockRegistry.cs` – Sperren für Nodes/Segmente/Lanes zur konfliktfreien Anwendung auf dem Host.
    - `Deps.cs` – Dynamisches Laden/Prüfen von Abhängigkeiten (Harmony, CSM, TM:PE).
  - `Messages/` – Gemeinsame System-Nachrichten.
    - `System/RequestRejected.cs` – Serverseitige Ablehnung inkl. Grund und Entität.

### 3) Dateiübersicht
| Datei | Zweck |
| --- | --- |
| `Mod/MyUserMod.cs` | Mod-Metadaten und Aktivierung im Spiel. |
| `Mod/FeatureBootstrapper.cs` | Aktiviert alle Feature-Listener (Harmony) und initialisiert Sync. |
| `Mod/TmpeSyncConnection.cs` | Registriert die CSM-Command-Assemblies für die Netzwerkkommunikation. |
| `Services/Log.cs` | Datei-Logging mit Kategorien (Lifecycle, Network, Synchronization, Diagnostics …). |
| `Services/CsmBridge.cs` | Senden/Empfangen via CSM, Rollenlogik und Ignore-Scope. |
| `Services/NetworkUtil.cs` | Entitäts-Helfer: Exists/Locate, Simulation-Dispatch. |
| `Services/EntityLocks.cs` | Sperr-Scopes auf Host-Seite zur konsistenten Anwendung. |
| `Messages/System/RequestRejected.cs` | Standardisierte Ablehnung von Anfragen. |

### 4) Feature-Übersicht (Verlinkt)
- `src/CSM.TmpeSync.PrioritySigns/` – Vorfahrtsschilder
- `src/CSM.TmpeSync.SpeedLimits/` – Geschwindigkeitsbegrenzungen
- `src/CSM.TmpeSync.ParkingRestrictions/` – Parkbeschränkungen
- `src/CSM.TmpeSync.LaneConnector/` – Lane Connector
- `src/CSM.TmpeSync.LaneArrows/` – Fahrpfeile
- `src/CSM.TmpeSync.ToggleTrafficLights/` – Ampeln toggeln
- `src/CSM.TmpeSync.JunctionRestrictions/` – Knotenbeschränkungen
- `src/CSM.TmpeSync.ClearTraffic/` – Clear Traffic

### 5) Ablauf (hochlevel)
- Beim Start registriert `FeatureBootstrapper` alle Feature-Listener. Jede lokale TM:PE-Änderung löst einen Harmony-Postfix aus (Client: sendet Request; Host: broadcastet angewandten Zustand).
- Der Host führt die Änderung im Simulationsthread aus, liest den resultierenden Zustand und broadcastet diesen an alle.
- Clients wenden den Zustand lokal an; lokale Apply-Scopes verhindern Echo-Schleifen.

Hinweise
- Logs liegen unter `%LOCALAPPDATA%/Colossal Order/Cities_Skylines/CSM.TmpeSync/<client|server>-YYYY-MM-DD.log`.
- Kategorien nutzbar für Filterung (z. B. Diagnostics für Detail-Logs).

---

## EN - English

### 1) Summary
- Core module for TM:PE synchronization features (Priority Signs, Speed Limits, Parking Restrictions, Lane Connector, Lane Arrows, Toggle Traffic Lights, Junction Restrictions, Clear Traffic).
- Provides shared services: logging, CSM bridge, entity locks, utilities and reflection helpers.
- Loads/registers feature assemblies and enables their Harmony listeners.

### 2) Directory Layout
- `src/CSM.TmpeSync/`
  - `Mod/` – mod bootstrap and CSM integration.
  - `Services/` – shared infrastructure (logging, bridge, utilities, locks).
  - `Messages/` – shared system messages.

### 3) File Overview
| File | Purpose |
| --- | --- |
| `Mod/MyUserMod.cs` | Mod metadata and activation in-game. |
| `Mod/FeatureBootstrapper.cs` | Enables all feature listeners (Harmony) and initializes sync. |
| `Mod/TmpeSyncConnection.cs` | Registers CSM command assemblies for networking. |
| `Services/Log.cs` | File logging with categories (Lifecycle, Network, Synchronization, Diagnostics …). |
| `Services/CsmBridge.cs` | CSM bridge, role logic, ignore scope. |
| `Services/NetworkUtil.cs` | Entity helpers: Exists/Locate, simulation dispatch. |
| `Services/EntityLocks.cs` | Lock scopes on host to apply changes consistently. |
| `Messages/System/RequestRejected.cs` | Standardized request rejection. |

### 4) Features (linked)
- See subfolders named `src/CSM.TmpeSync.*` for dedicated feature docs.

### 5) Flow (high-level)
- `FeatureBootstrapper` registers listeners. Local TM:PE changes trigger postfix hooks (client: sends request; host: broadcasts applied state).
- Host applies in simulation, reads back effective state, broadcasts to all.
- Clients apply locally; local ignore scopes prevent echo loops.

Notes
- Logs are under `%LOCALAPPDATA%/Colossal Order/Cities_Skylines/CSM.TmpeSync/…`.
- Use Diagnostics category for detailed troubleshooting (e.g., Speed Limits readback).

