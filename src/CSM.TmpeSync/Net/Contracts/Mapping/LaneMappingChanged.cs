using CSM.API.Commands;
using ProtoBuf;

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
        public long Version { get; set; }
    }
}
