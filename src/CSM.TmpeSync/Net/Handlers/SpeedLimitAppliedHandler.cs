using CSM.API.Commands;
using CSM.TmpeSync.Net;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class SpeedLimitAppliedHandler : CommandHandler<SpeedLimitApplied>
    {
        protected override void Handle(SpeedLimitApplied cmd)
        {
            if (cmd == null)
                return;

            TransmissionDiagnostics.LogIncomingSpeedLimit(cmd.LaneId, cmd.Speed, "applied_handler");

            var laneGuid = cmd.LaneGuid;
            var segmentGuid = cmd.SegmentGuid;
            var segmentId = cmd.SegmentId != 0 ? cmd.SegmentId : laneGuid.SegmentId;
            var laneIndex = cmd.LaneIndex >= 0 ? cmd.LaneIndex : laneGuid.PrefabLaneIndex;

            if (laneGuid.IsValid)
            {
                LaneMappingStore.UpsertHostLane(laneGuid, cmd.LaneId, segmentId, laneIndex, out _, out _);
                LaneMappingStore.UpdateLocalLane(segmentId, laneIndex, cmd.LaneId);
                LaneGuidRegistry.AssignLaneGuid(cmd.LaneId, laneGuid, true);
            }

            if (segmentGuid.IsValid)
            {
                SegmentMappingStore.UpsertHostSegment(segmentGuid, segmentId, out _, out _);
                SegmentMappingStore.UpdateLocalSegment(segmentGuid, segmentId);
            }

            Log.Debug(
                LogCategory.Synchronization,
                "SpeedLimitApplied received | laneId={0} segmentId={1} laneIndex={2} laneGuid={3} value={4}",
                cmd.LaneId,
                segmentId,
                laneIndex,
                laneGuid,
                SpeedLimitCodec.Describe(cmd.Speed));
            SpeedLimitCommandProcessor.Apply(cmd.LaneId, cmd.Speed, segmentId, laneIndex, cmd.MappingVersion);
        }
    }
}
