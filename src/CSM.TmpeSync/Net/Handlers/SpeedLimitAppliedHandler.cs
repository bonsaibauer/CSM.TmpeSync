using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class SpeedLimitAppliedHandler : CommandHandler<SpeedLimitApplied>
    {
        protected override void Handle(SpeedLimitApplied cmd)
        {
            if (cmd == null)
                return;

            Log.Debug(
                LogCategory.Synchronization,
                "SpeedLimitApplied received | laneId={0} segmentId={1} laneIndex={2} speedKmh={3}",
                cmd.LaneId,
                cmd.SegmentId,
                cmd.LaneIndex,
                cmd.SpeedKmh);
            SpeedLimitCommandProcessor.Apply(cmd.LaneId, cmd.SpeedKmh, cmd.SegmentId, cmd.LaneIndex, cmd.MappingVersion);
        }
    }
}
