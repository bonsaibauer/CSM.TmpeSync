using System;
using ProtoBuf;

namespace CSM.TmpeSync.Net.Contracts.States
{
    [ProtoContract]
    [Flags]
    public enum LaneArrowFlags
    {
        [ProtoEnum(Value = 0)] None = 0,
        [ProtoEnum(Value = 1)] Left = 1,
        [ProtoEnum(Value = 2)] Forward = 2,
        [ProtoEnum(Value = 4)] Right = 4
    }

    [ProtoContract]
    [Flags]
    public enum VehicleRestrictionFlags
    {
        [ProtoEnum(Value = 0)] None = 0,
        [ProtoEnum(Value = 1)] PassengerCar = 1,
        [ProtoEnum(Value = 2)] CargoTruck = 2,
        [ProtoEnum(Value = 4)] Bus = 4,
        [ProtoEnum(Value = 8)] Taxi = 8,
        [ProtoEnum(Value = 16)] Service = 16,
        [ProtoEnum(Value = 32)] Emergency = 32,
        [ProtoEnum(Value = 64)] Tram = 64
    }

    [ProtoContract]
    public class JunctionRestrictionsState
    {
        [ProtoMember(1)] public bool AllowUTurns { get; set; } = true;
        [ProtoMember(2)] public bool AllowLaneChangesWhenGoingStraight { get; set; } = true;
        [ProtoMember(3)] public bool AllowEnterWhenBlocked { get; set; } = true;
        [ProtoMember(4)] public bool AllowPedestrianCrossing { get; set; } = true;
        [ProtoMember(5)] public bool AllowTurningOnRed { get; set; } = true;

        public JunctionRestrictionsState Clone()
        {
            return (JunctionRestrictionsState) MemberwiseClone();
        }

        public bool IsDefault()
        {
            return AllowUTurns && AllowLaneChangesWhenGoingStraight && AllowEnterWhenBlocked && AllowPedestrianCrossing && AllowTurningOnRed;
        }

        public override string ToString()
        {
            return $"UTurns={AllowUTurns}, LaneChange={AllowLaneChangesWhenGoingStraight}, Blocked={AllowEnterWhenBlocked}, Pedestrians={AllowPedestrianCrossing}, TurnOnRed={AllowTurningOnRed}";
        }
    }

    [ProtoContract]
    public enum PrioritySignType
    {
        [ProtoEnum(Value = 0)] None = 0,
        [ProtoEnum(Value = 1)] Yield = 1,
        [ProtoEnum(Value = 2)] Stop = 2,
        [ProtoEnum(Value = 3)] Priority = 3
    }

    [ProtoContract]
    public class ParkingRestrictionState
    {
        [ProtoMember(1)] public bool AllowParkingForward { get; set; } = true;
        [ProtoMember(2)] public bool AllowParkingBackward { get; set; } = true;

        public bool AllowParkingBothDirections => AllowParkingForward && AllowParkingBackward;

        public ParkingRestrictionState Clone()
        {
            return new ParkingRestrictionState
            {
                AllowParkingForward = AllowParkingForward,
                AllowParkingBackward = AllowParkingBackward
            };
        }

        public override string ToString()
        {
            return $"Forward={AllowParkingForward}, Backward={AllowParkingBackward}";
        }
    }

    [ProtoContract]
    public class TimedTrafficLightState
    {
        [ProtoMember(1)] public bool Enabled { get; set; }
        [ProtoMember(2)] public int StepCount { get; set; }
        [ProtoMember(3)] public float CycleLengthSeconds { get; set; }

        public TimedTrafficLightState Clone()
        {
            return new TimedTrafficLightState
            {
                Enabled = Enabled,
                StepCount = StepCount,
                CycleLengthSeconds = CycleLengthSeconds
            };
        }

        public override string ToString()
        {
            return Enabled ? $"Enabled Steps={StepCount} Cycle={CycleLengthSeconds}s" : "Disabled";
        }
    }
}
