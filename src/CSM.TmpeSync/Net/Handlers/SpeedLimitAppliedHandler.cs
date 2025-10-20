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

            Log.Debug(LogCategory.Synchronization, "SpeedLimitApplied received | laneId={0} speedKmh={1}", cmd.LaneId, cmd.SpeedKmh);
            SpeedLimitCommandProcessor.Apply(cmd.LaneId, cmd.SpeedKmh);
        }
    }
}
