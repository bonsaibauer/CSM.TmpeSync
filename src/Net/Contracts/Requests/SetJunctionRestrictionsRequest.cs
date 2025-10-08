using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.States;
using ProtoBuf;

namespace CSM.TmpeSync.Net.Contracts.Requests
{
    [ProtoContract]
    public class SetJunctionRestrictionsRequest : CommandBase
    {
        [ProtoMember(1)] public ushort NodeId { get; set; }
        [ProtoMember(2)] public JunctionRestrictionsState State { get; set; }
    }
}
