using System.Collections.Generic;
using CSM.API.Commands;
using CSM.TmpeSync.Messages.States;
using ProtoBuf;

namespace CSM.TmpeSync.LaneArrows.Messages
{
    [ProtoContract(Name = "SetLaneArrowsAtEndRequest")]
    public class LaneArrowsUpdateRequest : CommandBase
    {
        public LaneArrowsUpdateRequest()
        {
            Items = new List<Entry>();
        }

        [ProtoMember(1, IsRequired = true)] public ushort NodeId { get; set; }
        [ProtoMember(2, IsRequired = true)] public ushort SegmentId { get; set; }
        [ProtoMember(3, IsRequired = true)] public bool StartNode { get; set; }
        [ProtoMember(4, IsRequired = true)] public List<Entry> Items { get; set; }

        [ProtoContract]
        public class Entry
        {
            [ProtoMember(1, IsRequired = true)] public int Ordinal { get; set; }
            [ProtoMember(2, IsRequired = true)] public LaneArrowFlags Arrows { get; set; }
        }
    }
}
