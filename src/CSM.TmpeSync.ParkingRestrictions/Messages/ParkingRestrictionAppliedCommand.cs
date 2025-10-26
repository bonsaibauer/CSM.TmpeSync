using CSM.API.Commands;
using CSM.TmpeSync.Messages.States;
using ProtoBuf;

namespace CSM.TmpeSync.ParkingRestrictions.Messages
{
    [ProtoContract(Name = "ParkingRestrictionApplied")]
    public class ParkingRestrictionAppliedCommand : CommandBase
    {
        [ProtoMember(1)] public ushort SegmentId { get; set; }
        [ProtoMember(2)] public ParkingRestrictionState State { get; set; }
    }
}
