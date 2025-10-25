using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.SpeedLimits.Util;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    public class SpeedLimitAppliedHandler : CommandHandler<SpeedLimitApplied>
    {
        protected override void Handle(SpeedLimitApplied cmd)
        {
            if (cmd == null)
                return;

            SpeedLimitDiagnostics.LogIncomingSpeedLimit(cmd.LaneId, cmd.Speed, "applied_handler");

            Log.Debug(
                LogCategory.Synchronization,
                "SpeedLimitApplied received | laneId={0} segmentId={1} laneIndex={2} value={3}",
                cmd.LaneId,
                cmd.SegmentId,
                cmd.LaneIndex,
                SpeedLimitCodec.Describe(cmd.Speed));
            SpeedLimitCommandProcessor.Apply(cmd.LaneId, cmd.Speed, cmd.SegmentId, cmd.LaneIndex, cmd.MappingVersion);
        }
    }
}
