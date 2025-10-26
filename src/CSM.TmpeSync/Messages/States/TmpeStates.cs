using System;
using ProtoBuf;

#pragma warning disable CS0436 // Allow linked shared types across modular assemblies.

namespace CSM.TmpeSync.Messages.States
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
    public enum SpeedLimitValueType
    {
        [ProtoEnum(Value = 0)] Default = 0,
        [ProtoEnum(Value = 1)] KilometresPerHour = 1,
        [ProtoEnum(Value = 2)] MilesPerHour = 2,
        [ProtoEnum(Value = 3)] Unlimited = 3
    }

    [ProtoContract]
    public class SpeedLimitValue
    {
        [ProtoMember(1)] public SpeedLimitValueType Type { get; set; }

        /// <summary>
        /// Index into the TM:PE speed-limit palette for the corresponding unit.
        /// </summary>
        [ProtoMember(2)] public byte Index { get; set; }

        /// <summary>
        /// Raw kilometres-per-hour representation of the intended limit. Acts as a
        /// resilience channel when the encoded type/index degrade in transit.
        /// </summary>
        [ProtoMember(3)] public float RawSpeedKmh { get; set; }

        /// <summary>
        /// Indicates whether the encoded value is still pending application on TM:PE.
        /// </summary>
        [ProtoMember(4)] public bool Pending { get; set; }

        public override string ToString()
        {
            var baseText = RawSpeedKmh > 0.01f
                ? $"Type={Type} Index={Index} Raw={RawSpeedKmh:0.###} km/h"
                : $"Type={Type} Index={Index}";

            return Pending ? baseText + " Pending=true" : baseText;
        }
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
        [ProtoMember(1)] public bool? AllowUTurns { get; set; }
        [ProtoMember(2)] public bool? AllowLaneChangesWhenGoingStraight { get; set; }
        [ProtoMember(3)] public bool? AllowEnterWhenBlocked { get; set; }
        [ProtoMember(4)] public bool? AllowPedestrianCrossing { get; set; }
        [ProtoMember(5)]
        public bool? AllowTurningOnRed
        {
            get
            {
                if (!AllowNearTurnOnRed.HasValue || !AllowFarTurnOnRed.HasValue)
                    return null;

                return AllowNearTurnOnRed.Value && AllowFarTurnOnRed.Value;
            }
            set
            {
                AllowNearTurnOnRed = value;
                AllowFarTurnOnRed = value;
            }
        }

        private bool? _allowNearTurnOnRed;
        private bool? _allowFarTurnOnRed;

        [ProtoMember(6)]
        public bool? AllowNearTurnOnRed
        {
            get => _allowNearTurnOnRed;
            set => _allowNearTurnOnRed = value;
        }

        [ProtoMember(7)]
        public bool? AllowFarTurnOnRed
        {
            get => _allowFarTurnOnRed;
            set => _allowFarTurnOnRed = value;
        }

        public bool ShouldSerializeAllowUTurns() => AllowUTurns.HasValue;

        public bool ShouldSerializeAllowLaneChangesWhenGoingStraight()
        {
            return AllowLaneChangesWhenGoingStraight.HasValue;
        }

        public bool ShouldSerializeAllowEnterWhenBlocked() => AllowEnterWhenBlocked.HasValue;

        public bool ShouldSerializeAllowPedestrianCrossing() => AllowPedestrianCrossing.HasValue;

        public bool ShouldSerializeAllowTurningOnRed()
        {
            return AllowNearTurnOnRed.HasValue && AllowFarTurnOnRed.HasValue;
        }

        public bool ShouldSerializeAllowNearTurnOnRed() => AllowNearTurnOnRed.HasValue;

        public bool ShouldSerializeAllowFarTurnOnRed() => AllowFarTurnOnRed.HasValue;

        public JunctionRestrictionsState Clone()
        {
            var clone = (JunctionRestrictionsState) MemberwiseClone();
            clone.WireSnapshot = null;
            clone.Pending = Pending?.Clone();
            return clone;
        }

        public bool IsDefault()
        {
            return IsDefaultFlag(AllowUTurns) &&
                   IsDefaultFlag(AllowLaneChangesWhenGoingStraight) &&
                   IsDefaultFlag(AllowEnterWhenBlocked) &&
                   IsDefaultFlag(AllowPedestrianCrossing) &&
                   IsDefaultFlag(AllowNearTurnOnRed) &&
                   IsDefaultFlag(AllowFarTurnOnRed);
        }

        private static bool IsDefaultFlag(bool? value)
        {
            return !value.HasValue || value.Value;
        }

        public bool HasAnyValue()
        {
            return AllowUTurns.HasValue ||
                   AllowLaneChangesWhenGoingStraight.HasValue ||
                   AllowEnterWhenBlocked.HasValue ||
                   AllowPedestrianCrossing.HasValue ||
                   AllowNearTurnOnRed.HasValue ||
                   AllowFarTurnOnRed.HasValue;
        }

        public override string ToString()
        {
            var pendingSuffix = Pending != null && Pending.HasAnyValue()
                ? $" Pending={Pending}"
                : string.Empty;

            return
                $"UTurns={Format(AllowUTurns)}, LaneChange={Format(AllowLaneChangesWhenGoingStraight)}, Blocked={Format(AllowEnterWhenBlocked)}, Pedestrians={Format(AllowPedestrianCrossing)}, NearTurnOnRed={Format(AllowNearTurnOnRed)}, FarTurnOnRed={Format(AllowFarTurnOnRed)}" +
                pendingSuffix;
        }

        private static string Format(bool? value)
        {
            return value.HasValue ? value.Value.ToString() : "<null>";
        }

        /// <summary>
        /// Describes which restriction flags are still pending application.
        /// </summary>
        [ProtoMember(200)]
        public JunctionRestrictionPendingState Pending { get; set; }

        [ProtoMember(100)]
        internal JunctionRestrictionWireSnapshot WireSnapshot { get; private set; }

        [ProtoBeforeSerialization]
        private void OnBeforeSerialize()
        {
            WireSnapshot = JunctionRestrictionWireSnapshot.Create(this);
        }

        [ProtoAfterDeserialization]
        private void OnAfterDeserialize()
        {
            JunctionRestrictionWireSnapshot.Apply(WireSnapshot, this);
            WireSnapshot = null;
        }

        [ProtoAfterSerialization]
        private void OnAfterSerialize()
        {
            WireSnapshot = null;
        }

        [ProtoContract]
        internal sealed class JunctionRestrictionWireSnapshot
        {
            [ProtoMember(1)] public byte SetMask { get; set; }
            [ProtoMember(2)] public byte ValueMask { get; set; }

            private const byte UTurn = 1 << 0;
            private const byte LaneChange = 1 << 1;
            private const byte EnterBlocked = 1 << 2;
            private const byte Pedestrians = 1 << 3;
            private const byte NearTurnOnRed = 1 << 4;
            private const byte FarTurnOnRed = 1 << 5;

            internal static JunctionRestrictionWireSnapshot Create(JunctionRestrictionsState state)
            {
                if (state == null)
                    return null;

                byte setMask = 0;
                byte valueMask = 0;

                Append(ref setMask, ref valueMask, UTurn, state.AllowUTurns);
                Append(ref setMask, ref valueMask, LaneChange, state.AllowLaneChangesWhenGoingStraight);
                Append(ref setMask, ref valueMask, EnterBlocked, state.AllowEnterWhenBlocked);
                Append(ref setMask, ref valueMask, Pedestrians, state.AllowPedestrianCrossing);
                Append(ref setMask, ref valueMask, NearTurnOnRed, state.AllowNearTurnOnRed);
                Append(ref setMask, ref valueMask, FarTurnOnRed, state.AllowFarTurnOnRed);

                if (setMask == 0)
                    return null;

                return new JunctionRestrictionWireSnapshot
                {
                    SetMask = setMask,
                    ValueMask = valueMask
                };
            }

            private static void Append(ref byte setMask, ref byte valueMask, byte flag, bool? value)
            {
                if (!value.HasValue)
                    return;

                setMask |= flag;
                if (value.Value)
                    valueMask |= flag;
            }

            internal static void Apply(JunctionRestrictionWireSnapshot snapshot, JunctionRestrictionsState state)
            {
                if (snapshot == null || state == null)
                    return;

                ApplyFlag(snapshot, state, UTurn, value => state.AllowUTurns = value);
                ApplyFlag(snapshot, state, LaneChange, value => state.AllowLaneChangesWhenGoingStraight = value);
                ApplyFlag(snapshot, state, EnterBlocked, value => state.AllowEnterWhenBlocked = value);
                ApplyFlag(snapshot, state, Pedestrians, value => state.AllowPedestrianCrossing = value);
                ApplyFlag(snapshot, state, NearTurnOnRed, value => state.AllowNearTurnOnRed = value);
                ApplyFlag(snapshot, state, FarTurnOnRed, value => state.AllowFarTurnOnRed = value);
            }

            private static void ApplyFlag(
                JunctionRestrictionWireSnapshot snapshot,
                JunctionRestrictionsState state,
                byte flag,
                Action<bool?> setter)
            {
                if ((snapshot.SetMask & flag) == 0)
                {
                    setter(null);
                    return;
                }

                var value = (snapshot.ValueMask & flag) != 0;
                setter(value);
            }
        }
    }

    [ProtoContract]
    public class JunctionRestrictionPendingState
    {
        [ProtoMember(1)] public bool? AllowUTurns { get; set; }
        [ProtoMember(2)] public bool? AllowLaneChangesWhenGoingStraight { get; set; }
        [ProtoMember(3)] public bool? AllowEnterWhenBlocked { get; set; }
        [ProtoMember(4)] public bool? AllowPedestrianCrossing { get; set; }
        [ProtoMember(5)] public bool? AllowNearTurnOnRed { get; set; }
        [ProtoMember(6)] public bool? AllowFarTurnOnRed { get; set; }

        internal bool HasAnyValue()
        {
            return AllowUTurns.HasValue ||
                   AllowLaneChangesWhenGoingStraight.HasValue ||
                   AllowEnterWhenBlocked.HasValue ||
                   AllowPedestrianCrossing.HasValue ||
                   AllowNearTurnOnRed.HasValue ||
                   AllowFarTurnOnRed.HasValue;
        }

        public JunctionRestrictionPendingState Clone()
        {
            return (JunctionRestrictionPendingState)MemberwiseClone();
        }

        public override string ToString()
        {
            return
                $"UTurns={Format(AllowUTurns)}, LaneChange={Format(AllowLaneChangesWhenGoingStraight)}, Blocked={Format(AllowEnterWhenBlocked)}, Pedestrians={Format(AllowPedestrianCrossing)}, NearTurnOnRed={Format(AllowNearTurnOnRed)}, FarTurnOnRed={Format(AllowFarTurnOnRed)}";
        }

        private static string Format(bool? value)
        {
            return value.HasValue ? value.Value.ToString() : "<null>";
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
        [ProtoMember(1)] public bool? AllowParkingForward { get; set; }
        [ProtoMember(2)] public bool? AllowParkingBackward { get; set; }

        public bool AllowParkingBothDirections => IsParkingAllowed(AllowParkingForward) && IsParkingAllowed(AllowParkingBackward);

        private static bool IsParkingAllowed(bool? value)
        {
            return !value.HasValue || value.Value;
        }

        public bool HasAnyValue()
        {
            return AllowParkingForward.HasValue || AllowParkingBackward.HasValue;
        }

        public bool IsDefault()
        {
            return AllowParkingBothDirections;
        }

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
            return $"Forward={Format(AllowParkingForward)}, Backward={Format(AllowParkingBackward)}";
        }

        private static string Format(bool? value)
        {
            return value.HasValue ? value.Value.ToString() : "<null>";
        }
    }


}

#pragma warning restore CS0436
