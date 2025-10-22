using System.Collections.Generic;
using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Mapping;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class LaneMappingBatchHandler : CommandHandler<LaneMappingBatch>
    {
        protected override void Handle(LaneMappingBatch command)
        {
            if (command?.Entries == null || command.Entries.Count == 0)
                return;

            if (command.IsFullSnapshot)
            {
                var snapshotEntries = new List<LaneMappingStore.Entry>(command.Entries.Count);
                foreach (var entry in command.Entries)
                {
                    snapshotEntries.Add(new LaneMappingStore.Entry
                    {
                        SegmentId = entry.SegmentId,
                        LaneIndex = entry.LaneIndex,
                        HostLaneId = entry.HostLaneId,
                        LocalLaneId = 0,
                        IsLocalResolved = false
                    });
                }

                if (!LaneMappingStore.ApplyRemoteSnapshot(snapshotEntries, command.Version))
                {
                    Log.Debug(LogCategory.Synchronization, "Ignoring stale lane mapping snapshot | version={0}", command.Version);
                    return;
                }

                foreach (var entry in command.Entries)
                    ResolveLocalLane(entry.SegmentId, entry.LaneIndex, entry.HostLaneId);

                Log.Info(
                    LogCategory.Synchronization,
                    "Lane mapping snapshot imported | count={0} version={1}",
                    command.Entries.Count,
                    command.Version);
                return;
            }

            foreach (var entry in command.Entries)
            {
                if (!LaneMappingStore.ApplyRemoteChange(command.Version, entry.HostLaneId, entry.SegmentId, entry.LaneIndex))
                {
                    Log.Debug(LogCategory.Synchronization, "Ignoring stale lane mapping change | segment={0} laneIndex={1} version={2}", entry.SegmentId, entry.LaneIndex, command.Version);
                    continue;
                }

                ResolveLocalLane(entry.SegmentId, entry.LaneIndex, entry.HostLaneId);
            }
        }

        internal static void ResolveLocalLane(ushort segmentId, int laneIndex, uint hostLaneId)
        {
            if (segmentId == 0 || laneIndex < 0)
                return;

            if (NetUtil.TryGetLaneId(segmentId, laneIndex, out var localLaneId))
                LaneMappingStore.UpdateLocalLane(segmentId, laneIndex, localLaneId);
        }
    }
}
