using System.Collections.Generic;
using CSM.API.Commands;
using CSM.TmpeSync.Messages.Locks;
using CSM.TmpeSync.Services;
using Log = CSM.TmpeSync.Services.Log;

namespace CSM.TmpeSync.Handlers.Locks
{
    internal static class HostLocks
    {
        internal static readonly Dictionary<string, int> Owner = new Dictionary<string, int>();

        internal static string Key(byte kind, uint id) => kind + ":" + id;
    }

    public class BeginEditRequestHandler : CommandHandler<BeginEditRequest>
    {
        protected override void Handle(BeginEditRequest cmd)
        {
            var sender = CsmBridge.GetSenderId(cmd);
            Log.Info(
                LogCategory.Network,
                "BeginEditRequest received | targetKind={0} targetId={1} senderId={2}",
                cmd.TargetKind,
                cmd.TargetId,
                sender);

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(
                    LogCategory.Network,
                    "BeginEditRequest ignored | targetKind={0} targetId={1} reason=not_server_instance",
                    cmd.TargetKind,
                    cmd.TargetId);
                return;
            }

            var key = HostLocks.Key(cmd.TargetKind, cmd.TargetId);
            if (!HostLocks.Owner.ContainsKey(key))
            {
                HostLocks.Owner[key] = sender;
                Log.Debug(
                    LogCategory.Network,
                    "Edit lock owner assigned | key={0} owner={1}",
                    key,
                    sender);
            }
            else
            {
                Log.Debug(
                    LogCategory.Network,
                    "Edit lock refreshed | key={0} owner={1}",
                    key,
                    HostLocks.Owner[key]);
            }

            CsmBridge.SendToAll(
                new EditLockApplied
                {
                    TargetKind = cmd.TargetKind,
                    TargetId = cmd.TargetId,
                    OwnerClientId = HostLocks.Owner[key],
                    TtlFrames = 180
                });
        }
    }

    public class EndEditRequestHandler : CommandHandler<EndEditRequest>
    {
        protected override void Handle(EndEditRequest cmd)
        {
            var sender = CsmBridge.GetSenderId(cmd);
            Log.Info(
                LogCategory.Network,
                "EndEditRequest received | targetKind={0} targetId={1} senderId={2}",
                cmd.TargetKind,
                cmd.TargetId,
                sender);

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(
                    LogCategory.Network,
                    "EndEditRequest ignored | targetKind={0} targetId={1} reason=not_server_instance",
                    cmd.TargetKind,
                    cmd.TargetId);
                return;
            }

            var key = HostLocks.Key(cmd.TargetKind, cmd.TargetId);
            if (HostLocks.Owner.Remove(key))
            {
                Log.Debug(
                    LogCategory.Network,
                    "Edit lock cleared | key={0} previousOwner={1}",
                    key,
                    sender);
            }
            else
            {
                Log.Warn(
                    LogCategory.Network,
                    "EndEditRequest missing lock | key={0}",
                    key);
            }

            CsmBridge.SendToAll(
                new EditLockCleared
                {
                    TargetKind = cmd.TargetKind,
                    TargetId = cmd.TargetId
                });
        }
    }
}
