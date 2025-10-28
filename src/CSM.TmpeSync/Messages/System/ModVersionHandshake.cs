using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.Messages.System
{
    [ProtoContract]
    public class ModVersionHandshake : CommandBase
    {
        [ProtoMember(1)]
        public string ClientModVersion { get; set; }

        [ProtoMember(2)]
        public string CompatibilityVersion { get; set; }
    }
}
