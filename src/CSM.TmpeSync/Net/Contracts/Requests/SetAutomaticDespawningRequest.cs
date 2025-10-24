using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.Net.Contracts.Requests
{
    [ProtoContract]
    public class SetAutomaticDespawningRequest : CommandBase
    {
        [ProtoMember(1)] public bool Enabled { get; set; }
    }
}
