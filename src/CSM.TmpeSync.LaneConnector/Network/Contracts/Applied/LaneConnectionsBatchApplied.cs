using System.Collections.Generic;
using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.Network.Contracts.Applied
{
    [ProtoContract]
    public class LaneConnectionsBatchApplied : CommandBase
    {
        public LaneConnectionsBatchApplied()
        {
            Items = new List<Entry>();
        }

        [ProtoMember(1, IsRequired = true)]
        public List<Entry> Items { get; set; }

        [ProtoContract]
        public class Entry
        {
            [ProtoMember(1, IsRequired = true)] public uint SourceLaneId { get; set; }
            [ProtoMember(2, IsRequired = true)] public ushort SourceSegmentId { get; set; }
            [ProtoMember(3, IsRequired = true)] public int SourceLaneIndex { get; set; } = -1;
            [ProtoMember(4)] public uint[] TargetLaneIds { get; set; } = new uint[0];
            [ProtoMember(5)] public ushort[] TargetSegmentIds { get; set; } = new ushort[0];
            [ProtoMember(6)] public int[] TargetLaneIndexes { get; set; } = new int[0];
        }
    }
}
