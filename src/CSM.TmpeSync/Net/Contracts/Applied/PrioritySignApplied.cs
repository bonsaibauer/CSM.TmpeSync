using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.States;
using ProtoBuf;

namespace CSM.TmpeSync.Net.Contracts.Applied
{
    [ProtoContract]
    public class PrioritySignApplied : CommandBase
    {
        [ProtoMember(1)] public ushort NodeId { get; set; }
        [ProtoMember(2)] public ushort SegmentId { get; set; }
        [ProtoMember(3)] public PrioritySignType SignType { get; set; }
        [ProtoMember(4)] public long MappingVersion { get; set; }
    }
}
