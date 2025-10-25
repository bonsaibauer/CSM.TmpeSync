using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    public class PrioritySignBatchAppliedHandler : CommandHandler<PrioritySignBatchApplied>
    {
        protected override void Handle(PrioritySignBatchApplied command)
        {
            if (command?.Items == null || command.Items.Count == 0)
            {
                Log.Debug(LogCategory.Synchronization, "PrioritySignBatchApplied empty | action=skip");
                return;
            }

            foreach (var item in command.Items)
            {
                if (item == null)
                    continue;

                PrioritySignAppliedHandler.ProcessEntry(
                    item.NodeId,
                    item.SegmentId,
                    item.SignType,
                    "batch_command");
            }
        }
    }
}
