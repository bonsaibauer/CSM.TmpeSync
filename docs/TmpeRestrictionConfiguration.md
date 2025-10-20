# TM:PE Restriction Configuration

CSM TM:PE Sync adopts the feature matrix that TM:PE exposes at runtime. When a host enables a TM:PE feature, the mod keeps the corresponding tools available for every player in the session and synchronises their state through the CSM channel.

- TM:PE tools remain active in multiplayer; users can interact with the same UI they expect in single-player.
- Authoritative changes are applied on the host, converted through the bridge in `src/CSM.TmpeSync/Tmpe`, and broadcast to all clients.
- Developers customise behaviour by extending those bridge helpers, using TM:PE’s public API whenever it is available.

This configuration model keeps behaviour deterministic: TM:PE decides what features exist, and the sync layer makes sure every participant observes the same result.
