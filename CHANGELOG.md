# Changelog - CSM.TmpeSync

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
