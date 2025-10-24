using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.Net.Contracts.Applied
{
    [ProtoContract]
    public class AutomaticDespawningApplied : CommandBase
    {
        [ProtoMember(1)] public bool Enabled { get; set; }
    }
}
