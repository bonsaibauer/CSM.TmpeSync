using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.States;
using ProtoBuf;

namespace CSM.TmpeSync.Network.Contracts.Applied
{
    [ProtoContract]
    public class VehicleRestrictionsApplied : CommandBase
    {
        [ProtoMember(1, IsRequired = true)] public uint LaneId { get; set; }
        [ProtoMember(2, IsRequired = true)] public VehicleRestrictionFlags Restrictions { get; set; }
        [ProtoMember(3, IsRequired = true)] public ushort SegmentId { get; set; }
        [ProtoMember(4, IsRequired = true)] public int LaneIndex { get; set; } = -1;
    }
}
