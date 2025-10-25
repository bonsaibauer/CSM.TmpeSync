using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    public class LaneConnectionsBatchAppliedHandler : CommandHandler<LaneConnectionsBatchApplied>
    {
        protected override void Handle(LaneConnectionsBatchApplied command)
        {
            if (command?.Items == null || command.Items.Count == 0)
            {
                Log.Debug(LogCategory.Synchronization, "LaneConnectionsBatchApplied empty | action=skip");
                return;
            }

            foreach (var item in command.Items)
            {
                if (item == null)
                    continue;

                LaneConnectionsAppliedHandler.ProcessEntry(
                    item.SourceLaneId,
                    item.SourceSegmentId,
                    item.SourceLaneIndex,
                    item.TargetLaneIds,
                    item.TargetSegmentIds,
                    item.TargetLaneIndexes,
                    "batch_command");
            }
        }
    }
}
