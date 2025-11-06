using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.ToggleTrafficLights.Messages
{
    [ProtoContract(Name = "ToggleTrafficLightRequest")]
    public class ToggleTrafficLightsUpdateRequest : CommandBase
    {
        [ProtoMember(1)] public ushort NodeId { get; set; }
        [ProtoMember(2)] public bool Enabled { get; set; }
    }
}
