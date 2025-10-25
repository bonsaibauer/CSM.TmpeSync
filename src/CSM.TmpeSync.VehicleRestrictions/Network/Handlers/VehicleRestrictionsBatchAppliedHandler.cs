using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    public class VehicleRestrictionsBatchAppliedHandler : CommandHandler<VehicleRestrictionsBatchApplied>
    {
        protected override void Handle(VehicleRestrictionsBatchApplied command)
        {
            if (command?.Items == null || command.Items.Count == 0)
            {
                Log.Debug(LogCategory.Synchronization, "VehicleRestrictionsBatchApplied empty | action=skip");
                return;
            }

            foreach (var item in command.Items)
            {
                if (item == null)
                    continue;

                VehicleRestrictionsAppliedHandler.ProcessEntry(
                    item.LaneId,
                    item.SegmentId,
                    item.LaneIndex,
                    item.Restrictions,
                    "batch_command");
            }
        }
    }
}
