using System.Collections.Generic;
using ProtoBuf;

namespace CSM.TmpeSync.TimedTrafficLights.Messages
{
    [ProtoContract(Name = "TimedTrafficLightsDefinitionState")]
    public class TimedTrafficLightsDefinitionState
    {
        public TimedTrafficLightsDefinitionState()
        {
            NodeGroup = new List<ushort>();
            Nodes = new List<NodeState>();
        }

        [ProtoMember(1, IsRequired = true)] public ushort MasterNodeId { get; set; }
        [ProtoMember(2, IsRequired = true)] public List<ushort> NodeGroup { get; set; }
        [ProtoMember(3, IsRequired = true)] public List<NodeState> Nodes { get; set; }

        [ProtoContract]
        public class NodeState
        {
            public NodeState()
            {
                Steps = new List<StepState>();
            }

            [ProtoMember(1, IsRequired = true)] public ushort NodeId { get; set; }
            [ProtoMember(2, IsRequired = true)] public List<StepState> Steps { get; set; }
        }

        [ProtoContract]
        public class StepState
        {
            public StepState()
            {
                SegmentLights = new List<SegmentState>();
            }

            [ProtoMember(1, IsRequired = true)] public int MinTime { get; set; }
            [ProtoMember(2, IsRequired = true)] public int MaxTime { get; set; }
            [ProtoMember(3, IsRequired = true)] public int ChangeMetric { get; set; }
            [ProtoMember(4, IsRequired = true)] public float WaitFlowBalance { get; set; }
            [ProtoMember(5, IsRequired = true)] public List<SegmentState> SegmentLights { get; set; }
        }

        [ProtoContract]
        public class SegmentState
        {
            public SegmentState()
            {
                VehicleLights = new List<VehicleState>();
            }

            [ProtoMember(1, IsRequired = true)] public ushort SegmentId { get; set; }
            [ProtoMember(2, IsRequired = true)] public bool StartNode { get; set; }
            [ProtoMember(3, IsRequired = true)] public bool ManualPedestrianMode { get; set; }
            [ProtoMember(4, IsRequired = true)] public bool HasPedestrianLightState { get; set; }
            [ProtoMember(5, IsRequired = true)] public int PedestrianLightState { get; set; }
            [ProtoMember(6, IsRequired = true)] public List<VehicleState> VehicleLights { get; set; }
        }

        [ProtoContract]
        public class VehicleState
        {
            [ProtoMember(1, IsRequired = true)] public int VehicleType { get; set; }
            [ProtoMember(2, IsRequired = true)] public int LightMode { get; set; }
            [ProtoMember(3, IsRequired = true)] public int MainLightState { get; set; }
            [ProtoMember(4, IsRequired = true)] public int LeftLightState { get; set; }
            [ProtoMember(5, IsRequired = true)] public int RightLightState { get; set; }
        }
    }
}