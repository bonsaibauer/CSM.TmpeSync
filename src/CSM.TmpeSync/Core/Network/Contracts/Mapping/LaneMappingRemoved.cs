using CSM.API.Commands;
using CSM.TmpeSync.Network;
using ProtoBuf;

namespace CSM.TmpeSync.Network.Contracts.Mapping
{
    [ProtoContract]
    public class LaneMappingRemoved : CommandBase
    {
        [ProtoMember(1)]
        public ushort SegmentId { get; set; }

        [ProtoMember(2)]
        public int LaneIndex { get; set; }

        [ProtoMember(3)]
        public ushort LaneGuidSegmentId { get; set; }

        [ProtoMember(4)]
        public uint LaneGuidSegmentBuildIndex { get; set; }

        [ProtoMember(5)]
        public ushort LaneGuidPrefabId { get; set; }

        [ProtoMember(6)]
        public byte LaneGuidPrefabLaneIndex { get; set; }

        [ProtoMember(7)]
        public uint LaneGuidSequence { get; set; }

        [ProtoMember(8)]
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
