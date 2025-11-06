using CSM.API.Commands;
using CSM.TmpeSync.Messages.Locks;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.Handlers.Locks
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
