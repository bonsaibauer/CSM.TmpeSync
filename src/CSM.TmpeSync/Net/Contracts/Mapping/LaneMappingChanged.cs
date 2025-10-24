using CSM.API.Commands;
using ProtoBuf;
using CSM.TmpeSync.Net;

namespace CSM.TmpeSync.Net.Contracts.Mapping
{
    [ProtoContract]
    public class LaneMappingChanged : CommandBase
    {
        [ProtoMember(1)]
        public ushort SegmentId { get; set; }

        [ProtoMember(2)]
        public int LaneIndex { get; set; }

        [ProtoMember(3)]
        public uint HostLaneId { get; set; }

        [ProtoMember(4)]
        public ushort LaneGuidSegmentId { get; set; }

        [ProtoMember(5)]
        public uint LaneGuidSegmentBuildIndex { get; set; }

        [ProtoMember(6)]
        public ushort LaneGuidPrefabId { get; set; }

        [ProtoMember(7)]
        public byte LaneGuidPrefabLaneIndex { get; set; }

        [ProtoMember(8)]
        public uint LaneGuidSequence { get; set; }

        [ProtoMember(9)]
        public long Version { get; set; }

        [ProtoIgnore]
        public LaneGuid LaneGuid
        {
            get => new LaneGuid(
                LaneGuidSegmentId,
                LaneGuidSegmentBuildIndex,
                LaneGuidPrefabId,
                LaneGuidPrefabLaneIndex,
                LaneGuidSequence);
            set
            {
                LaneGuidSegmentId = value.SegmentId;
                LaneGuidSegmentBuildIndex = value.SegmentBuildIndex;
                LaneGuidPrefabId = value.PrefabId;
                LaneGuidPrefabLaneIndex = value.PrefabLaneIndex;
                LaneGuidSequence = value.Sequence;
            }
        }
    }
}
