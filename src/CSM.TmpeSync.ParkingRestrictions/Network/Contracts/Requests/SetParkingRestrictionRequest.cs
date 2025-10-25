using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.States;
using ProtoBuf;

namespace CSM.TmpeSync.Network.Contracts.Requests
{
    [ProtoContract]
    public class SetParkingRestrictionRequest : CommandBase
    {
        [ProtoMember(1)] public ushort SegmentId { get; set; }
        [ProtoMember(2)] public ParkingRestrictionState State { get; set; }
    }
}
