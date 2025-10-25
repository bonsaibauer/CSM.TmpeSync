using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.States;
using ProtoBuf;

namespace CSM.TmpeSync.Network.Contracts.Requests
{
    [ProtoContract]
    public class SetJunctionRestrictionsRequest : CommandBase
    {
        [ProtoMember(1)] public ushort NodeId { get; set; }
        [ProtoMember(2)] public JunctionRestrictionsState State { get; set; }
    }
}
