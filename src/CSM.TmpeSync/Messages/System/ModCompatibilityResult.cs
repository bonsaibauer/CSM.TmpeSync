using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.Messages.System
{
    [ProtoContract]
    public class ModCompatibilityResult : CommandBase
    {
        [ProtoMember(1)]
        public bool Accepted { get; set; }

        [ProtoMember(2)]
        public string HostCompatibilityVersion { get; set; }

        [ProtoMember(3)]
        public string ClientCompatibilityVersion { get; set; }

        [ProtoMember(4)]
        public string HostModVersion { get; set; }

        [ProtoMember(5)]
        public string ClientModVersion { get; set; }

        [ProtoMember(6)]
        public string Relation { get; set; }

        [ProtoMember(7)]
        public string Reason { get; set; }
    }
}
