using System.Collections.Generic;
using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.Net.Contracts.Applied
{
    /// <summary>
    /// Batched variant used when exporting large speed-limit snapshots.
    /// </summary>
    [ProtoContract]
    public class SpeedLimitBatchApplied : CommandBase
    {
        public SpeedLimitBatchApplied()
        {
            Items = new List<Entry>();
        }

        [ProtoMember(1, IsRequired = true)]
        public List<Entry> Items { get; set; }

        /// <summary>
        /// Version of the lane mapping that the sender used while exporting the snapshot.
        /// Receivers wait for this version before applying the batch to avoid GUID/build mismatches.
        /// </summary>
        [ProtoMember(2)]
        public long MappingVersion { get; set; }

        [ProtoContract]
        public class Entry
        {
            [ProtoMember(1, IsRequired = true)] public uint LaneId { get; set; }
            [ProtoMember(2, IsRequired = true)] public float SpeedKmh { get; set; }
            [ProtoMember(3, IsRequired = true)] public ushort SegmentId { get; set; }
            [ProtoMember(4, IsRequired = true)] public int LaneIndex { get; set; } = -1;
            [ProtoMember(5)] public long MappingVersion { get; set; }
        }
    }
}
