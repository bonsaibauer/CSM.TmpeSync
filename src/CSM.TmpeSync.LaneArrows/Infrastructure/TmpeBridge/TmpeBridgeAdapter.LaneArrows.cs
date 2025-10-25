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
        private static readonly Dictionary<uint, LaneArrowFlags> LaneArrows = new Dictionary<uint, LaneArrowFlags>();
        private static object LaneArrowManagerInstance;
        private static MethodInfo LaneArrowSetMethod;
        private static MethodInfo LaneArrowGetMethod;
        private static MethodInfo LaneArrowCanHaveMethod;
        private static Type LaneArrowsEnumType;
        private static int LaneArrowLeftMask;
        private static int LaneArrowForwardMask;
        private static int LaneArrowRightMask;

        private static bool InitialiseLaneArrowBridge(Assembly tmpeAssembly)
        {
            try
            {
                LaneArrowManagerInstance = null;
                LaneArrowSetMethod = null;
                LaneArrowGetMethod = null;
                LaneArrowCanHaveMethod = null;
                LaneArrowsEnumType = null;
                LaneArrowLeftMask = 0;
                LaneArrowForwardMask = 0;
                LaneArrowRightMask = 0;

                var managerType = tmpeAssembly?.GetType("TrafficManager.Manager.Impl.LaneArrowManager");
                var manager = GetManagerFromFactory("LaneArrowManager", "Lane Arrows");

                if (manager != null)
                    managerType = manager.GetType();
                else if (managerType != null)
                    manager = TryGetStaticInstance(managerType, "Lane Arrows");

                if (managerType == null)
                    LogBridgeGap("Lane Arrows", "type", "TrafficManager.Manager.Impl.LaneArrowManager");

                LaneArrowManagerInstance = manager;

                if (managerType != null)
                {
                    foreach (var method in managerType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
                    {
                        if (method.Name != "SetLaneArrows")
                            continue;

                        var parameters = method.GetParameters();
                        if (parameters.Length >= 2 && parameters[0].ParameterType == typeof(uint))
                        {
                            LaneArrowSetMethod = method;
                            break;
                        }
                    }
                    if (LaneArrowSetMethod == null)
                        LogBridgeGap("Lane Arrows", "SetLaneArrows", DescribeMethodOverloads(managerType, "SetLaneArrows"));

                    LaneArrowGetMethod = managerType.GetMethod(
                        "GetFinalLaneArrows",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new[] { typeof(uint) },
                        null);
                    if (LaneArrowGetMethod == null)
                        LogBridgeGap("Lane Arrows", "GetFinalLaneArrows(uint)", DescribeMethodOverloads(managerType, "GetFinalLaneArrows"));
                }

                var contextAssembly = managerType?.Assembly ?? tmpeAssembly;
                LaneArrowsEnumType = ResolveTypeWithContext("TrafficManager.API.Traffic.Enums.LaneArrows", contextAssembly, "Lane Arrows");

                if (LaneArrowsEnumType == null)
                {
                    var enumCandidate = LaneArrowSetMethod?.GetParameters()
                        .Skip(1)
                        .FirstOrDefault()
                        ?.ParameterType;

                    if (enumCandidate?.IsEnum == true)
                        LaneArrowsEnumType = enumCandidate;
                }

                if (LaneArrowsEnumType == null)
                {
                    var enumCandidate = LaneArrowGetMethod?.ReturnType;
                    if (enumCandidate?.IsEnum == true)
                        LaneArrowsEnumType = enumCandidate;
                }

                if (LaneArrowsEnumType != null)
                {
                    try
                    {
                        LaneArrowLeftMask = (int)Enum.Parse(LaneArrowsEnumType, "Left", true);
                        LaneArrowForwardMask = (int)Enum.Parse(LaneArrowsEnumType, "Forward", true);
                        LaneArrowRightMask = (int)Enum.Parse(LaneArrowsEnumType, "Right", true);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(LogCategory.Bridge, "TM:PE lane arrow enum conversion failed | error={0}", ex);
                    }
                }

                if (contextAssembly != null)
                {
                    var flagsType = ResolveTypeWithContext("TrafficManager.State.Flags", contextAssembly, "Lane Arrows");
                    if (flagsType != null)
                    {
                        LaneArrowCanHaveMethod = flagsType.GetMethod(
                            "CanHaveLaneArrows",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                            null,
                            new[] { typeof(uint), typeof(bool?) },
                            null) ?? flagsType.GetMethod(
                            "CanHaveLaneArrows",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                            null,
                            new[] { typeof(uint) },
                            null);

                        if (LaneArrowCanHaveMethod == null)
                            LogBridgeGap("Lane Arrows", "Flags.CanHaveLaneArrows", DescribeMethodOverloads(flagsType, "CanHaveLaneArrows"));
                    }
                }
            }
            catch (Exception ex)
            {
                RecordFeatureGapDetail("laneArrows", "exception", ex.GetType().Name);
                Log.Warn(LogCategory.Bridge, "TM:PE lane arrow bridge initialization failed | error={0}", ex);
            }

            var supported = LaneArrowManagerInstance != null && LaneArrowSetMethod != null && LaneArrowsEnumType != null;
            SetFeatureStatus("laneArrows", supported, null);
            return supported;
        }

        internal static bool ApplyLaneArrows(uint laneId, LaneArrowFlags arrows)
        {
            try
            {
                if (SupportsLaneArrows)
                {
                    Log.Debug(LogCategory.Hook, "TM:PE lane arrow request | laneId={0} arrows={1}", laneId, arrows);
                    if (TryApplyLaneArrowsReal(laneId, arrows))
                    {
                        lock (StateLock)
                        {
                            if (arrows == LaneArrowFlags.None)
                                LaneArrows.Remove(laneId);
                            else
                                LaneArrows[laneId] = arrows;
                        }
                        Log.Info(LogCategory.Synchronization, "TM:PE lane arrows applied via API | laneId={0} arrows={1}", laneId, arrows);
                        return true;
                    }

                    Log.Warn(LogCategory.Bridge, "TM:PE lane arrow apply via API failed | laneId={0} arrows={1}", laneId, arrows);
                    return false;
                }

                Log.Info(LogCategory.Synchronization, "TM:PE lane arrows stored in stub | laneId={0} arrows={1}", laneId, arrows);

                lock (StateLock)
                {
                    if (arrows == LaneArrowFlags.None)
                        LaneArrows.Remove(laneId);
                    else
                        LaneArrows[laneId] = arrows;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE ApplyLaneArrows failed | error={0}", ex);
                return false;
            }
        }

        internal static bool TryGetLaneArrows(uint laneId, out LaneArrowFlags arrows)
        {
            try
            {
                if (SupportsLaneArrows && TryGetLaneArrowsReal(laneId, out arrows))
                {
                    Log.Debug(LogCategory.Hook, "TM:PE lane arrow query | laneId={0} arrows={1}", laneId, arrows);
                    lock (StateLock)
                    {
                        if (arrows == LaneArrowFlags.None)
                            LaneArrows.Remove(laneId);
                        else
                            LaneArrows[laneId] = arrows;
                    }
                    return true;
                }

                lock (StateLock)
                {
                    if (!LaneArrows.TryGetValue(laneId, out arrows))
                        arrows = LaneArrowFlags.None;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE TryGetLaneArrows failed | error={0}", ex);
                arrows = LaneArrowFlags.None;
                return false;
            }
        }

        private static bool LaneSupportsCustomArrows(uint laneId, out string detail)
        {
            detail = null;

            if (!TryGetLaneInfo(laneId, out var segmentId, out _, out var laneInfo, out var segmentInfo))
            {
                detail = "lane_lookup_failed";
                return false;
            }

            if (laneInfo == null || segmentInfo == null)
            {
                detail = "lane_data_missing";
                return false;
            }

            ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
            if ((segment.m_flags & NetSegment.Flags.Created) == NetSegment.Flags.None)
            {
                detail = "segment_not_created";
                return false;
            }

            var supportedLaneTypes = laneInfo.m_laneType & (NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle);
            if (supportedLaneTypes == NetInfo.LaneType.None)
            {
                detail = "lane_type=" + laneInfo.m_laneType;
                return false;
            }

            var forward = NetInfo.Direction.Forward;
            var effectiveDirection = (segment.m_flags & NetSegment.Flags.Invert) == NetSegment.Flags.None
                ? forward
                : NetInfo.InvertDirection(forward);
            var isStartNode = (laneInfo.m_finalDirection & effectiveDirection) == NetInfo.Direction.None;
            var nodeId = isStartNode ? segment.m_startNode : segment.m_endNode;
            if (nodeId == 0)
            {
                detail = "node_missing";
                return false;
            }

            ref var node = ref NetManager.instance.m_nodes.m_buffer[nodeId];
            if ((node.m_flags & NetNode.Flags.Created) == NetNode.Flags.None)
            {
                detail = "node_not_created";
                return false;
            }

            var laneArrowEligibleFlags = NetNode.Flags.Junction | NetNode.Flags.Transition | NetNode.Flags.Bend;
            if ((node.m_flags & laneArrowEligibleFlags) == NetNode.Flags.None)
            {
                detail = "node_not_junction_transition_or_bend";
                return false;
            }

            if (LaneArrowCanHaveMethod != null)
            {
                try
                {
                    object result;
                    var parameters = LaneArrowCanHaveMethod.GetParameters();
                    if (parameters.Length == 1)
                        result = LaneArrowCanHaveMethod.Invoke(null, new object[] { laneId });
                    else
                        result = LaneArrowCanHaveMethod.Invoke(null, new object[] { laneId, null });

                    if (result is bool allowed && !allowed)
                    {
                        detail = "tmpe_can_have_lane_arrows=false";
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn(LogCategory.Diagnostics, "TM:PE CanHaveLaneArrows probe failed | laneId={0} error={1}", laneId, ex);
                }
            }

            return true;
        }

        private static bool TryApplyLaneArrowsReal(uint laneId, LaneArrowFlags arrows)
        {
            if (LaneArrowManagerInstance == null || LaneArrowSetMethod == null || LaneArrowsEnumType == null)
                return false;

            if (!LaneSupportsCustomArrows(laneId, out var rejectionDetail))
            {
                var detailText = string.IsNullOrEmpty(rejectionDetail) ? "<unspecified>" : rejectionDetail;
                Log.Warn(LogCategory.Bridge, "TM:PE lane arrow apply aborted | laneId={0} detail={1}", laneId, detailText);
                return false;
            }

            var tmpeValue = Enum.ToObject(LaneArrowsEnumType, CombineLaneArrowFlags(arrows));
            var parameters = LaneArrowSetMethod.GetParameters();
            object result;
            if (parameters.Length == 3)
                result = LaneArrowSetMethod.Invoke(LaneArrowManagerInstance, new[] { (object)laneId, tmpeValue, (object)true });
            else
                result = LaneArrowSetMethod.Invoke(LaneArrowManagerInstance, new[] { (object)laneId, tmpeValue });

            if (LaneArrowSetMethod.ReturnType == typeof(bool))
            {
                if (!(result is bool success && success))
                {
                    Log.Warn(LogCategory.Bridge, "TM:PE lane arrow apply rejected by API | laneId={0} arrows={1}", laneId, arrows);
                    return false;
                }
            }

            return true;
        }

        private static bool TryGetLaneArrowsReal(uint laneId, out LaneArrowFlags arrows)
        {
            arrows = LaneArrowFlags.None;
            if (LaneArrowManagerInstance == null || LaneArrowGetMethod == null || LaneArrowsEnumType == null)
                return false;

            var result = LaneArrowGetMethod.Invoke(LaneArrowManagerInstance, new object[] { laneId });
            if (result == null)
                return true;

            var raw = Convert.ToInt32(result);
            arrows = LaneArrowFlags.None;
            if ((raw & LaneArrowLeftMask) != 0)
                arrows |= LaneArrowFlags.Left;
            if ((raw & LaneArrowForwardMask) != 0)
                arrows |= LaneArrowFlags.Forward;
            if ((raw & LaneArrowRightMask) != 0)
                arrows |= LaneArrowFlags.Right;
            return true;
        }

        private static int CombineLaneArrowFlags(LaneArrowFlags arrows)
        {
            var value = 0;
            if ((arrows & LaneArrowFlags.Left) != 0)
                value |= LaneArrowLeftMask;
            if ((arrows & LaneArrowFlags.Forward) != 0)
                value |= LaneArrowForwardMask;
            if ((arrows & LaneArrowFlags.Right) != 0)
                value |= LaneArrowRightMask;
            return value;
        }

    }
}
