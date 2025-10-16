using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.Net.Contracts.Requests
{
    [ProtoContract]
    public class SetSpeedLimitRequest : CommandBase
    {
        [ProtoMember(1)] public uint LaneId { get; set; }
        [ProtoMember(2)] public float SpeedKmh { get; set; }
    }
}
