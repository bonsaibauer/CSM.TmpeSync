using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.TmpeBridge;
using ProtoBuf;

namespace CSM.TmpeSync.Network.Contracts.Applied
{
    [ProtoContract]
    public class SpeedLimitApplied : CommandBase
    {
        [ProtoMember(1, IsRequired = true)] public uint LaneId { get; set; }
        [ProtoMember(2, IsRequired = true)] public SpeedLimitValue Speed { get; set; }
        [ProtoMember(3, IsRequired = true)] public ushort SegmentId { get; set; }
        [ProtoMember(4, IsRequired = true)] public int LaneIndex { get; set; } = -1;

        /// <summary>
        /// Lane mapping version that was current on the sender when the change was recorded.
        /// Allows receivers to defer application until the matching mapping update is available.
        /// </summary>
        [ProtoMember(5)]
        public long MappingVersion { get; set; }

        /// <summary>
        /// Raw km/h payload that mirrors <see cref="Speed"/>. Used as a lossless fallback when the
        /// palette-based encoding collapses during transport.
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
