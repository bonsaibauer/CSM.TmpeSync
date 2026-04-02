using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.TimedTrafficLights.Messages
{
    [ProtoContract(Name = "TimedTrafficLightsRuntimeApplied")]
    public class TimedTrafficLightsRuntimeAppliedCommand : CommandBase
    {
        [ProtoMember(1, IsRequired = true)] public TimedTrafficLightsRuntimeState Runtime { get; set; }
    }
}