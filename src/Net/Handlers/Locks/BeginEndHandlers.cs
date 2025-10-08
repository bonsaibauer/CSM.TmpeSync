using System.Collections.Generic;
using CSM.API;
using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Locks;

namespace CSM.TmpeSync.Net.Handlers.Locks
{
    internal static class HostLocks {
        internal static readonly Dictionary<string,int> Owner=new Dictionary<string,int>();
        internal static string Key(byte k,uint id)=>k+":"+id;
    }

    public class BeginEditRequestHandler : CommandHandler<BeginEditRequest>
    {
        protected override void Handle(BeginEditRequest cmd){
            if (Command.CurrentRole!=MultiplayerRole.Server) return;
            var key=HostLocks.Key(cmd.TargetKind,cmd.TargetId);
            if (!HostLocks.Owner.ContainsKey(key)) HostLocks.Owner[key]=Command.SenderId;
            Command.SendToAll(new EditLockApplied{ TargetKind=cmd.TargetKind, TargetId=cmd.TargetId, OwnerClientId=HostLocks.Owner[key], TtlFrames=180 });
        }
    }

    public class EndEditRequestHandler : CommandHandler<EndEditRequest>
    {
        protected override void Handle(EndEditRequest cmd){
            if (Command.CurrentRole!=MultiplayerRole.Server) return;
            HostLocks.Owner.Remove(HostLocks.Key(cmd.TargetKind,cmd.TargetId));
            Command.SendToAll(new EditLockCleared{ TargetKind=cmd.TargetKind, TargetId=cmd.TargetId });
        }
    }
}
