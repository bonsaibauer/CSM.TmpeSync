using System.Collections.Generic;
using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.LaneConnector.Messages
{
    [ProtoContract(Name = "LaneConnectionsApplied")]
    public class LaneConnectionsAppliedCommand : CommandBase
    {
        public LaneConnectionsAppliedCommand()
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
            [ProtoMember(1, IsRequired = true)] public int SourceOrdinal { get; set; }
            [ProtoMember(2, IsRequired = true)] public List<int> TargetOrdinals { get; set; } = new List<int>();
        }
    }
}

