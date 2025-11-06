using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.ClearTraffic.Messages
{
    [ProtoContract(Name = "ClearTrafficRequest")]
    public class ClearTrafficUpdateRequest : CommandBase
    {
    }
}

