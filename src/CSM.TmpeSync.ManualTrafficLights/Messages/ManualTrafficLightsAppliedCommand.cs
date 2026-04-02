using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.ManualTrafficLights.Messages
{
    [ProtoContract(Name = "ManualTrafficLightsApplied")]
    public class ManualTrafficLightsAppliedCommand : CommandBase
    {
        [ProtoMember(1)] public ushort NodeId { get; set; }
        [ProtoMember(2)] public ManualTrafficLightsNodeState State { get; set; }
    }
}