using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.Net.Contracts.Applied
{
    [ProtoContract]
    public class SpeedLimitApplied : CommandBase
    {
        [ProtoMember(1, IsRequired = true)] public uint LaneId { get; set; }
        [ProtoMember(2, IsRequired = true)] public float SpeedKmh { get; set; }
        [ProtoMember(3, IsRequired = true)] public ushort SegmentId { get; set; }
        [ProtoMember(4, IsRequired = true)] public int LaneIndex { get; set; } = -1;

        /// <summary>
        /// Lane mapping version that was current on the sender when the change was recorded.
        /// Allows receivers to defer application until the matching mapping update is available.
        /// </summary>
        [ProtoMember(5)]
        public long MappingVersion { get; set; }
    }
}
