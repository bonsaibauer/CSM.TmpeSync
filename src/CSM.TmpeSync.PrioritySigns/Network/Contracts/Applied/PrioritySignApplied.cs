using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.States;
using ProtoBuf;

namespace CSM.TmpeSync.Network.Contracts.Applied
{
    [ProtoContract]
    public class PrioritySignApplied : CommandBase
    {
        [ProtoMember(1)] public ushort NodeId { get; set; }
        [ProtoMember(2)] public ushort SegmentId { get; set; }
        [ProtoMember(3)] public PrioritySignType SignType { get; set; }
    }
}
