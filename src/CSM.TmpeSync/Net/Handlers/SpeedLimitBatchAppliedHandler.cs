using CSM.API.Commands;
using CSM.TmpeSync.Net;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class SpeedLimitBatchAppliedHandler : CommandHandler<SpeedLimitBatchApplied>
    {
        protected override void Handle(SpeedLimitBatchApplied command)
        {
            if (command?.Items == null || command.Items.Count == 0)
            {
                Log.Debug(LogCategory.Synchronization, "Speed-limit batch empty | action=skip");
                return;
            }

            Log.Info(
                LogCategory.Synchronization,
                "SpeedLimitBatchApplied received | count={0} from={1}",
                command.Items.Count,
                CsmCompat.GetSenderId(command));

            foreach (var item in command.Items)
            {
                var effectiveVersion = item.MappingVersion > 0 ? item.MappingVersion : command.MappingVersion;
                var laneGuid = item.LaneGuid;
                var segmentGuid = item.SegmentGuid;
                var segmentId = item.SegmentId != 0 ? item.SegmentId : laneGuid.SegmentId;
                var laneIndex = item.LaneIndex >= 0 ? item.LaneIndex : laneGuid.PrefabLaneIndex;

                if (laneGuid.IsValid)
                {
                    LaneMappingStore.UpsertHostLane(laneGuid, item.LaneId, segmentId, laneIndex, out _, out _);
                    LaneMappingStore.UpdateLocalLane(segmentId, laneIndex, item.LaneId);
                    LaneGuidRegistry.AssignLaneGuid(item.LaneId, laneGuid, true);
                }

                if (segmentGuid.IsValid)
                {
                    SegmentMappingStore.UpsertHostSegment(segmentGuid, segmentId, out _, out _);
                    SegmentMappingStore.UpdateLocalSegment(segmentGuid, segmentId);
                }

                SpeedLimitCommandProcessor.Apply(
                    item.LaneId,
                    item.Speed,
                    segmentId,
                    laneIndex,
                    effectiveVersion);
            }
        }
    }
}
