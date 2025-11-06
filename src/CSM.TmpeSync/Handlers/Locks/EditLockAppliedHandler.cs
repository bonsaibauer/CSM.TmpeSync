using CSM.API.Commands;
using CSM.TmpeSync.Messages.Locks;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.Handlers.Locks
{
    public class EditLockAppliedHandler : CommandHandler<EditLockApplied>
    {
        protected override void Handle(EditLockApplied cmd)
        {
            Log.Info(
                LogCategory.Network,
                "EditLockApplied received | targetKind={0} targetId={1} ownerId={2} ttl={3}",
                cmd.TargetKind,
                cmd.TargetId,
                cmd.OwnerClientId,
                cmd.TtlFrames);
            LockRegistry.Apply(cmd.TargetKind, cmd.TargetId, cmd.OwnerClientId, cmd.TtlFrames);
        }
    }
}
