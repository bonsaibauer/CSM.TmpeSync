using CSM.API.Commands;
using CSM.TmpeSync.Messages.States;
using ProtoBuf;

namespace CSM.TmpeSync.JunctionRestrictions.Messages
{
    [ProtoContract(Name = "SetJunctionRestrictionsRequest")]
    public class JunctionRestrictionsUpdateRequest : CommandBase
    {
        [ProtoMember(1)] public ushort NodeId { get; set; }
        [ProtoMember(2)] public ushort SegmentId { get; set; }
        [ProtoMember(3)] public JunctionRestrictionsState State { get; set; }
    }
}
