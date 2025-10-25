# Lane GUID Mapping in CSM.TmpeSync

This document explains how CSM.TmpeSync manages its own lane GUIDs to map lanes deterministically
between host and client. The implementation no longer depends on the TM:PE runtime API and instead
watches the lifecycle of net segments and lanes directly.

## Overview

* **Goal:** Replace the volatile runtime lane IDs from Cities: Skylines with stable GUIDs. Every
  GUID encodes the segment, prefab, lane slot, and a monotonically increasing sequence number.
* **Core service:** `LaneGuidRegistry` creates, stores, and validates GUIDs. The host generates
  them automatically, while clients accept only the values transmitted by the host. The registry
  uses lightweight dictionary-based storage so it only grows for lanes that actually exist.
* **Storage:** `LaneMappingStore` records, per segment and lane index, the host lane identifier, the
  local lane ID, and the associated `LaneGuid`.
* **Transport:** `LaneMappingBatch` and `LaneMappingChanged` include every GUID component
  (`SegmentId`, `SegmentBuildIndex`, `PrefabId`, `PrefabLaneIndex`, `Sequence`).

Runtime lane IDs now only appear as a technical detail inside the protocol. All actual resolution is
performed through GUIDs.

## Host sequence

1. `LaneMappingTracker.Initialize()` clears the `LaneMappingStore` and `LaneGuidRegistry`, enables
   automatic GUID generation, and resets the build index cache.
2. `SyncAllSegments` calls `SyncSegmentInternal` for each segment. When necessary,
   `LaneGuidRegistry.GetOrCreateLaneGuid(laneId)` creates new GUIDs, and
   `LaneMappingStore.UpsertHostLane(...)` records the current host state.
3. `LaneMappingTracker` observes the build indices of every known segment. When a segment is created
   or its build index changes, `DetectSegmentLifecycle()` automatically triggers another
   `SyncSegment`, causing the host to broadcast the updated GUIDs immediately.
4. Every successful scan invokes `LaneGuidRegistry.AssignLaneGuid(laneId, laneGuid, overwrite: true)`
   so registry and store remain in sync.
5. When a segment is removed or a lane disappears, `LaneGuidRegistry.HandleSegmentReleased(...)`
   and `HandleLaneReleased(...)` purge the cached entries. In addition,
   `LaneAssignmentRetryBuffer.RemoveForSegment(...)` drops any pending assignments for the segment.

## Client sequence

1. When switching to the client role, `LaneMappingTracker` disables automatic generation
   (`LaneGuidRegistry.SetAutomaticGeneration(false)`) and clears the registry and build index cache.
2. Upon receiving a snapshot, `LaneMappingStore.ApplyRemoteSnapshot` ingests the transmitted
   entries. `PendingMap.ResolveLaneMapping(...)` then tries to translate each GUID into a
   local lane ID via `LaneGuidRegistry.TryResolveLane(...)`.
3. If resolution succeeds, the client updates the store (`LaneMappingStore.UpdateLocalLane`) and
   calls `LaneGuidRegistry.AssignLaneGuid(...)` so future requests can be served directly from the
   registry cache.
4. If resolution fails due to spawn races or pending segments, the `LaneAssignmentRetryBuffer`
   holds on to the GUID and retries with exponential backoff until the lane becomes available.
5. Delta updates (`LaneMappingChanged`) follow the same path. Successful resolutions also trigger an
   immediate retry round for the affected segment.

## Client-originated changes

When a client places a road or applies a TM:PE feature such as a speed limit, the client still sends
its request to the host without relying on a locally invented lane ID:

1. The feature handler (for example, the speed limit tool) calls `NetUtil.TryResolveLane` for the
   target lane. The resolver uses GUIDs received from the host; if the GUID is not yet available, the
   request is delayed or retried.
2. Once the GUID-to-lane mapping succeeds, the client packages the action with that GUID and sends
   it to the host. The host looks up the authoritative runtime lane ID from its registry, applies the
   change, and rebroadcasts the resulting state to all clients.
3. Clients that initiated the action receive the broadcast like any other update. Because the
   mapping is driven by GUIDs on both sides, the final state is consistent even if runtime lane IDs
   changed in the meantime.

This means lane GUIDs remain authoritative regardless of where the request originated—clients cannot
apply TM:PE changes until the host has published the corresponding GUID.

## Runtime resolution

`NetUtil.TryResolveLane` is the central entry point for every TM:PE operation that needs a lane:

1. `TryGetLaneGuidContext` derives the relevant `LaneGuid` from `LaneMappingStore` or directly from
   the registry, using lane ID, segment, and lane index as hints when available.
2. `LaneGuidRegistry.TryResolveLane` attempts to translate the GUID to an active lane ID. When it
   succeeds, store and registry are updated so the next lookup works without any extra effort.
3. If the call fails, the lane is treated as (temporarily) unavailable. There are no heuristic
   fallbacks via segment or lane index—every assignment runs exclusively through GUIDs.

`LaneAssignmentRetryBuffer` ensures GUIDs for yet-to-spawn lanes are retried until they can be
resolved.

## Weaknesses and improvement ideas

* **Retry window:** `LaneAssignmentRetryBuffer` aborts after a fixed number of attempts. An adaptive
  limit or an additional lifecycle signal could make late spawns even more robust.
* **Sequence progression after full reset:** When `LaneGuidRegistry.Rebuild()` is invoked manually,
  sequences per slot restart at 1. Persisting sequences across sessions could be added as an optional
  extension.
* **Lifecycle events without segment IDs:** Some mods might modify lanes without bumping the build
  index (for example, direct lane reassignment). Adding hooks for lane-specific events could detect
  such edge cases sooner.

## Debugging hints

* `LaneMappingStore.Snapshot()` returns a snapshot of the current mapping at any time.
* The registry logs build-index mismatches and invalid assignments under
  `LogCategory.Synchronization`.
* `LaneGuidRegistry.TryGetLaneGuid(...)` allows diagnostic tools to inspect existing assignments and
  show them in UI panels.
