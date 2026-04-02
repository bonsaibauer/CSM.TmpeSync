using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.ManualTrafficLights.Messages
{
    [ProtoContract(Name = "ManualTrafficLightsUpdateRequest")]
    public class ManualTrafficLightsUpdateRequest : CommandBase
    {
        [ProtoMember(1)] public ushort NodeId { get; set; }
        [ProtoMember(2)] public ManualTrafficLightsNodeState State { get; set; }
    }
}