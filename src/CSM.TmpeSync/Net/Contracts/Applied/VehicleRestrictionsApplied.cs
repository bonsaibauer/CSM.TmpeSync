using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.States;
using ProtoBuf;

namespace CSM.TmpeSync.Net.Contracts.Applied
{
    [ProtoContract]
    public class VehicleRestrictionsApplied : CommandBase
    {
        [ProtoMember(1)] public uint LaneId { get; set; }
        [ProtoMember(2)] public VehicleRestrictionFlags Restrictions { get; set; }
    }
}
