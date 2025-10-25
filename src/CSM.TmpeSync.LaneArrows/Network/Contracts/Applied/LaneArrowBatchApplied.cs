using System.Collections.Generic;
using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.States;
using ProtoBuf;

namespace CSM.TmpeSync.Network.Contracts.Applied
{
    [ProtoContract]
    public class LaneArrowBatchApplied : CommandBase
    {
        public LaneArrowBatchApplied()
        {
            Items = new List<Entry>();
        }

        [ProtoMember(1, IsRequired = true)]
        public List<Entry> Items { get; set; }

        [ProtoContract]
        public class Entry
        {
            [ProtoMember(1, IsRequired = true)] public uint LaneId { get; set; }
            [ProtoMember(2, IsRequired = true)] public LaneArrowFlags Arrows { get; set; }
            [ProtoMember(3, IsRequired = true)] public ushort SegmentId { get; set; }
            [ProtoMember(4, IsRequired = true)] public int LaneIndex { get; set; } = -1;
        }
    }
}
