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

        [ProtoMember(1)]
        public List<Entry> Items { get; set; }

        [ProtoContract]
        public class Entry
        {
            [ProtoMember(1)] public uint LaneId { get; set; }
            [ProtoMember(2)] public float SpeedKmh { get; set; }
        }
    }
}
