using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.States;
using ProtoBuf;

namespace CSM.TmpeSync.Net.Contracts.Applied
{
    [ProtoContract]
    public class JunctionRestrictionsApplied : CommandBase
    {
        [ProtoMember(1)] public ushort NodeId { get; set; }
        [ProtoMember(2)] public JunctionRestrictionsState State { get; set; }
    }
}
