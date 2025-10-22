using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.Net.Contracts.Applied
{
    [ProtoContract]
    public class SpeedLimitApplied : CommandBase
    {
        [ProtoMember(1, IsRequired = true)] public uint LaneId { get; set; }
        [ProtoMember(2, IsRequired = true)] public float SpeedKmh { get; set; }
        [ProtoMember(3, IsRequired = true)] public ushort SegmentId { get; set; }
        [ProtoMember(4, IsRequired = true)] public int LaneIndex { get; set; } = -1;
    }
}
