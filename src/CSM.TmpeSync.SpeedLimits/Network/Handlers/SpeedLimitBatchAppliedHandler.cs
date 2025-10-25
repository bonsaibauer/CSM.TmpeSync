using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Util;
using CSM.TmpeSync.CsmBridge;

namespace CSM.TmpeSync.Network.Handlers
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
                CsmBridge.GetSenderId(command));

            foreach (var item in command.Items)
            {
                var effectiveVersion = item.MappingVersion > 0 ? item.MappingVersion : command.MappingVersion;
                SpeedLimitCommandProcessor.Apply(
                    item.LaneId,
                    item.Speed,
                    item.SegmentId,
                    item.LaneIndex,
                    effectiveVersion);
            }
        }
    }
}
