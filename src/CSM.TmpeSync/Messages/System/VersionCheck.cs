using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.Messages.System
{
    [ProtoContract]
    public class VersionCheckRequest : CommandBase
    {
        [ProtoMember(1)] public string Version { get; set; }
    }

    [ProtoContract]
    public class VersionCheckResponse : CommandBase
    {
        [ProtoMember(1)] public string Version { get; set; }
    }
}
