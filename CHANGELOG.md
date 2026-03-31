# Changelog - CSM.TmpeSync

In-game changelog source: `src/CSM.TmpeSync/Services/UI/ChangelogPanel.cs` (`ChangelogService.LoadEntries()`).

## [1.2.0.0] - 2026-03-31

### Compatibility & Health Check
- **Compatibility management rework**: Added a dedicated Health Check flow in Mod Options with live Host/Client and dependency diagnostics.
- **Cities: Skylines compatibility validation**: Added explicit CS version-line checks and integrated them into diagnostics and notifier flows.
- **Runtime UX behavior**: Improved Host/Client session handling and status rendering in Mod Options and popup panels.

### UI & Messaging
- **Modern UI rollout**: Unified TMPE-style modal cards, badges, and typography across version check, dependency check, and changelog windows.

### Tooling & Metadata
- **Tooling**: Standardized script logging and improved update/build output consistency for build, debug, install, and update flows.
- **Metadata refresh**: Synchronized mod/dependency release tags and compatibility references for TMPE, CSM, Harmony, and CSM.TmpeSync.

## [1.1.1.0] - 2026-03-24

### Updated Compatibility
- Updated `ModMetadata.cs` to support:
    - Cities: Skylines 1.21 (latest)
    - Cities: Skylines Multiplayer (CSM) v2603.307
    - Traffic Manager: President Edition (TM:PE) 11.9.4.1

### Improvements & Robustness
- **Network Resilience**: Added defensive bounds checks in `NetworkUtil.cs` to prevent "Array index is out of range" exceptions when accessing game buffers (Nodes, Segments, Lanes).
- **Reflection Logic**: Enhanced `CsmBridge.cs` with assembly-aware type resolution. This ensures that internal CSM types in `csm.dll` are correctly resolved even when the mod is loaded in complex environments.
- **Harmony Patching**: Refined `LaneConnectorEventListener.cs` to use more granular method signature matching. Added extra logging to capture failures when patching TM:PE managers.

### Bug Fixes
- Fixed a potential issue where "Move Building" (via CSM's new rebuild handler) could be interfered with by outdated mod version states.
- Aligned synchronization handlers with the latest TM:PE internal manager signatures to ensure stability during lane connection changes.

## [1.1.0.0] - 2025-12-06

- [Updated] Improve resync logic: when a client rejoins, the host replays all TM:PE changes made since the host came online, including those performed while the client was offline.
- [Updated] Includes the 1.0.1.0 updates: in-game changelog popup and client lane connection fix.

## [1.0.1.0] - 2025-12-04

- [New] Add minimal in-game changelog popup.
- [Fixed] Fix lane connection handling for clients.

## [1.0.0.0] - 2025-11-06

- [New] Host-authoritative bridge between CSM and TM:PE with retry/backoff so every state stays in sync.
- [New] Supports Clear Traffic, Junction Restrictions, Lane Arrows, Lane Connector, Parking Restrictions, Priority Signs, Speed Limits, Toggle Traffic Lights, and Vehicle Restrictions.
- [Removed] Timed traffic lights remain disabled as synchronizing them would generate disproportionate multiplayer traffic.
- [New] Modular per-feature architecture with dedicated logging, guard scopes, and explicit client error feedback.
