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
        [ProtoEnum(Value = 64)] Tram = 64,
        [ProtoEnum(Value = 128)] PassengerTrain = 128,
        [ProtoEnum(Value = 256)] CargoTrain = 256,
        [ProtoEnum(Value = 512)] Bicycle = 512,
        [ProtoEnum(Value = 1024)] Pedestrian = 1024,
        [ProtoEnum(Value = 2048)] PassengerShip = 2048,
        [ProtoEnum(Value = 4096)] CargoShip = 4096,
        [ProtoEnum(Value = 8192)] PassengerPlane = 8192,
        [ProtoEnum(Value = 16384)] CargoPlane = 16384,
        [ProtoEnum(Value = 32768)] Helicopter = 32768,
        [ProtoEnum(Value = 65536)] CableCar = 65536,
        [ProtoEnum(Value = 131072)] PassengerFerry = 131072,
        [ProtoEnum(Value = 262144)] PassengerBlimp = 262144,
        [ProtoEnum(Value = 524288)] Trolleybus = 524288
    }

    [ProtoContract]
    public class JunctionRestrictionsState
    {
        [ProtoMember(1)] public bool AllowUTurns { get; set; } = true;
        [ProtoMember(2)] public bool AllowLaneChangesWhenGoingStraight { get; set; } = true;
        [ProtoMember(3)] public bool AllowEnterWhenBlocked { get; set; } = true;
        [ProtoMember(4)] public bool AllowPedestrianCrossing { get; set; } = true;
        [ProtoMember(5)]
        public bool AllowTurningOnRed
        {
            get => AllowNearTurnOnRed && AllowFarTurnOnRed;
            set
            {
                AllowNearTurnOnRed = value;
                AllowFarTurnOnRed = value;
            }
        }

        private bool _allowNearTurnOnRed = true;
        private bool _allowFarTurnOnRed = true;

        [ProtoMember(6)]
        public bool AllowNearTurnOnRed
        {
            get => _allowNearTurnOnRed;
            set => _allowNearTurnOnRed = value;
        }

        [ProtoMember(7)]
        public bool AllowFarTurnOnRed
        {
            get => _allowFarTurnOnRed;
            set => _allowFarTurnOnRed = value;
        }

        public JunctionRestrictionsState Clone()
        {
            return (JunctionRestrictionsState) MemberwiseClone();
        }

        public bool IsDefault()
        {
            return AllowUTurns &&
                   AllowLaneChangesWhenGoingStraight &&
                   AllowEnterWhenBlocked &&
                   AllowPedestrianCrossing &&
                   AllowNearTurnOnRed &&
                   AllowFarTurnOnRed;
        }

        public override string ToString()
        {
            return $"UTurns={AllowUTurns}, LaneChange={AllowLaneChangesWhenGoingStraight}, Blocked={AllowEnterWhenBlocked}, Pedestrians={AllowPedestrianCrossing}, NearTurnOnRed={AllowNearTurnOnRed}, FarTurnOnRed={AllowFarTurnOnRed}";
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
