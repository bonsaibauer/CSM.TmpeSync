using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.TmpeBridge;
using ProtoBuf;

namespace CSM.TmpeSync.Network.Contracts.Requests
{
    [ProtoContract]
    public class SetSpeedLimitRequest : CommandBase
    {
        [ProtoMember(1, IsRequired = true)] public uint LaneId { get; set; }
        [ProtoMember(2, IsRequired = true)] public SpeedLimitValue Speed { get; set; }
        [ProtoMember(3, IsRequired = true)] public ushort SegmentId { get; set; }
        [ProtoMember(4, IsRequired = true)] public int LaneIndex { get; set; } = -1;

        /// <summary>
        /// Raw km/h payload accompanying <see cref="Speed"/> to guarantee lossless transfer.
        /// </summary>
        [ProtoMember(100)]
        public float RawSpeedKmh { get; private set; }

        [ProtoBeforeSerialization]
        private void OnBeforeSerialize()
        {
            RawSpeedKmh = SpeedLimitCodec.DecodeToKmh(Speed);
        }

        [ProtoAfterSerialization]
        private void OnAfterSerialize()
        {
            RawSpeedKmh = 0f;
        }

        [ProtoAfterDeserialization]
        private void OnAfterDeserialize()
        {
            NormalizeSpeedFromRaw();
        }

        private void NormalizeSpeedFromRaw()
        {
            if (Speed == null && RawSpeedKmh > 0.05f)
            {
                Speed = SpeedLimitCodec.Encode(RawSpeedKmh);
                return;
            }

            if (Speed == null)
                return;

            if (Speed.RawSpeedKmh <= 0.05f && RawSpeedKmh > 0.05f)
                Speed.RawSpeedKmh = RawSpeedKmh;

            if (SpeedLimitCodec.IsDefault(Speed) && RawSpeedKmh > 0.05f)
            {
                var rebuilt = SpeedLimitCodec.Encode(RawSpeedKmh);
                Speed.Type = rebuilt.Type;
                Speed.Index = rebuilt.Index;
                Speed.RawSpeedKmh = rebuilt.RawSpeedKmh;
            }
        }
    }
}
