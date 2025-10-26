using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.ClearTraffic.Messages
{
    [ProtoContract(Name = "ClearTrafficApplied")]
    public class ClearTrafficAppliedCommand : CommandBase
    {
    }
}

