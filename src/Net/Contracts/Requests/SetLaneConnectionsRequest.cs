using System;
using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.Net.Contracts.Requests
{
    [ProtoContract]
    public class SetLaneConnectionsRequest : CommandBase
    {
        [ProtoMember(1)] public uint SourceLaneId { get; set; }

        [ProtoMember(2)] public uint[] TargetLaneIds { get; set; } = Array.Empty<uint>();
    }
}
