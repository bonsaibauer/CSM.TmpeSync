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
        private static readonly Dictionary<NodeSegmentKey, PrioritySignType> PrioritySigns = new Dictionary<NodeSegmentKey, PrioritySignType>();
        private static object TrafficPriorityManagerInstance;
        private static MethodInfo PrioritySignSetMethod;
        private static MethodInfo PrioritySignGetMethod;
        private static Type PriorityTypeEnumType;

        private struct NodeSegmentKey : IEquatable<NodeSegmentKey>
        {
            public readonly ushort Node;
            public readonly ushort Segment;

            public NodeSegmentKey(ushort node, ushort segment)
            {
                Node = node;
                Segment = segment;
            }

            public bool Equals(NodeSegmentKey other)
            {
                return Node == other.Node && Segment == other.Segment;
            }

            public override bool Equals(object obj)
            {
                return obj is NodeSegmentKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (Node << 16) ^ Segment;
            }
        }

        private static bool InitialisePrioritySignBridge(Assembly tmpeAssembly)
        {
            try
            {
                var managerType = tmpeAssembly.GetType("TrafficManager.Manager.Impl.TrafficPriorityManager");
                if (managerType == null)
                    LogBridgeGap("Priority Signs", "type", "TrafficManager.Manager.Impl.TrafficPriorityManager");

                TrafficPriorityManagerInstance = TryGetStaticInstance(managerType, "Priority Signs", managerType?.FullName + ".Instance");

                PriorityTypeEnumType = ResolveType("TrafficManager.API.Traffic.Enums.PriorityType", tmpeAssembly);

                if (managerType != null && PriorityTypeEnumType != null)
                {
                    PrioritySignSetMethod = managerType.GetMethod(
                        "SetPrioritySign",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new[] { typeof(ushort), typeof(bool), PriorityTypeEnumType },
                        null);

                    PrioritySignGetMethod = managerType.GetMethod(
                        "GetPrioritySign",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new[] { typeof(ushort), typeof(bool) },
                        null);
                }
            }
            catch (Exception ex)
            {
                RecordFeatureGapDetail("prioritySigns", "exception", ex.GetType().Name);
                Log.Warn(LogCategory.Bridge, "TM:PE priority sign bridge initialization failed | error={0}", ex);
            }

            var supported = TrafficPriorityManagerInstance != null &&
                            PrioritySignSetMethod != null &&
                            PrioritySignGetMethod != null &&
                            PriorityTypeEnumType != null;
            SetFeatureStatus("prioritySigns", supported, null);
            return supported;
        }

        internal static bool ApplyPrioritySign(ushort nodeId, ushort segmentId, PrioritySignType signType)
        {
            try
            {
                var appliedViaApi = false;

                if (SupportsPrioritySigns)
                {
                    Log.Debug(LogCategory.Hook, "TM:PE priority sign request | nodeId={0} segmentId={1} signType={2}", nodeId, segmentId, signType);
                    appliedViaApi = TryApplyPrioritySignReal(nodeId, segmentId, signType);

                    if (!appliedViaApi)
                    {
                        Log.Warn(LogCategory.Bridge, "TM:PE priority sign apply via API failed | nodeId={0} segmentId={1}", nodeId, segmentId);
                        return false;
                    }
                }
                else
                {
                    Log.Info(LogCategory.Synchronization, "TM:PE priority sign stored in stub | nodeId={0} segmentId={1} signType={2}", nodeId, segmentId, signType);
                }

                if (appliedViaApi || !SupportsPrioritySigns)
                {
                    lock (StateLock)
                    {
                        var key = new NodeSegmentKey(nodeId, segmentId);
                        if (signType == PrioritySignType.None)
                            PrioritySigns.Remove(key);
                        else
                            PrioritySigns[key] = signType;
                    }
                }

                return appliedViaApi || !SupportsPrioritySigns;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE ApplyPrioritySign failed | error={0}", ex);
                return false;
            }
        }

        internal static bool TryGetPrioritySign(ushort nodeId, ushort segmentId, out PrioritySignType signType)
        {
            try
            {
                var key = new NodeSegmentKey(nodeId, segmentId);

                if (SupportsPrioritySigns && TryGetPrioritySignReal(nodeId, segmentId, out signType))
                {
                    Log.Debug(LogCategory.Hook, "TM:PE priority sign query | nodeId={0} segmentId={1} signType={2}", nodeId, segmentId, signType);

                    lock (StateLock)
                    {
                        if (signType == PrioritySignType.None)
                            PrioritySigns.Remove(key);
                        else
                            PrioritySigns[key] = signType;
                    }

                    return true;
                }

                lock (StateLock)
                {
                    if (!PrioritySigns.TryGetValue(key, out signType))
                        signType = PrioritySignType.None;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE TryGetPrioritySign failed | error={0}", ex);
                signType = PrioritySignType.None;
                return false;
            }
        }

        private static bool TryApplyPrioritySignReal(ushort nodeId, ushort segmentId, PrioritySignType signType)
        {
            if (TrafficPriorityManagerInstance == null || PrioritySignSetMethod == null || PriorityTypeEnumType == null)
                return false;

            if (!TryResolvePrioritySegmentOrientation(nodeId, segmentId, out var startNode))
                return false;

            var tmpeValue = ConvertPrioritySignToTmpe(signType);
            if (tmpeValue == null)
                return false;

            PrioritySignSetMethod.Invoke(TrafficPriorityManagerInstance, new[] { (object)segmentId, (object)startNode, tmpeValue });
            return true;
        }

        private static bool TryGetPrioritySignReal(ushort nodeId, ushort segmentId, out PrioritySignType signType)
        {
            signType = PrioritySignType.None;

            if (TrafficPriorityManagerInstance == null || PrioritySignGetMethod == null || PriorityTypeEnumType == null)
                return false;

            if (!TryResolvePrioritySegmentOrientation(nodeId, segmentId, out var startNode))
                return false;

            var result = PrioritySignGetMethod.Invoke(TrafficPriorityManagerInstance, new object[] { segmentId, startNode });
            signType = ConvertPrioritySignFromTmpe(result);
            return true;
        }

        private static bool TryResolvePrioritySegmentOrientation(ushort nodeId, ushort segmentId, out bool startNode)
        {
            startNode = false;
            ref var segment = ref NetManager.instance.m_segments.m_buffer[(int)segmentId];
            if ((segment.m_flags & NetSegment.Flags.Created) == NetSegment.Flags.None)
                return false;

            if (segment.m_startNode == nodeId)
            {
                startNode = true;
                return true;
            }

            if (segment.m_endNode == nodeId)
            {
                startNode = false;
                return true;
            }

            return false;
        }

        private static object ConvertPrioritySignToTmpe(PrioritySignType signType)
        {
            if (PriorityTypeEnumType == null)
                return null;

            string name;
            switch (signType)
            {
                case PrioritySignType.Priority:
                    name = "Main";
                    break;
                case PrioritySignType.Stop:
                    name = "Stop";
                    break;
                case PrioritySignType.Yield:
                    name = "Yield";
                    break;
                default:
                    name = "None";
                    break;
            }

            return Enum.Parse(PriorityTypeEnumType, name);
        }

        private static PrioritySignType ConvertPrioritySignFromTmpe(object value)
        {
            if (value == null || PriorityTypeEnumType == null)
                return PrioritySignType.None;

            var name = Enum.GetName(PriorityTypeEnumType, value) ?? "None";
            switch (name)
            {
                case "Main":
                    return PrioritySignType.Priority;
                case "Stop":
                    return PrioritySignType.Stop;
                case "Yield":
                    return PrioritySignType.Yield;
                default:
                    return PrioritySignType.None;
            }
        }

    }
}
