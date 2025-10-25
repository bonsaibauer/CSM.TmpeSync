using System.Collections.Generic;
using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Locks;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;
using CSM.TmpeSync.Bridge;

namespace CSM.TmpeSync.Network.Handlers.Locks
{
    internal static class HostLocks {
        internal static readonly Dictionary<string,int> Owner=new Dictionary<string,int>();
        internal static string Key(byte k,uint id)=>k+":"+id;
    }

    public class BeginEditRequestHandler : CommandHandler<BeginEditRequest>
    {
        protected override void Handle(BeginEditRequest cmd){
            var sender=CsmBridge.GetSenderId(cmd);
            Log.Info("Received BeginEditRequest kind={0} id={1} from client={2}", cmd.TargetKind, cmd.TargetId, sender);
            if (!CsmBridge.IsServerInstance()){
                Log.Debug("Ignoring BeginEditRequest on non-server instance.");
                return;
            }
            var key=HostLocks.Key(cmd.TargetKind,cmd.TargetId);
            if (!HostLocks.Owner.ContainsKey(key)){
                HostLocks.Owner[key]=sender;
                Log.Debug("Assigned new edit lock owner client={0} for key={1}", sender, key);
            }
            else{
                Log.Debug("Refreshing edit lock for key={0}; owner remains client={1}", key, HostLocks.Owner[key]);
            }
            CsmBridge.SendToAll(new EditLockApplied{ TargetKind=cmd.TargetKind, TargetId=cmd.TargetId, OwnerClientId=HostLocks.Owner[key], TtlFrames=180 });
        }
    }

    public class EndEditRequestHandler : CommandHandler<EndEditRequest>
    {
        protected override void Handle(EndEditRequest cmd){
            var sender=CsmBridge.GetSenderId(cmd);
            Log.Info("Received EndEditRequest kind={0} id={1} from client={2}", cmd.TargetKind, cmd.TargetId, sender);
            if (!CsmBridge.IsServerInstance()){
                Log.Debug("Ignoring EndEditRequest on non-server instance.");
                return;
            }
            var key=HostLocks.Key(cmd.TargetKind,cmd.TargetId);
            if(HostLocks.Owner.Remove(key))
                Log.Debug("Cleared edit lock key={0} previously owned by client={1}", key, sender);
            else
                Log.Warn("EndEditRequest for key={0} had no existing lock", key);
            CsmBridge.SendToAll(new EditLockCleared{ TargetKind=cmd.TargetKind, TargetId=cmd.TargetId });
        }
    }
}
