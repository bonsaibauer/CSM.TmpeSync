using CSM.API.Commands;
using CSM.TmpeSync.Messages.States;
using ProtoBuf;

namespace CSM.TmpeSync.PrioritySigns.Messages
{
    [ProtoContract(Name = "SetPrioritySignRequest")]
    public class PrioritySignUpdateRequest : CommandBase
    {
        [ProtoMember(1)] public ushort NodeId { get; set; }
        [ProtoMember(2)] public ushort SegmentId { get; set; }
        [ProtoMember(3)] public PrioritySignType SignType { get; set; }
    }
}
