using System;
using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.Net.Contracts.Applied
{
    [ProtoContract]
    public class LaneConnectionsApplied : CommandBase
    {
        [ProtoMember(1)] public uint SourceLaneId { get; set; }
        [ProtoMember(2)] public uint[] TargetLaneIds { get; set; } = new uint[0];
    }
}
