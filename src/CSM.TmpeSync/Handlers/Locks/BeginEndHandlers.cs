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
                "[EditLocks] Begin request received | targetKind={0} targetId={1} senderId={2}.",
                cmd.TargetKind,
                cmd.TargetId,
                sender);

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(
                    LogCategory.Network,
                    "[EditLocks] Begin request ignored | targetKind={0} targetId={1} reason=not_server_instance.",
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
                    "[EditLocks] Owner assigned | key={0} owner={1}.",
                    key,
                    sender);
            }
            else
            {
                Log.Debug(
                    LogCategory.Network,
                    "[EditLocks] Owner refreshed | key={0} owner={1}.",
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
                "[EditLocks] End request received | targetKind={0} targetId={1} senderId={2}.",
                cmd.TargetKind,
                cmd.TargetId,
                sender);

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(
                    LogCategory.Network,
                    "[EditLocks] End request ignored | targetKind={0} targetId={1} reason=not_server_instance.",
                    cmd.TargetKind,
                    cmd.TargetId);
                return;
            }

            var key = HostLocks.Key(cmd.TargetKind, cmd.TargetId);
            if (HostLocks.Owner.Remove(key))
            {
                Log.Debug(
                    LogCategory.Network,
                    "[EditLocks] Cleared | key={0} previousOwner={1}.",
                    key,
                    sender);
            }
            else
            {
                Log.Warn(
                    LogCategory.Network,
                    "[EditLocks] End request ignored | reason=missing_lock key={0}.",
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
