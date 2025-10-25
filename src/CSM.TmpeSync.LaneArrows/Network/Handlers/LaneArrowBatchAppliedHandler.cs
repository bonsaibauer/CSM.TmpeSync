using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    public class LaneArrowBatchAppliedHandler : CommandHandler<LaneArrowBatchApplied>
    {
        protected override void Handle(LaneArrowBatchApplied command)
        {
            if (command?.Items == null || command.Items.Count == 0)
            {
                Log.Debug(LogCategory.Synchronization, "LaneArrowBatchApplied empty | action=skip");
                return;
            }

            foreach (var item in command.Items)
            {
                if (item == null)
                    continue;

                LaneArrowAppliedHandler.ProcessEntry(
                    item.LaneId,
                    item.SegmentId,
                    item.LaneIndex,
                    item.Arrows,
                    "batch_command");
            }
        }
    }
}
