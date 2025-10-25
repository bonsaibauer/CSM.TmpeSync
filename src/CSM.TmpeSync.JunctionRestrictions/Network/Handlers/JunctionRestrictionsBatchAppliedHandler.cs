using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    public class JunctionRestrictionsBatchAppliedHandler : CommandHandler<JunctionRestrictionsBatchApplied>
    {
        protected override void Handle(JunctionRestrictionsBatchApplied command)
        {
            if (command?.Items == null || command.Items.Count == 0)
            {
                Log.Debug(LogCategory.Synchronization, "JunctionRestrictionsBatchApplied empty | action=skip");
                return;
            }

            foreach (var item in command.Items)
            {
                if (item == null)
                    continue;

                JunctionRestrictionsAppliedHandler.ProcessEntry(
                    item.NodeId,
                    item.State ?? new JunctionRestrictionsState(),
                    "batch_command");
            }
        }
    }
}
