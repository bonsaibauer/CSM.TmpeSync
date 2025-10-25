using System;
using System.Collections.Generic;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.CsmBridge;

namespace CSM.TmpeSync.Util
{
    internal static partial class PendingMap
    {
        internal static bool ApplySpeedLimit(uint laneId, float speedKmh, bool ignoreScope)
        {
            if (ignoreScope)
            {
                using (CsmBridge.StartIgnore())
                {
                    return TmpeBridgeAdapter.ApplySpeedLimit(laneId, speedKmh);
                }
            }

            return TmpeBridgeAdapter.ApplySpeedLimit(laneId, speedKmh);
        }

        internal static bool TryGetSpeedLimit(
            uint laneId,
            out float speedKmh,
            out float? defaultKmh,
            out bool hasOverride,
            out bool pending)
        {
            return TmpeBridgeAdapter.TryGetSpeedLimit(laneId, out speedKmh, out defaultKmh, out hasOverride, out pending);
        }

        internal static bool ApplyVehicleRestrictions(
            uint laneId,
            VehicleRestrictionFlags restrictions,
            bool ignoreScope)
        {
            if (ignoreScope)
            {
                using (CsmBridge.StartIgnore())
                {
                    return TmpeBridgeAdapter.ApplyVehicleRestrictions(laneId, restrictions);
                }
            }

            return TmpeBridgeAdapter.ApplyVehicleRestrictions(laneId, restrictions);
        }

        internal static bool TryGetVehicleRestrictions(uint laneId, out VehicleRestrictionFlags restrictions)
        {
            return TmpeBridgeAdapter.TryGetVehicleRestrictions(laneId, out restrictions);
        }

        internal static bool ApplyParkingRestriction(
            ushort segmentId,
            ParkingRestrictionState state,
            bool ignoreScope)
        {
            if (ignoreScope)
            {
                using (CsmBridge.StartIgnore())
                {
                    return TmpeBridgeAdapter.ApplyParkingRestriction(segmentId, state);
                }
            }

            return TmpeBridgeAdapter.ApplyParkingRestriction(segmentId, state);
        }

        internal static bool TryGetParkingRestriction(ushort segmentId, out ParkingRestrictionState state)
        {
            return TmpeBridgeAdapter.TryGetParkingRestriction(segmentId, out state);
        }

        internal static bool ApplyLaneArrows(uint laneId, LaneArrowFlags arrows, bool ignoreScope)
        {
            if (ignoreScope)
            {
                using (CsmBridge.StartIgnore())
                {
                    return TmpeBridgeAdapter.ApplyLaneArrows(laneId, arrows);
                }
            }

            return TmpeBridgeAdapter.ApplyLaneArrows(laneId, arrows);
        }

        internal static bool TryGetLaneArrows(uint laneId, out LaneArrowFlags arrows)
        {
            return TmpeBridgeAdapter.TryGetLaneArrows(laneId, out arrows);
        }

        internal static bool ApplyLaneConnections(
            uint sourceLaneId,
            uint[] targetLaneIds,
            bool ignoreScope)
        {
            if (ignoreScope)
            {
                using (CsmBridge.StartIgnore())
                {
                    return TmpeBridgeAdapter.ApplyLaneConnections(sourceLaneId, targetLaneIds);
                }
            }

            return TmpeBridgeAdapter.ApplyLaneConnections(sourceLaneId, targetLaneIds);
        }

        internal static bool TryGetLaneConnections(uint laneId, out uint[] targetLaneIds)
        {
            return TmpeBridgeAdapter.TryGetLaneConnections(laneId, out targetLaneIds);
        }

        internal static bool ApplyJunctionRestrictions(
            ushort nodeId,
            JunctionRestrictionsState state,
            bool ignoreScope)
        {
            if (ignoreScope)
            {
                using (CsmBridge.StartIgnore())
                {
                    return TmpeBridgeAdapter.ApplyJunctionRestrictions(nodeId, state);
                }
            }

            return TmpeBridgeAdapter.ApplyJunctionRestrictions(nodeId, state);
        }

        internal static bool TryGetJunctionRestrictions(ushort nodeId, out JunctionRestrictionsState state)
        {
            return TmpeBridgeAdapter.TryGetJunctionRestrictions(nodeId, out state);
        }

        internal static bool ApplyPrioritySign(
            ushort nodeId,
            ushort segmentId,
            PrioritySignType signType,
            bool ignoreScope)
        {
            if (ignoreScope)
            {
                using (CsmBridge.StartIgnore())
                {
                    return TmpeBridgeAdapter.ApplyPrioritySign(nodeId, segmentId, signType);
                }
            }

            return TmpeBridgeAdapter.ApplyPrioritySign(nodeId, segmentId, signType);
        }

        internal static bool TryGetPrioritySign(
            ushort nodeId,
            ushort segmentId,
            out PrioritySignType signType)
        {
            return TmpeBridgeAdapter.TryGetPrioritySign(nodeId, segmentId, out signType);
        }
    }
}
