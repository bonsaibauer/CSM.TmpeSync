using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
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
                SpeedLimitCommandProcessor.Apply(item.LaneId, item.SpeedKmh);
            }
        }
    }
}
