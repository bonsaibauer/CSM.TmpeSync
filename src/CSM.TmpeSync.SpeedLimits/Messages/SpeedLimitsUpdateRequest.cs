using System.Collections.Generic;
using CSM.API.Commands;
using CSM.TmpeSync.Messages.States;
using ProtoBuf;

namespace CSM.TmpeSync.SpeedLimits.Messages
{
    [ProtoContract(Name = "SetSpeedLimitsRequest")] 
    public class SpeedLimitsUpdateRequest : CommandBase
    {
        [ProtoMember(1)] public ushort SegmentId { get; set; }
        [ProtoMember(2)] public List<Entry> Items { get; set; } = new List<Entry>();

        [ProtoContract]
        public class Entry
        {
            [ProtoMember(1)] public int LaneOrdinal { get; set; }
            [ProtoMember(2)] public SpeedLimitValue Speed { get; set; }
            [ProtoMember(3)] public SpeedLimitsAppliedCommand.LaneSignature Signature { get; set; } = new SpeedLimitsAppliedCommand.LaneSignature();
        }
    }
}
