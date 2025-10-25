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
        private static readonly Dictionary<ushort, JunctionRestrictionsState> JunctionRestrictions = new Dictionary<ushort, JunctionRestrictionsState>();
        private static object JunctionRestrictionsManagerInstance;
        private static MethodInfo SetUturnAllowedMethod;
        private static MethodInfo SetNearTurnOnRedAllowedMethod;
        private static MethodInfo SetFarTurnOnRedAllowedMethod;
        private static MethodInfo SetLaneChangingAllowedMethod;
        private static MethodInfo SetEnteringBlockedMethod;
        private static MethodInfo SetPedestrianCrossingMethod;
        private static MethodInfo IsUturnAllowedMethod;
        private static MethodInfo IsNearTurnOnRedAllowedMethod;
        private static MethodInfo IsFarTurnOnRedAllowedMethod;
        private static MethodInfo IsLaneChangingAllowedMethod;
        private static MethodInfo IsEnteringBlockedMethod;
        private static MethodInfo IsPedestrianCrossingAllowedMethod;
        private static MethodInfo MayHaveJunctionRestrictionsMethod;
        private static MethodInfo HasJunctionRestrictionsMethod;

        private enum JunctionRestrictionApplyOutcome
        {
            None,
            Success,
            Partial,
            Fatal
        }

        private static bool InitialiseJunctionRestrictionsBridge(Assembly tmpeAssembly)
        {
            JunctionRestrictionsManagerInstance = null;
            SetUturnAllowedMethod = null;
            SetNearTurnOnRedAllowedMethod = null;
            SetFarTurnOnRedAllowedMethod = null;
            SetLaneChangingAllowedMethod = null;
            SetEnteringBlockedMethod = null;
            SetPedestrianCrossingMethod = null;
            IsUturnAllowedMethod = null;
            IsNearTurnOnRedAllowedMethod = null;
            IsFarTurnOnRedAllowedMethod = null;
            IsLaneChangingAllowedMethod = null;
            IsEnteringBlockedMethod = null;
            IsPedestrianCrossingAllowedMethod = null;
            MayHaveJunctionRestrictionsMethod = null;
            HasJunctionRestrictionsMethod = null;

            try
            {
                var managerType = tmpeAssembly?.GetType("TrafficManager.Manager.Impl.JunctionRestrictionsManager");
                var manager = GetManagerFromFactory("JunctionRestrictionsManager", "Junction Restrictions");

                if (manager != null)
                    managerType = manager.GetType();
                else if (managerType != null)
                {
                    var instanceProperty = managerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    manager = instanceProperty?.GetValue(null, null);
                }

                if (managerType == null)
                    LogBridgeGap("Junction Restrictions", "type", "TrafficManager.Manager.Impl.JunctionRestrictionsManager");

                JunctionRestrictionsManagerInstance = manager;

                if (managerType != null)
                {
                    SetUturnAllowedMethod = managerType.GetMethod("SetUturnAllowed", new[] { typeof(ushort), typeof(bool), typeof(bool) });
                    SetNearTurnOnRedAllowedMethod = managerType.GetMethod("SetNearTurnOnRedAllowed", new[] { typeof(ushort), typeof(bool), typeof(bool) });
                    SetFarTurnOnRedAllowedMethod = managerType.GetMethod("SetFarTurnOnRedAllowed", new[] { typeof(ushort), typeof(bool), typeof(bool) });
                    SetLaneChangingAllowedMethod = managerType.GetMethod("SetLaneChangingAllowedWhenGoingStraight", new[] { typeof(ushort), typeof(bool), typeof(bool) });
                    SetEnteringBlockedMethod = managerType.GetMethod("SetEnteringBlockedJunctionAllowed", new[] { typeof(ushort), typeof(bool), typeof(bool) });
                    SetPedestrianCrossingMethod = managerType.GetMethod("SetPedestrianCrossingAllowed", new[] { typeof(ushort), typeof(bool), typeof(bool) });

                    IsUturnAllowedMethod = managerType.GetMethod("IsUturnAllowed", new[] { typeof(ushort), typeof(bool) });
                    IsNearTurnOnRedAllowedMethod = managerType.GetMethod("IsNearTurnOnRedAllowed", new[] { typeof(ushort), typeof(bool) });
                    IsFarTurnOnRedAllowedMethod = managerType.GetMethod("IsFarTurnOnRedAllowed", new[] { typeof(ushort), typeof(bool) });
                    IsLaneChangingAllowedMethod = managerType.GetMethod("IsLaneChangingAllowedWhenGoingStraight", new[] { typeof(ushort), typeof(bool) });
                    IsEnteringBlockedMethod = managerType.GetMethod("IsEnteringBlockedJunctionAllowed", new[] { typeof(ushort), typeof(bool) });
                    IsPedestrianCrossingAllowedMethod = managerType.GetMethod("IsPedestrianCrossingAllowed", new[] { typeof(ushort), typeof(bool) });
                    MayHaveJunctionRestrictionsMethod = managerType.GetMethod(
                        "MayHaveJunctionRestrictions",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(ushort) },
                        null);
                    if (MayHaveJunctionRestrictionsMethod == null)
                        LogBridgeGap("Junction Restrictions", "MayHaveJunctionRestrictions", DescribeMethodOverloads(managerType, "MayHaveJunctionRestrictions"));

                    HasJunctionRestrictionsMethod = managerType.GetMethod(
                        "HasJunctionRestrictions",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(ushort) },
                        null);
                    if (HasJunctionRestrictionsMethod == null)
                        LogBridgeGap("Junction Restrictions", "HasJunctionRestrictions", DescribeMethodOverloads(managerType, "HasJunctionRestrictions"));
                }
            }
            catch (Exception ex)
            {
                RecordFeatureGapDetail("junctionRestrictions", "exception", ex.GetType().Name);
                Log.Warn(LogCategory.Bridge, "TM:PE junction restrictions bridge initialization failed | error={0}", ex);
            }

            var supported = JunctionRestrictionsManagerInstance != null &&
                            SetUturnAllowedMethod != null &&
                            SetNearTurnOnRedAllowedMethod != null &&
                            SetFarTurnOnRedAllowedMethod != null &&
                            SetLaneChangingAllowedMethod != null &&
                            SetEnteringBlockedMethod != null &&
                            SetPedestrianCrossingMethod != null &&
                            IsUturnAllowedMethod != null &&
                            IsNearTurnOnRedAllowedMethod != null &&
                            IsFarTurnOnRedAllowedMethod != null &&
                            IsLaneChangingAllowedMethod != null &&
                            IsEnteringBlockedMethod != null &&
                            IsPedestrianCrossingAllowedMethod != null;
            SetFeatureStatus("junctionRestrictions", supported, null);
            return supported;
        }

        internal static bool ApplyJunctionRestrictions(ushort nodeId, JunctionRestrictionsState state)
        {
            try
            {
                var desired = state?.Clone() ?? new JunctionRestrictionsState();
                var storeDesiredInStub = false;
                var observerHash = ComputeNodeObserverHash(nodeId);

                if (SupportsJunctionRestrictions)
                {
                    Log.Debug(LogCategory.Hook, "TM:PE junction restriction request | nodeId={0} state={1}", nodeId, desired);

                    PendingMap.UpsertNode(nodeId, desired, observerHash, "apply");
                    var outcome = TryApplyJunctionRestrictionsReal(nodeId, desired, out var appliedFlags, out var rejectedFlags);

                    if (outcome == JunctionRestrictionApplyOutcome.Fatal)
                    {
                        Log.Warn(LogCategory.Bridge, "TM:PE junction restriction apply via API failed | nodeId={0} action=fallback_to_stub", nodeId);
                        PendingMap.DropNode(nodeId);
                        return false;
                    }

                    UpdateJunctionRestrictionStub(nodeId, rejectedFlags, appliedFlags);

                    if (outcome == JunctionRestrictionApplyOutcome.Success)
                    {
                        if (desired.HasAnyValue())
                            PendingMap.MarkNodeApplied(nodeId, desired);
                        else
                            PendingMap.ClearNode(nodeId, "clear");
                        return true;
                    }

                    if (outcome == JunctionRestrictionApplyOutcome.Partial)
                    {
                        if (appliedFlags != null && appliedFlags.HasAnyValue())
                            PendingMap.MarkNodeApplied(nodeId, appliedFlags);

                        if (rejectedFlags != null && rejectedFlags.HasAnyValue())
                            PendingMap.ReportNodeRejection(nodeId, rejectedFlags, "partial", observerHash);
                        else if (desired.HasAnyValue())
                            PendingMap.ReportNodeRejection(nodeId, desired, "partial", observerHash);

                        return true;
                    }

                    if (desired.HasAnyValue())
                        PendingMap.ReportNodeRejection(nodeId, desired, "no_effect", observerHash);

                    storeDesiredInStub = true;
                }
                else
                {
                    Log.Info(LogCategory.Synchronization, "TM:PE junction restrictions stored in stub | nodeId={0} state={1}", nodeId, desired);
                    storeDesiredInStub = true;
                }

                if (storeDesiredInStub)
                    UpdateJunctionRestrictionStub(nodeId, desired, null);

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE ApplyJunctionRestrictions failed | error={0}", ex);
                return false;
            }
        }

        internal static bool TryGetJunctionRestrictions(ushort nodeId, out JunctionRestrictionsState state)
        {
            try
            {
                if (SupportsJunctionRestrictions && TryGetJunctionRestrictionsReal(nodeId, out state))
                {
                    Log.Debug(LogCategory.Hook, "TM:PE junction restriction query | nodeId={0} state={1}", nodeId, state);
                    PendingMap.OverlayPending(nodeId, state);
                    return true;
                }

                lock (StateLock)
                {
                    if (!JunctionRestrictions.TryGetValue(nodeId, out var stored))
                        state = new JunctionRestrictionsState();
                    else
                        state = stored.Clone();
                }

                PendingMap.OverlayPending(nodeId, state);

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE TryGetJunctionRestrictions failed | error={0}", ex);
                state = new JunctionRestrictionsState();
                return false;
            }
        }

        private static void UpdateJunctionRestrictionStub(ushort nodeId, JunctionRestrictionsState valuesToStore, JunctionRestrictionsState valuesToClear)
        {
            lock (StateLock)
            {
                JunctionRestrictionsState existing = null;
                if (JunctionRestrictions.TryGetValue(nodeId, out var stored) && stored != null)
                    existing = stored.Clone();

                var merged = MergeJunctionRestrictionStub(existing, valuesToStore, valuesToClear);

                if (merged != null)
                    PendingMap.OverlayPending(nodeId, merged);

                if (merged == null || !merged.HasAnyValue() || merged.IsDefault())
                    JunctionRestrictions.Remove(nodeId);
                else
                    JunctionRestrictions[nodeId] = merged;
            }
        }

        private static JunctionRestrictionsState MergeJunctionRestrictionStub(JunctionRestrictionsState existing, JunctionRestrictionsState valuesToStore, JunctionRestrictionsState valuesToClear)
        {
            var result = existing?.Clone() ?? new JunctionRestrictionsState();

            if (valuesToClear != null)
            {
                if (valuesToClear.AllowUTurns.HasValue)
                    result.AllowUTurns = null;
                if (valuesToClear.AllowLaneChangesWhenGoingStraight.HasValue)
                    result.AllowLaneChangesWhenGoingStraight = null;
                if (valuesToClear.AllowEnterWhenBlocked.HasValue)
                    result.AllowEnterWhenBlocked = null;
                if (valuesToClear.AllowPedestrianCrossing.HasValue)
                    result.AllowPedestrianCrossing = null;
                if (valuesToClear.AllowNearTurnOnRed.HasValue)
                    result.AllowNearTurnOnRed = null;
                if (valuesToClear.AllowFarTurnOnRed.HasValue)
                    result.AllowFarTurnOnRed = null;
            }

            if (valuesToStore != null)
            {
                if (valuesToStore.AllowUTurns.HasValue)
                    result.AllowUTurns = valuesToStore.AllowUTurns;
                if (valuesToStore.AllowLaneChangesWhenGoingStraight.HasValue)
                    result.AllowLaneChangesWhenGoingStraight = valuesToStore.AllowLaneChangesWhenGoingStraight;
                if (valuesToStore.AllowEnterWhenBlocked.HasValue)
                    result.AllowEnterWhenBlocked = valuesToStore.AllowEnterWhenBlocked;
                if (valuesToStore.AllowPedestrianCrossing.HasValue)
                    result.AllowPedestrianCrossing = valuesToStore.AllowPedestrianCrossing;
                if (valuesToStore.AllowNearTurnOnRed.HasValue)
                    result.AllowNearTurnOnRed = valuesToStore.AllowNearTurnOnRed;
                if (valuesToStore.AllowFarTurnOnRed.HasValue)
                    result.AllowFarTurnOnRed = valuesToStore.AllowFarTurnOnRed;
            }

            return result.HasAnyValue() ? result : null;
        }

        private static bool NodeSupportsJunctionRestrictions(ushort nodeId, out string detail, out bool shouldRetry)
        {
            detail = null;
            shouldRetry = false;

            if (!NetworkUtil.NodeExists(nodeId))
            {
                detail = "node_missing";
                shouldRetry = true;
                return false;
            }

            ref var node = ref NetManager.instance.m_nodes.m_buffer[nodeId];
            if ((node.m_flags & NetNode.Flags.Created) == NetNode.Flags.None)
            {
                detail = "node_not_created";
                shouldRetry = true;
                return false;
            }

            var tmpeDeniedSupport = false;

            if (MayHaveJunctionRestrictionsMethod != null && JunctionRestrictionsManagerInstance != null)
            {
                try
                {
                    var result = MayHaveJunctionRestrictionsMethod.Invoke(JunctionRestrictionsManagerInstance, new object[] { nodeId });
                    if (result is bool allowed)
                    {
                        tmpeDeniedSupport = !allowed;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn(LogCategory.Diagnostics, "TM:PE MayHaveJunctionRestrictions probe failed | nodeId={0} error={1}", nodeId, ex);
                }
            }

            var eligibleFlags = NetNode.Flags.Junction | NetNode.Flags.Bend | NetNode.Flags.Transition;
            var usedFallback = false;

            if ((node.m_flags & eligibleFlags) == NetNode.Flags.None)
            {
                if (!tmpeDeniedSupport)
                {
                    detail = "node_not_junction_transition_or_bend";
                    return false;
                }

                usedFallback = true;
            }

            for (int i = 0; i < 8; i++)
            {
                if (node.GetSegment(i) != 0)
                {
                    if (tmpeDeniedSupport)
                        Log.Debug(LogCategory.Diagnostics, "TM:PE MayHaveJunctionRestrictions returned false, using fallback heuristics | nodeId={0}", nodeId);

                    return true;
                }
            }

            if (tmpeDeniedSupport || usedFallback)
            {
                detail = "tmpe_may_have_junction_restrictions=false";
                shouldRetry = true;
            }
            else
            {
                detail = "node_has_no_segments";
            }

            return false;
        }

        private static JunctionRestrictionApplyOutcome TryApplyJunctionRestrictionsReal(
            ushort nodeId,
            JunctionRestrictionsState state,
            out JunctionRestrictionsState appliedFlags,
            out JunctionRestrictionsState rejectedFlags)
        {
            appliedFlags = new JunctionRestrictionsState();
            rejectedFlags = new JunctionRestrictionsState();

            if (!state.HasAnyValue())
                return JunctionRestrictionApplyOutcome.None;

            if (JunctionRestrictionsManagerInstance == null ||
                SetUturnAllowedMethod == null ||
                SetNearTurnOnRedAllowedMethod == null ||
                SetFarTurnOnRedAllowedMethod == null ||
                SetLaneChangingAllowedMethod == null ||
                SetEnteringBlockedMethod == null ||
                SetPedestrianCrossingMethod == null)
            {
                return JunctionRestrictionApplyOutcome.Fatal;
            }

            if (!NodeSupportsJunctionRestrictions(nodeId, out var rejectionDetail, out var retryableFatal))
            {
                var detailText = string.IsNullOrEmpty(rejectionDetail) ? "<unspecified>" : rejectionDetail;
                Log.Warn(LogCategory.Bridge, "TM:PE junction restriction apply aborted | nodeId={0} detail={1}", nodeId, detailText);
                return retryableFatal ? JunctionRestrictionApplyOutcome.None : JunctionRestrictionApplyOutcome.Fatal;
            }

            ref var node = ref NetManager.instance.m_nodes.m_buffer[(int)nodeId];
            var anySegment = false;

            var allowUturnAttempted = state.AllowUTurns.HasValue;
            var allowLaneChangeAttempted = state.AllowLaneChangesWhenGoingStraight.HasValue;
            var allowEnterBlockedAttempted = state.AllowEnterWhenBlocked.HasValue;
            var allowPedestrianAttempted = state.AllowPedestrianCrossing.HasValue;
            var allowNearTurnAttempted = state.AllowNearTurnOnRed.HasValue;
            var allowFarTurnAttempted = state.AllowFarTurnOnRed.HasValue;

            var allowUturnFailed = false;
            var allowLaneChangeFailed = false;
            var allowEnterBlockedFailed = false;
            var allowPedestrianFailed = false;
            var allowNearTurnFailed = false;
            var allowFarTurnFailed = false;

            var allowUturnApplied = false;
            var allowLaneChangeApplied = false;
            var allowEnterBlockedApplied = false;
            var allowPedestrianApplied = false;
            var allowNearTurnApplied = false;
            var allowFarTurnApplied = false;

            for (int i = 0; i < 8; i++)
            {
                var segmentId = node.GetSegment(i);
                if (segmentId == 0)
                    continue;

                ref var segment = ref NetManager.instance.m_segments.m_buffer[(int)segmentId];
                if ((segment.m_flags & NetSegment.Flags.Created) == NetSegment.Flags.None)
                    continue;

                var startNode = segment.m_startNode == nodeId;
                anySegment = true;

                if (allowUturnAttempted)
                {
                    var success = InvokeJunctionRestrictionSetter(
                        SetUturnAllowedMethod,
                        "AllowUTurns",
                        segmentId,
                        startNode,
                        state.AllowUTurns.Value);
                    allowUturnFailed |= !success;
                    allowUturnApplied |= success;
                }

                if (allowNearTurnAttempted)
                {
                    var success = InvokeJunctionRestrictionSetter(
                        SetNearTurnOnRedAllowedMethod,
                        "AllowNearTurnOnRed",
                        segmentId,
                        startNode,
                        state.AllowNearTurnOnRed.Value);
                    allowNearTurnFailed |= !success;
                    allowNearTurnApplied |= success;
                }

                if (allowFarTurnAttempted)
                {
                    var success = InvokeJunctionRestrictionSetter(
                        SetFarTurnOnRedAllowedMethod,
                        "AllowFarTurnOnRed",
                        segmentId,
                        startNode,
                        state.AllowFarTurnOnRed.Value);
                    allowFarTurnFailed |= !success;
                    allowFarTurnApplied |= success;
                }

                if (allowLaneChangeAttempted)
                {
                    var success = InvokeJunctionRestrictionSetter(
                        SetLaneChangingAllowedMethod,
                        "AllowLaneChangesWhenGoingStraight",
                        segmentId,
                        startNode,
                        state.AllowLaneChangesWhenGoingStraight.Value);
                    allowLaneChangeFailed |= !success;
                    allowLaneChangeApplied |= success;
                }

                if (allowEnterBlockedAttempted)
                {
                    var success = InvokeJunctionRestrictionSetter(
                        SetEnteringBlockedMethod,
                        "AllowEnterWhenBlocked",
                        segmentId,
                        startNode,
                        state.AllowEnterWhenBlocked.Value);
                    allowEnterBlockedFailed |= !success;
                    allowEnterBlockedApplied |= success;
                }

                if (allowPedestrianAttempted)
                {
                    var success = InvokeJunctionRestrictionSetter(
                        SetPedestrianCrossingMethod,
                        "AllowPedestrianCrossing",
                        segmentId,
                        startNode,
                        state.AllowPedestrianCrossing.Value);
                    allowPedestrianFailed |= !success;
                    allowPedestrianApplied |= success;
                }
            }

            if (!anySegment)
                return JunctionRestrictionApplyOutcome.Fatal;

            if (allowUturnAttempted)
            {
                if (!allowUturnFailed && allowUturnApplied)
                    appliedFlags.AllowUTurns = state.AllowUTurns;
                else
                    rejectedFlags.AllowUTurns = state.AllowUTurns;
            }

            if (allowLaneChangeAttempted)
            {
                if (!allowLaneChangeFailed && allowLaneChangeApplied)
                    appliedFlags.AllowLaneChangesWhenGoingStraight = state.AllowLaneChangesWhenGoingStraight;
                else
                    rejectedFlags.AllowLaneChangesWhenGoingStraight = state.AllowLaneChangesWhenGoingStraight;
            }

            if (allowEnterBlockedAttempted)
            {
                if (!allowEnterBlockedFailed && allowEnterBlockedApplied)
                    appliedFlags.AllowEnterWhenBlocked = state.AllowEnterWhenBlocked;
                else
                    rejectedFlags.AllowEnterWhenBlocked = state.AllowEnterWhenBlocked;
            }

            if (allowPedestrianAttempted)
            {
                if (!allowPedestrianFailed && allowPedestrianApplied)
                    appliedFlags.AllowPedestrianCrossing = state.AllowPedestrianCrossing;
                else
                    rejectedFlags.AllowPedestrianCrossing = state.AllowPedestrianCrossing;
            }

            if (allowNearTurnAttempted)
            {
                if (!allowNearTurnFailed && allowNearTurnApplied)
                    appliedFlags.AllowNearTurnOnRed = state.AllowNearTurnOnRed;
                else
                    rejectedFlags.AllowNearTurnOnRed = state.AllowNearTurnOnRed;
            }

            if (allowFarTurnAttempted)
            {
                if (!allowFarTurnFailed && allowFarTurnApplied)
                    appliedFlags.AllowFarTurnOnRed = state.AllowFarTurnOnRed;
                else
                    rejectedFlags.AllowFarTurnOnRed = state.AllowFarTurnOnRed;
            }

            if (appliedFlags.HasAnyValue())
            {
                if (TryGetJunctionRestrictionsReal(nodeId, out var liveState))
                {
                    ValidateAppliedJunctionRestrictions(state, liveState, appliedFlags, rejectedFlags);
                }
                else
                {
                    Log.Warn(LogCategory.Bridge, "TM:PE junction restriction verification unavailable | nodeId={0}", nodeId);
                    MoveAppliedToRejected(appliedFlags, rejectedFlags);
                }
            }

            var anyAppliedFlags = appliedFlags.HasAnyValue();
            var anyRejectedFlags = rejectedFlags.HasAnyValue();

            if (!anyAppliedFlags && !anyRejectedFlags)
                return JunctionRestrictionApplyOutcome.None;

            if (anyAppliedFlags && !anyRejectedFlags)
                return JunctionRestrictionApplyOutcome.Success;

            return JunctionRestrictionApplyOutcome.Partial;
        }

        private static bool TryGetJunctionRestrictionsReal(ushort nodeId, out JunctionRestrictionsState state)
        {
            state = new JunctionRestrictionsState();

            if (JunctionRestrictionsManagerInstance == null ||
                IsUturnAllowedMethod == null ||
                IsNearTurnOnRedAllowedMethod == null ||
                IsFarTurnOnRedAllowedMethod == null ||
                IsLaneChangingAllowedMethod == null ||
                IsEnteringBlockedMethod == null ||
                IsPedestrianCrossingAllowedMethod == null)
            {
                return false;
            }

            ref var node = ref NetManager.instance.m_nodes.m_buffer[(int)nodeId];
            if ((node.m_flags & NetNode.Flags.Created) == NetNode.Flags.None)
                return false;

            if (HasJunctionRestrictionsMethod != null)
            {
                try
                {
                    var hasCustom = (bool)HasJunctionRestrictionsMethod.Invoke(JunctionRestrictionsManagerInstance, new object[] { nodeId });
                    if (!hasCustom)
                    {
                        state = new JunctionRestrictionsState();
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(LogCategory.Diagnostics, "TM:PE HasJunctionRestrictions probe failed | nodeId={0} error={1}", nodeId, ex.GetType().Name);
                }
            }

            var any = false;
            var allowUTurns = true;
            var allowLaneChange = true;
            var allowEnter = true;
            var allowPedestrians = true;
            var allowNear = true;
            var allowFar = true;

            for (int i = 0; i < 8; i++)
            {
                var segmentId = node.GetSegment(i);
                if (segmentId == 0)
                    continue;

                ref var segment = ref NetManager.instance.m_segments.m_buffer[(int)segmentId];
                if ((segment.m_flags & NetSegment.Flags.Created) == NetSegment.Flags.None)
                    continue;

                var startNode = segment.m_startNode == nodeId;

                allowUTurns &= (bool)IsUturnAllowedMethod.Invoke(JunctionRestrictionsManagerInstance, new object[] { segmentId, startNode });
                allowLaneChange &= (bool)IsLaneChangingAllowedMethod.Invoke(JunctionRestrictionsManagerInstance, new object[] { segmentId, startNode });
                allowEnter &= (bool)IsEnteringBlockedMethod.Invoke(JunctionRestrictionsManagerInstance, new object[] { segmentId, startNode });
                allowPedestrians &= (bool)IsPedestrianCrossingAllowedMethod.Invoke(JunctionRestrictionsManagerInstance, new object[] { segmentId, startNode });
                allowNear &= (bool)IsNearTurnOnRedAllowedMethod.Invoke(JunctionRestrictionsManagerInstance, new object[] { segmentId, startNode });
                allowFar &= (bool)IsFarTurnOnRedAllowedMethod.Invoke(JunctionRestrictionsManagerInstance, new object[] { segmentId, startNode });

                any = true;
            }

            if (!any)
                return false;

            state.AllowUTurns = allowUTurns;
            state.AllowLaneChangesWhenGoingStraight = allowLaneChange;
            state.AllowEnterWhenBlocked = allowEnter;
            state.AllowPedestrianCrossing = allowPedestrians;
            state.AllowNearTurnOnRed = allowNear;
            state.AllowFarTurnOnRed = allowFar;
            return true;
        }

        private static bool InvokeJunctionRestrictionSetter(MethodInfo method, string flagName, ushort segmentId, bool startNode, bool value)
        {
            if (method == null)
                return false;

            object result;
            try
            {
                result = method.Invoke(JunctionRestrictionsManagerInstance, new object[] { segmentId, startNode, value });
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, "TM:PE junction restriction apply threw | segmentId={0} startNode={1} flag={2} value={3} error={4}", segmentId, startNode, flagName, value, ex);
                return false;
            }

            if (method.ReturnType == typeof(bool))
            {
                if (!(result is bool success))
                {
                    Log.Warn(LogCategory.Bridge, "TM:PE junction restriction unexpected return | segmentId={0} startNode={1} flag={2}", segmentId, startNode, flagName);
                    return false;
                }

                if (!success)
                {
                    Log.Warn(LogCategory.Bridge, "TM:PE junction restriction rejected | segmentId={0} startNode={1} flag={2} value={3}", segmentId, startNode, flagName, value);
                    return false;
                }
            }

            return true;
        }

        private static void ValidateAppliedJunctionRestrictions(
            JunctionRestrictionsState requested,
            JunctionRestrictionsState live,
            JunctionRestrictionsState applied,
            JunctionRestrictionsState rejected)
        {
            void Validate(Func<JunctionRestrictionsState, bool?> selector, Action<bool?> applySetter, Action<bool?> rejectSetter)
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

            Validate(s => s.AllowUTurns, v => applied.AllowUTurns = v, v => rejected.AllowUTurns = v);
            Validate(s => s.AllowLaneChangesWhenGoingStraight, v => applied.AllowLaneChangesWhenGoingStraight = v, v => rejected.AllowLaneChangesWhenGoingStraight = v);
            Validate(s => s.AllowEnterWhenBlocked, v => applied.AllowEnterWhenBlocked = v, v => rejected.AllowEnterWhenBlocked = v);
            Validate(s => s.AllowPedestrianCrossing, v => applied.AllowPedestrianCrossing = v, v => rejected.AllowPedestrianCrossing = v);
            Validate(s => s.AllowNearTurnOnRed, v => applied.AllowNearTurnOnRed = v, v => rejected.AllowNearTurnOnRed = v);
            Validate(s => s.AllowFarTurnOnRed, v => applied.AllowFarTurnOnRed = v, v => rejected.AllowFarTurnOnRed = v);
        }

    }
}
