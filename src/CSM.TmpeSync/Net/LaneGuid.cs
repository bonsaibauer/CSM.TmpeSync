using System;

namespace CSM.TmpeSync.Net
{
    /// <summary>
    /// Stable identifier for a lane that stays valid across network sessions.
    /// </summary>
    public readonly struct LaneGuid : IEquatable<LaneGuid>
    {
        public LaneGuid(
            ushort segmentId,
            uint segmentBuildIndex,
            ushort prefabId,
            byte prefabLaneIndex,
            uint sequence)
        {
            SegmentId = segmentId;
            SegmentBuildIndex = segmentBuildIndex;
            PrefabId = prefabId;
            PrefabLaneIndex = prefabLaneIndex;
            Sequence = sequence;
        }

        public ushort SegmentId { get; }

        public uint SegmentBuildIndex { get; }

        public ushort PrefabId { get; }

        public byte PrefabLaneIndex { get; }

        public uint Sequence { get; }

        public bool IsValid => SegmentId != 0 || PrefabId != 0 || Sequence != 0;

        public bool Equals(LaneGuid other) =>
            SegmentId == other.SegmentId &&
            SegmentBuildIndex == other.SegmentBuildIndex &&
            PrefabId == other.PrefabId &&
            PrefabLaneIndex == other.PrefabLaneIndex &&
            Sequence == other.Sequence;

        public override bool Equals(object obj) => obj is LaneGuid other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = SegmentId.GetHashCode();
                hashCode = (hashCode * 397) ^ SegmentBuildIndex.GetHashCode();
                hashCode = (hashCode * 397) ^ PrefabId.GetHashCode();
                hashCode = (hashCode * 397) ^ PrefabLaneIndex.GetHashCode();
                hashCode = (hashCode * 397) ^ Sequence.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(LaneGuid left, LaneGuid right) => left.Equals(right);

        public static bool operator !=(LaneGuid left, LaneGuid right) => !left.Equals(right);

        public override string ToString() =>
            $"LaneGuid(Segment={SegmentId}, Build={SegmentBuildIndex}, Prefab={PrefabId}, Lane={PrefabLaneIndex}, Seq={Sequence})";
    }
}
