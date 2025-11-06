using CSM.API.Commands;
using ProtoBuf;

namespace CSM.TmpeSync.Messages.Locks
{
    [ProtoContract] public class BeginEditRequest : CommandBase { [ProtoMember(1)] public byte TargetKind; [ProtoMember(2)] public uint TargetId; }
    [ProtoContract] public class EndEditRequest   : CommandBase { [ProtoMember(1)] public byte TargetKind; [ProtoMember(2)] public uint TargetId; }

    [ProtoContract] public class EditLockApplied  : CommandBase { [ProtoMember(1)] public byte TargetKind; [ProtoMember(2)] public uint TargetId; [ProtoMember(3)] public int OwnerClientId; [ProtoMember(4)] public int TtlFrames; }
    [ProtoContract] public class EditLockCleared  : CommandBase { [ProtoMember(1)] public byte TargetKind; [ProtoMember(2)] public uint TargetId; }
}
