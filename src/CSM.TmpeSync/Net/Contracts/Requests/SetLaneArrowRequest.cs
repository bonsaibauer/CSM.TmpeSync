using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.States;
using ProtoBuf;

namespace CSM.TmpeSync.Net.Contracts.Requests
{
    [ProtoContract]
    public class SetLaneArrowRequest : CommandBase
    {
        [ProtoMember(1, IsRequired = true)] public uint LaneId { get; set; }
        [ProtoMember(2, IsRequired = true)] public LaneArrowFlags Arrows { get; set; }
        [ProtoMember(3, IsRequired = true)] public ushort SegmentId { get; set; }
        [ProtoMember(4, IsRequired = true)] public int LaneIndex { get; set; } = -1;
    }
}
