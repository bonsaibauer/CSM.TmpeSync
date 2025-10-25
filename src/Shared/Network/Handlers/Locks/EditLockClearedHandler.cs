using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Locks;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers.Locks
{
    public class EditLockClearedHandler : CommandHandler<EditLockCleared>
    {
        protected override void Handle(EditLockCleared cmd)
        {
            Log.Info(
                LogCategory.Network,
                "EditLockCleared received | targetKind={0} targetId={1}",
                cmd.TargetKind,
                cmd.TargetId);
            LockRegistry.Clear(cmd.TargetKind, cmd.TargetId);
        }
    }
}
