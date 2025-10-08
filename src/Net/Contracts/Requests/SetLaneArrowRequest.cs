using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.States;
using ProtoBuf;

namespace CSM.TmpeSync.Net.Contracts.Requests
{
    [ProtoContract]
    public class SetLaneArrowRequest : CommandBase
    {
        [ProtoMember(1)] public uint LaneId { get; set; }
        [ProtoMember(2)] public LaneArrowFlags Arrows { get; set; }
    }
}
