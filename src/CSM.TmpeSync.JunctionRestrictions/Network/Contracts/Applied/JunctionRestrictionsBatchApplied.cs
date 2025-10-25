using System.Collections.Generic;
using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.States;
using ProtoBuf;

namespace CSM.TmpeSync.Network.Contracts.Applied
{
    [ProtoContract]
    public class JunctionRestrictionsBatchApplied : CommandBase
    {
        public JunctionRestrictionsBatchApplied()
        {
            Items = new List<Entry>();
        }

        [ProtoMember(1, IsRequired = true)]
        public List<Entry> Items { get; set; }

        [ProtoContract]
        public class Entry
        {
            [ProtoMember(1, IsRequired = true)] public ushort NodeId { get; set; }
            [ProtoMember(2, IsRequired = true)] public JunctionRestrictionsState State { get; set; }
        }
    }
}
