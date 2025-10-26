using System.Collections.Generic;
using CSM.API.Commands;
using CSM.TmpeSync.Messages.States;
using ProtoBuf;

namespace CSM.TmpeSync.VehicleRestrictions.Messages
{
    [ProtoContract(Name = "VehicleRestrictionsApplied")] 
    public class VehicleRestrictionsAppliedCommand : CommandBase
    {
        [ProtoMember(1)] public ushort SegmentId { get; set; }
        [ProtoMember(2)] public List<Entry> Items { get; set; } = new List<Entry>();

        [ProtoContract]
        public class Entry
        {
            [ProtoMember(1)] public int LaneOrdinal { get; set; }
            [ProtoMember(2)] public VehicleRestrictionFlags Restrictions { get; set; }
            [ProtoMember(3)] public LaneSignature Signature { get; set; } = new LaneSignature();
        }

        [ProtoContract]
        public class LaneSignature
        {
            [ProtoMember(1)] public int LaneTypeRaw { get; set; }
            [ProtoMember(2)] public int VehicleTypeRaw { get; set; }
            [ProtoMember(3)] public int DirectionRaw { get; set; }
        }
    }
}
