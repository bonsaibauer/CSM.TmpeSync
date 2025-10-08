using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.States;
using ProtoBuf;

namespace CSM.TmpeSync.Net.Contracts.Applied
{
    [ProtoContract]
    public class LaneArrowApplied : CommandBase
    {
        [ProtoMember(1)] public uint LaneId { get; set; }
        [ProtoMember(2)] public LaneArrowFlags Arrows { get; set; }
    }
}
