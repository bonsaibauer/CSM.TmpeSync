using System;
using System.Collections.Generic;
using System.Linq;
using ProtoBuf;

namespace CSM.TmpeSync.ManualTrafficLights.Messages
{
    [ProtoContract(Name = "ManualTrafficLightsNodeState")]
    public class ManualTrafficLightsNodeState
    {
        public ManualTrafficLightsNodeState()
        {
            Segments = new List<SegmentState>();
        }

        [ProtoMember(1)] public ushort NodeId { get; set; }
        [ProtoMember(2)] public bool IsManualEnabled { get; set; }
        [ProtoMember(3)] public List<SegmentState> Segments { get; set; }

        public void Normalize()
        {
            if (Segments == null)
            {
                Segments = new List<SegmentState>();
                return;
            }

            Segments = Segments
                .Where(segment => segment != null && segment.SegmentId != 0)
                .OrderBy(segment => segment.SegmentId)
                .ThenBy(segment => segment.StartNode ? 0 : 1)
                .ToList();

            for (int i = 0; i < Segments.Count; i++)
                Segments[i].Normalize();
        }

        public ManualTrafficLightsNodeState Clone()
        {
            var clone = new ManualTrafficLightsNodeState
            {
                NodeId = NodeId,
                IsManualEnabled = IsManualEnabled
            };

            if (Segments != null)
            {
                for (int i = 0; i < Segments.Count; i++)
                {
                    var segment = Segments[i];
                    if (segment == null)
                        continue;

                    clone.Segments.Add(segment.Clone());
                }
            }

            clone.Normalize();
            return clone;
        }

        public override string ToString()
        {
            var segmentCount = Segments != null ? Segments.Count : 0;
            return string.Format("Node={0} Manual={1} Segments={2}", NodeId, IsManualEnabled, segmentCount);
        }

        [ProtoContract]
        public class SegmentState
        {
            public SegmentState()
            {
                VehicleLights = new List<VehicleLightState>();
            }

            [ProtoMember(1)] public ushort SegmentId { get; set; }
            [ProtoMember(2)] public bool StartNode { get; set; }
            [ProtoMember(3)] public bool ManualPedestrianMode { get; set; }
            [ProtoMember(4)] public bool HasPedestrianLightState { get; set; }
            [ProtoMember(5)] public int PedestrianLightState { get; set; }
            [ProtoMember(6)] public List<VehicleLightState> VehicleLights { get; set; }

            internal void Normalize()
            {
                if (VehicleLights == null)
                {
                    VehicleLights = new List<VehicleLightState>();
                    return;
                }

                VehicleLights = VehicleLights
                    .Where(light => light != null)
                    .OrderBy(light => light.VehicleType)
                    .ToList();
            }

            public SegmentState Clone()
            {
                var clone = new SegmentState
                {
                    SegmentId = SegmentId,
                    StartNode = StartNode,
                    ManualPedestrianMode = ManualPedestrianMode,
                    HasPedestrianLightState = HasPedestrianLightState,
                    PedestrianLightState = PedestrianLightState
                };

                if (VehicleLights != null)
                {
                    for (int i = 0; i < VehicleLights.Count; i++)
                    {
                        var vehicle = VehicleLights[i];
                        if (vehicle == null)
                            continue;

                        clone.VehicleLights.Add(vehicle.Clone());
                    }
                }

                clone.Normalize();
                return clone;
            }
        }

        [ProtoContract]
        public class VehicleLightState
        {
            [ProtoMember(1)] public int VehicleType { get; set; }
            [ProtoMember(2)] public int LightMode { get; set; }
            [ProtoMember(3)] public int MainLightState { get; set; }
            [ProtoMember(4)] public int LeftLightState { get; set; }
            [ProtoMember(5)] public int RightLightState { get; set; }

            public VehicleLightState Clone()
            {
                return new VehicleLightState
                {
                    VehicleType = VehicleType,
                    LightMode = LightMode,
                    MainLightState = MainLightState,
                    LeftLightState = LeftLightState,
                    RightLightState = RightLightState
                };
            }
        }
    }
}