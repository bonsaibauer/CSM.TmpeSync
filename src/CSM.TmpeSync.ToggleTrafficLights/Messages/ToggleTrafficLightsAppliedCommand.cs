using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.ToggleTrafficLights.Messages
{
    [ProtoContract(Name = "TrafficLightToggledApplied")]
    public class ToggleTrafficLightsAppliedCommand : CommandBase
    {
        [ProtoMember(1)] public ushort NodeId { get; set; }
        [ProtoMember(2)] public bool Enabled { get; set; }
    }
}
