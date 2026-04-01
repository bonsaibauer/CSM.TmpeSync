using ProtoBuf;

namespace CSM.TmpeSync.TimedTrafficLights.Messages
{
    [ProtoContract(Name = "TimedTrafficLightsRuntimeState")]
    public class TimedTrafficLightsRuntimeState
    {
        [ProtoMember(1, IsRequired = true)] public ushort MasterNodeId { get; set; }
        [ProtoMember(2, IsRequired = true)] public bool IsRunning { get; set; }
        [ProtoMember(3, IsRequired = true)] public int CurrentStep { get; set; }
        [ProtoMember(4, IsRequired = true)] public uint Epoch { get; set; }
    }
}