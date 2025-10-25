using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.Network.Contracts.System
{
    [ProtoContract]
    public class TmpeFeatureReady : CommandBase
    {
        [ProtoMember(1)]
        public bool IsReady { get; set; }

        [ProtoMember(2)]
        public ulong FeatureMask { get; set; }
    }
}
