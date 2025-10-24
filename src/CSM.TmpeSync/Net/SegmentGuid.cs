using System;

namespace CSM.TmpeSync.Net
{
    /// <summary>
    /// Stable identifier for a network segment that survives handle reassignments.
    /// </summary>
    public readonly struct SegmentGuid : IEquatable<SegmentGuid>
    {
        public SegmentGuid(
            ushort segmentId,
            uint buildIndex,
            ushort prefabId,
            ushort startNodePrefabId,
            ushort endNodePrefabId,
            uint sequence)
        {
            SegmentId = segmentId;
            BuildIndex = buildIndex;
            PrefabId = prefabId;
            StartNodePrefabId = startNodePrefabId;
            EndNodePrefabId = endNodePrefabId;
            Sequence = sequence;
        }

        public ushort SegmentId { get; }

        public uint BuildIndex { get; }

        public ushort PrefabId { get; }

        public ushort StartNodePrefabId { get; }

        public ushort EndNodePrefabId { get; }

        public uint Sequence { get; }

        public bool IsValid => BuildIndex != 0 || PrefabId != 0 || Sequence != 0;

        public bool Equals(SegmentGuid other) =>
            SegmentId == other.SegmentId &&
            BuildIndex == other.BuildIndex &&
            PrefabId == other.PrefabId &&
            StartNodePrefabId == other.StartNodePrefabId &&
            EndNodePrefabId == other.EndNodePrefabId &&
            Sequence == other.Sequence;

        public override bool Equals(object obj) => obj is SegmentGuid other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = SegmentId.GetHashCode();
                hashCode = (hashCode * 397) ^ BuildIndex.GetHashCode();
                hashCode = (hashCode * 397) ^ PrefabId.GetHashCode();
                hashCode = (hashCode * 397) ^ StartNodePrefabId.GetHashCode();
                hashCode = (hashCode * 397) ^ EndNodePrefabId.GetHashCode();
                hashCode = (hashCode * 397) ^ Sequence.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(SegmentGuid left, SegmentGuid right) => left.Equals(right);

        public static bool operator !=(SegmentGuid left, SegmentGuid right) => !left.Equals(right);

        public override string ToString() =>
            $"SegmentGuid(Id={SegmentId}, Build={BuildIndex}, Prefab={PrefabId}, StartPrefab={StartNodePrefabId}, EndPrefab={EndNodePrefabId}, Seq={Sequence})";
    }
}
