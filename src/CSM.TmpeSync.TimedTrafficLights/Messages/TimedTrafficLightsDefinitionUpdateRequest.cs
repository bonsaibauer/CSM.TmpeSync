using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.TimedTrafficLights.Messages
{
    [ProtoContract(Name = "TimedTrafficLightsDefinitionUpdateRequest")]
    public class TimedTrafficLightsDefinitionUpdateRequest : CommandBase
    {
        [ProtoMember(1, IsRequired = true)] public ushort MasterNodeId { get; set; }
        [ProtoMember(2, IsRequired = true)] public bool Removed { get; set; }
        [ProtoMember(3)] public TimedTrafficLightsDefinitionState Definition { get; set; }
    }
}