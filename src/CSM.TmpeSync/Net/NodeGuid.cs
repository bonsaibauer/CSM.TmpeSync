using System;

namespace CSM.TmpeSync.Net
{
    /// <summary>
    /// Stable identifier for a network node.
    /// </summary>
    public readonly struct NodeGuid : IEquatable<NodeGuid>
    {
        public NodeGuid(
            ushort nodeId,
            uint buildIndex,
            ushort prefabId,
            ushort flags,
            uint sequence)
        {
            NodeId = nodeId;
            BuildIndex = buildIndex;
            PrefabId = prefabId;
            Flags = flags;
            Sequence = sequence;
        }

        public ushort NodeId { get; }

        public uint BuildIndex { get; }

        public ushort PrefabId { get; }

        public ushort Flags { get; }

        public uint Sequence { get; }

        public bool IsValid => BuildIndex != 0 || PrefabId != 0 || Sequence != 0;

        public bool Equals(NodeGuid other) =>
            NodeId == other.NodeId &&
            BuildIndex == other.BuildIndex &&
            PrefabId == other.PrefabId &&
            Flags == other.Flags &&
            Sequence == other.Sequence;

        public override bool Equals(object obj) => obj is NodeGuid other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = NodeId.GetHashCode();
                hashCode = (hashCode * 397) ^ BuildIndex.GetHashCode();
                hashCode = (hashCode * 397) ^ PrefabId.GetHashCode();
                hashCode = (hashCode * 397) ^ Flags.GetHashCode();
                hashCode = (hashCode * 397) ^ Sequence.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(NodeGuid left, NodeGuid right) => left.Equals(right);

        public static bool operator !=(NodeGuid left, NodeGuid right) => !left.Equals(right);

        public override string ToString() =>
            $"NodeGuid(Id={NodeId}, Build={BuildIndex}, Prefab={PrefabId}, Flags={Flags}, Seq={Sequence})";
    }
}
