using CSM.API.Commands;
using ProtoBuf;

namespace CSM.BaseGame.Commands.Data.Districts
{
    /// <summary>
    ///     Called when a district policy is set.
    /// </summary>
    [ProtoContract]
    public class DistrictPolicyCommand : CommandBase
    {
        /// <summary>
        ///     The district policy to set.
        /// </summary>
        [ProtoMember(1)]
        public DistrictPolicies.Policies Policy { get; set; }

        /// <summary>
        ///     The modified district.
        /// </summary>
        [ProtoMember(2)]
        public byte DistrictId { get; set; }

        /// <summary>
        ///     If the target is a park instead of a district.
        /// </summary>
        [ProtoMember(3)]
        public bool IsPark { get; set; }
    }
}
