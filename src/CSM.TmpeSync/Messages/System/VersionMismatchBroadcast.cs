using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.Messages.System
{
    [ProtoContract]
    public class VersionMismatchBroadcast : CommandBase
    {
        [ProtoMember(1)] public string ServerVersion { get; set; }
        [ProtoMember(2)] public string ReportedClientVersion { get; set; }
        [ProtoMember(3)] public int TargetClientId { get; set; }
    }
}
