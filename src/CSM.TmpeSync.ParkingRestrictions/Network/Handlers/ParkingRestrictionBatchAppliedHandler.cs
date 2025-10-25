using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    public class ParkingRestrictionBatchAppliedHandler : CommandHandler<ParkingRestrictionBatchApplied>
    {
        protected override void Handle(ParkingRestrictionBatchApplied command)
        {
            if (command?.Items == null || command.Items.Count == 0)
            {
                Log.Debug(LogCategory.Synchronization, "ParkingRestrictionBatchApplied empty | action=skip");
                return;
            }

            foreach (var item in command.Items)
            {
                if (item == null)
                    continue;

                ParkingRestrictionAppliedHandler.ProcessEntry(
                    item.SegmentId,
                    item.State ?? new ParkingRestrictionState(),
                    "batch_command");
            }
        }
    }
}
