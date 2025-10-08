using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.Net.Contracts.Applied
{
    [ProtoContract]
    public class SpeedLimitApplied : CommandBase
    {
        [ProtoMember(1)] public uint LaneId { get; set; }
        [ProtoMember(2)] public float SpeedKmh { get; set; }
    }
}
