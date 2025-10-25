using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Util;
using ColossalFramework;

namespace CSM.TmpeSync.TmpeBridge
{
    internal static partial class TmpeBridgeAdapter
    {
        private static readonly Dictionary<ushort, ParkingRestrictionState> ParkingRestrictions = new Dictionary<ushort, ParkingRestrictionState>();
        private static object ParkingRestrictionsManagerInstance;
        private static MethodInfo ParkingAllowedSetMethod;
        private static MethodInfo ParkingAllowedGetMethod;
        private static MethodInfo ParkingMayHaveMethod;
        private static MethodInfo ParkingMayHaveDirectionMethod;

        private enum ParkingRestrictionApplyOutcome
        {
            None,
            Success,
            Partial,
            Fatal
        }

        private static bool InitialiseParkingRestrictionBridge(Assembly tmpeAssembly)
        {
            ParkingRestrictionsManagerInstance = null;
            ParkingAllowedSetMethod = null;
            ParkingAllowedGetMethod = null;
            ParkingMayHaveMethod = null;
            ParkingMayHaveDirectionMethod = null;

            try
            {
                var managerType = tmpeAssembly?.GetType("TrafficManager.Manager.Impl.ParkingRestrictionsManager");
                var manager = GetManagerFromFactory("ParkingRestrictionsManager", "Parking Restrictions");

                if (manager != null)
                    managerType = manager.GetType();
                else if (managerType != null)
                    manager = TryGetStaticInstance(managerType, "Parking Restrictions");

                if (managerType == null)
                    LogBridgeGap("Parking Restrictions", "type", "TrafficManager.Manager.Impl.ParkingRestrictionsManager");

                ParkingRestrictionsManagerInstance = manager;

                if (managerType != null)
                {
                    ParkingAllowedSetMethod = managerType.GetMethod(
                        "SetParkingAllowed",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new[] { typeof(ushort), typeof(NetInfo.Direction), typeof(bool) },
                        null);

                    ParkingAllowedGetMethod = managerType.GetMethod(
                        "IsParkingAllowed",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new[] { typeof(ushort), typeof(NetInfo.Direction) },
                        null);

                    ParkingMayHaveMethod = managerType.GetMethod(
                        "MayHaveParkingRestriction",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new[] { typeof(ushort) },
                        null);

                    ParkingMayHaveDirectionMethod = managerType.GetMethod(
                        "MayHaveParkingRestriction",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new[] { typeof(ushort), typeof(NetInfo.Direction) },
                        null);
                }
            }
            catch (Exception ex)
            {
                RecordFeatureGapDetail("parkingRestrictions", "exception", ex.GetType().Name);
                Log.Warn(LogCategory.Bridge, "TM:PE parking restriction bridge initialization failed | error={0}", ex);
            }

            var supported = ParkingRestrictionsManagerInstance != null &&
                            ParkingAllowedSetMethod != null &&
                            ParkingAllowedGetMethod != null;
            SetFeatureStatus("parkingRestrictions", supported, null);
            return supported;
        }

        internal static bool ApplyParkingRestriction(ushort segmentId, ParkingRestrictionState state)
        {
            try
            {
                var desired = state?.Clone() ?? new ParkingRestrictionState();

                if (SupportsParkingRestrictions)
                {
                    Log.Debug(LogCategory.Hook, "TM:PE parking restriction request | segmentId={0} state={1}", segmentId, desired);
                    var outcome = TryApplyParkingRestrictionReal(segmentId, desired.Clone(), out var appliedDirections, out var rejectedDirections);

                    if (outcome == ParkingRestrictionApplyOutcome.Fatal)
                    {
                        Log.Warn(LogCategory.Bridge, "TM:PE parking restriction apply via API failed | segmentId={0} action=fatal", segmentId);
                        return false;
                    }

                    UpdateParkingRestrictionStub(segmentId, rejectedDirections, appliedDirections);

                    if (TryGetParkingRestrictionReal(segmentId, out var liveState) && ParkingRestrictionMatches(desired, liveState))
                        return true;

                    return false;
                }

                Log.Info(LogCategory.Synchronization, "TM:PE parking restriction stored in stub | segmentId={0} state={1}", segmentId, desired);
                UpdateParkingRestrictionStub(segmentId, desired, null);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE ApplyParkingRestriction failed | error={0}", ex);
                return false;
            }
        }

        internal static bool TryGetParkingRestriction(ushort segmentId, out ParkingRestrictionState state)
        {
            try
            {
                if (SupportsParkingRestrictions && TryGetParkingRestrictionReal(segmentId, out state))
                {
                    Log.Debug(LogCategory.Hook, "TM:PE parking restriction query | segmentId={0} state={1}", segmentId, state);
                    UpdateParkingRestrictionStub(segmentId, state, null);
                    return true;
                }

                lock (StateLock)
                {
                    if (!ParkingRestrictions.TryGetValue(segmentId, out var stored))
                        state = new ParkingRestrictionState();
                    else
                        state = stored.Clone();
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE TryGetParkingRestriction failed | error={0}", ex);
                state = new ParkingRestrictionState();
                return false;
            }
        }

        private static bool TryDescribeParkingLanes(ushort segmentId, out bool hasForward, out bool hasBackward)
        {
            hasForward = false;
            hasBackward = false;

            if (!NetworkUtil.SegmentExists(segmentId))
                return false;

            ref var segment = ref NetManager.instance.m_segments.m_buffer[(int)segmentId];
            var info = segment.Info;
            if (info?.m_lanes == null)
                return true;

            foreach (var lane in info.m_lanes)
            {
                if ((lane.m_laneType & NetInfo.LaneType.Parking) == 0)
                    continue;

                switch (lane.m_finalDirection)
                {
                    case NetInfo.Direction.Forward:
                        hasForward = true;
                        break;
                    case NetInfo.Direction.Backward:
                        hasBackward = true;
                        break;
                    case NetInfo.Direction.Both:
                    case NetInfo.Direction.AvoidBoth:
                        hasForward = true;
                        hasBackward = true;
                        break;
                }

                if (hasForward && hasBackward)
                    return true;
            }

            return true;
        }

        private static ParkingRestrictionApplyOutcome TryApplyParkingRestrictionReal(
            ushort segmentId,
            ParkingRestrictionState state,
            out ParkingRestrictionState appliedDirections,
            out ParkingRestrictionState rejectedDirections)
        {
            appliedDirections = new ParkingRestrictionState();
            rejectedDirections = new ParkingRestrictionState();

            if (!state.HasAnyValue())
                return ParkingRestrictionApplyOutcome.None;

            if (ParkingRestrictionsManagerInstance == null || ParkingAllowedSetMethod == null)
                return ParkingRestrictionApplyOutcome.Fatal;

            if (!TryDescribeParkingLanes(segmentId, out var hasForwardLane, out var hasBackwardLane))
            {
                Log.Warn(LogCategory.Bridge, "TM:PE parking restriction rejected – segment missing | segmentId={0}", segmentId);
                return ParkingRestrictionApplyOutcome.Fatal;
            }

            bool? segmentSupportsRestrictions = null;
            if (ParkingMayHaveMethod != null)
            {
                segmentSupportsRestrictions = Convert.ToBoolean(
                    ParkingMayHaveMethod.Invoke(ParkingRestrictionsManagerInstance, new object[] { segmentId }));
            }

            bool? forwardConfigurable = null;
            bool? backwardConfigurable = null;

            if (ParkingMayHaveDirectionMethod != null)
            {
                forwardConfigurable = Convert.ToBoolean(
                    ParkingMayHaveDirectionMethod.Invoke(
                        ParkingRestrictionsManagerInstance,
                        new object[] { segmentId, NetInfo.Direction.Forward }));

                backwardConfigurable = Convert.ToBoolean(
                    ParkingMayHaveDirectionMethod.Invoke(
                        ParkingRestrictionsManagerInstance,
                        new object[] { segmentId, NetInfo.Direction.Backward }));
            }

            var forwardAttempted = state.AllowParkingForward.HasValue;
            var backwardAttempted = state.AllowParkingBackward.HasValue;

            var forwardFailed = false;
            var backwardFailed = false;
            var forwardApplied = false;
            var backwardApplied = false;

            bool EvaluateDirection(NetInfo.Direction direction, bool desired, bool hasLane, bool? configurable)
            {
                if (segmentSupportsRestrictions == false)
                {
                    if (desired)
                        return true;

                    Log.Warn(LogCategory.Bridge, "TM:PE parking restriction rejected – segment has no configurable parking | segmentId={0} direction={1}", segmentId, direction);
                    return false;
                }

                if (!hasLane)
                {
                    if (desired)
                        return true;

                    Log.Warn(LogCategory.Bridge, "TM:PE parking restriction rejected – no parking lane for direction | segmentId={0} direction={1}", segmentId, direction);
                    return false;
                }

                if (configurable == false)
                {
                    if (desired)
                        return true;

                    Log.Warn(LogCategory.Bridge, "TM:PE parking restriction rejected – direction unsupported | segmentId={0} direction={1}", segmentId, direction);
                    return false;
                }

                var result = Convert.ToBoolean(
                    ParkingAllowedSetMethod.Invoke(
                        ParkingRestrictionsManagerInstance,
                        new object[] { segmentId, direction, desired }));

                if (!result)
                {
                    Log.Warn(LogCategory.Bridge, "TM:PE parking restriction apply returned false | segmentId={0} direction={1}", segmentId, direction);
                }

                return result;
            }

            if (forwardAttempted)
            {
                var desired = state.AllowParkingForward.Value;
                var success = EvaluateDirection(NetInfo.Direction.Forward, desired, hasForwardLane, forwardConfigurable);
                forwardFailed = !success;
                forwardApplied = success;
            }

            if (backwardAttempted)
            {
                var desired = state.AllowParkingBackward.Value;
                var success = EvaluateDirection(NetInfo.Direction.Backward, desired, hasBackwardLane, backwardConfigurable);
                backwardFailed = !success;
                backwardApplied = success;
            }

            if (forwardAttempted)
            {
                if (!forwardFailed && forwardApplied)
                    appliedDirections.AllowParkingForward = state.AllowParkingForward;
                else
                    rejectedDirections.AllowParkingForward = state.AllowParkingForward;
            }

            if (backwardAttempted)
            {
                if (!backwardFailed && backwardApplied)
                    appliedDirections.AllowParkingBackward = state.AllowParkingBackward;
                else
                    rejectedDirections.AllowParkingBackward = state.AllowParkingBackward;
            }

            if (appliedDirections.HasAnyValue())
            {
                if (TryGetParkingRestrictionReal(segmentId, out var liveState))
                {
                    ValidateAppliedParkingRestrictions(state, liveState, appliedDirections, rejectedDirections);
                }
                else
                {
                    Log.Warn(LogCategory.Bridge, "TM:PE parking restriction verification unavailable | segmentId={0}", segmentId);
                    MoveAppliedParkingToRejected(appliedDirections, rejectedDirections);
                }
            }

            var anyApplied = appliedDirections.HasAnyValue();
            var anyRejected = rejectedDirections.HasAnyValue();

            if (!anyApplied && !anyRejected)
                return ParkingRestrictionApplyOutcome.None;

            if (anyApplied && !anyRejected)
                return ParkingRestrictionApplyOutcome.Success;

            return ParkingRestrictionApplyOutcome.Partial;
        }

        private static bool TryGetParkingRestrictionReal(ushort segmentId, out ParkingRestrictionState state)
        {
            state = new ParkingRestrictionState();

            if (ParkingRestrictionsManagerInstance == null || ParkingAllowedGetMethod == null)
                return false;

            state.AllowParkingForward = (bool)ParkingAllowedGetMethod.Invoke(ParkingRestrictionsManagerInstance, new object[] { segmentId, NetInfo.Direction.Forward });
            state.AllowParkingBackward = (bool)ParkingAllowedGetMethod.Invoke(ParkingRestrictionsManagerInstance, new object[] { segmentId, NetInfo.Direction.Backward });
            return true;
        }

        private static void ValidateAppliedParkingRestrictions(
            ParkingRestrictionState requested,
            ParkingRestrictionState live,
            ParkingRestrictionState applied,
            ParkingRestrictionState rejected)
        {
            void Validate(Func<ParkingRestrictionState, bool?> selector, Action<bool?> applySetter, Action<bool?> rejectSetter)
            {
                var appliedValue = selector(applied);
                if (!appliedValue.HasValue)
                    return;

                var liveValue = selector(live);
                if (!liveValue.HasValue || liveValue.Value != appliedValue.Value)
                {
                    applySetter(null);
                    rejectSetter(selector(requested));
                }
            }

            Validate(s => s.AllowParkingForward, v => applied.AllowParkingForward = v, v => rejected.AllowParkingForward = v);
            Validate(s => s.AllowParkingBackward, v => applied.AllowParkingBackward = v, v => rejected.AllowParkingBackward = v);
        }

        private static void MoveAppliedParkingToRejected(ParkingRestrictionState applied, ParkingRestrictionState rejected)
        {
            void Move(Func<ParkingRestrictionState, bool?> selector, Action<bool?> appliedSetter, Action<bool?> rejectSetter)
            {
                var value = selector(applied);
                if (!value.HasValue)
                    return;

                appliedSetter(null);
                rejectSetter(value);
            }

            Move(s => s.AllowParkingForward, v => applied.AllowParkingForward = v, v => rejected.AllowParkingForward = v);
            Move(s => s.AllowParkingBackward, v => applied.AllowParkingBackward = v, v => rejected.AllowParkingBackward = v);
        }

    }
}
