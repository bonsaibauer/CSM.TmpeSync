using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Locks;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers.Locks
{
    public class EditLockClearedHandler : CommandHandler<EditLockCleared>
    {
        protected override void Handle(EditLockCleared cmd){ LockRegistry.Clear(cmd.TargetKind, cmd.TargetId); }
    }
}
