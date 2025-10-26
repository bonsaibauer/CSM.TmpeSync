using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.Messages.System
{
    [ProtoContract]
    public class RequestRejected : CommandBase
    {
        [ProtoMember(1)] public string Reason { get; set; }
        [ProtoMember(2)] public uint EntityId { get; set; }
        [ProtoMember(3)] public byte EntityType { get; set; } // 1=Lane,2=Segment,3=Node
    }
}
