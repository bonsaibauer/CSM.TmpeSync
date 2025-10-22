using System.Collections.Generic;
using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.Net.Contracts.Mapping
{
    [ProtoContract]
    public class LaneMappingBatch : CommandBase
    {
        [ProtoMember(1)]
        public bool IsFullSnapshot { get; set; }

        [ProtoMember(2)]
        public long Version { get; set; }

        [ProtoMember(3)]
        public List<Entry> Entries { get; set; } = new List<Entry>();

        [ProtoContract]
        public class Entry
        {
            [ProtoMember(1)]
            public ushort SegmentId { get; set; }

            [ProtoMember(2)]
            public int LaneIndex { get; set; }

            [ProtoMember(3)]
            public uint HostLaneId { get; set; }
        }
    }
}
