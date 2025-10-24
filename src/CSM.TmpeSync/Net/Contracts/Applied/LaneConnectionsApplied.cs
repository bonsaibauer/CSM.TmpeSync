using System;
using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.Net.Contracts.Applied
{
    [ProtoContract]
    public class LaneConnectionsApplied : CommandBase
    {
        [ProtoMember(1, IsRequired = true)] public uint SourceLaneId { get; set; }
        [ProtoMember(2)] public uint[] TargetLaneIds { get; set; } = new uint[0];
        [ProtoMember(3, IsRequired = true)] public ushort SourceSegmentId { get; set; }
        [ProtoMember(4, IsRequired = true)] public int SourceLaneIndex { get; set; } = -1;
        [ProtoMember(5)] public ushort[] TargetSegmentIds { get; set; } = new ushort[0];
        [ProtoMember(6)] public int[] TargetLaneIndexes { get; set; } = new int[0];
        [ProtoMember(7)] public long MappingVersion { get; set; }
    }
}
