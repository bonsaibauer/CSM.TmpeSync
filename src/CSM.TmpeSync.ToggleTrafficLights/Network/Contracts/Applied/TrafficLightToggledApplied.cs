using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.ToggleTrafficLights.Network.Contracts.Applied
{
    [ProtoContract]
    public class TrafficLightToggledApplied : CommandBase
    {
        [ProtoMember(1)] public ushort NodeId { get; set; }
        [ProtoMember(2)] public bool Enabled { get; set; }
    }
}
