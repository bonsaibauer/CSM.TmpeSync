using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Locks;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers.Locks
{
    public class EditLockAppliedHandler : CommandHandler<EditLockApplied>
    {
        protected override void Handle(EditLockApplied cmd){ LockRegistry.Apply(cmd.TargetKind, cmd.TargetId, cmd.OwnerClientId, cmd.TtlFrames); }
    }
}
