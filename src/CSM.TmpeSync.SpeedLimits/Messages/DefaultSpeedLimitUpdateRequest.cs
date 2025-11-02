using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.SpeedLimits.Messages
{
    [ProtoContract(Name = "SetDefaultSpeedLimitRequest")]
    public class DefaultSpeedLimitUpdateRequest : CommandBase
    {
        [ProtoMember(1)] public string NetInfoName { get; set; }
        [ProtoMember(2)] public bool HasCustomSpeed { get; set; }
        [ProtoMember(3)] public float CustomGameSpeed { get; set; }
    }
}
