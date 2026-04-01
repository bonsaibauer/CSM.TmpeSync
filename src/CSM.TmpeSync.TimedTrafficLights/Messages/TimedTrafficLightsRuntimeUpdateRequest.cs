using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.TimedTrafficLights.Messages
{
    [ProtoContract(Name = "TimedTrafficLightsRuntimeUpdateRequest")]
    public class TimedTrafficLightsRuntimeUpdateRequest : CommandBase
    {
        [ProtoMember(1, IsRequired = true)] public TimedTrafficLightsRuntimeState Runtime { get; set; }
    }
}
