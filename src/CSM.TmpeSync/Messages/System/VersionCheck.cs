using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.Messages.System
{
    [ProtoContract]
    public class VersionCheckRequest : CommandBase
    {
        [ProtoMember(1)] public string Version { get; set; }
        [ProtoMember(2)] public bool IsManualCheck { get; set; }
        [ProtoMember(3)] public string RequestId { get; set; }
    }

    [ProtoContract]
    public class VersionCheckResponse : CommandBase
    {
        [ProtoMember(1)] public string Version { get; set; }
        [ProtoMember(2)] public bool IsManualCheck { get; set; }
        [ProtoMember(3)] public string RequestId { get; set; }
    }

    [ProtoContract]
    public class VersionProbeRequest : CommandBase
    {
        [ProtoMember(1)] public string RequestId { get; set; }
        [ProtoMember(2)] public string HostVersion { get; set; }
    }

    [ProtoContract]
    public class VersionProbeResponse : CommandBase
    {
        [ProtoMember(1)] public string RequestId { get; set; }
        [ProtoMember(2)] public string ClientVersion { get; set; }
        [ProtoMember(3)] public bool MatchesHost { get; set; }
    }
}
