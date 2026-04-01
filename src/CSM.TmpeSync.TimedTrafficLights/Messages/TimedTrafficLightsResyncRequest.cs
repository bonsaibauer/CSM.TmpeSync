using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.TimedTrafficLights.Messages
{
    [ProtoContract(Name = "TimedTrafficLightsResyncRequest")]
    public class TimedTrafficLightsResyncRequest : CommandBase
    {
        [ProtoMember(1, IsRequired = true)] public ushort MasterNodeId { get; set; }
    }
}
