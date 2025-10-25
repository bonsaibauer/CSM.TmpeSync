using System;
using System.Collections.Generic;
using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Tmpe;

namespace CSM.TmpeSync.Util
{
    internal static partial class PendingMap
    {
        internal static bool ApplySpeedLimit(uint laneId, float speedKmh, bool ignoreScope)
        {
            if (ignoreScope)
            {
                using (CsmCompat.StartIgnore())
                {
                    return TmpeAdapter.ApplySpeedLimit(laneId, speedKmh);
                }
            }

            return TmpeAdapter.ApplySpeedLimit(laneId, speedKmh);
        }

        internal static bool TryGetSpeedLimit(
            uint laneId,
            out float speedKmh,
            out float? defaultKmh,
            out bool hasOverride,
            out bool pending)
        {
            return TmpeAdapter.TryGetSpeedLimit(laneId, out speedKmh, out defaultKmh, out hasOverride, out pending);
        }

        internal static bool ApplyVehicleRestrictions(
            uint laneId,
            VehicleRestrictionFlags restrictions,
            bool ignoreScope)
        {
            if (ignoreScope)
            {
                using (CsmCompat.StartIgnore())
                {
                    return TmpeAdapter.ApplyVehicleRestrictions(laneId, restrictions);
                }
            }

            return TmpeAdapter.ApplyVehicleRestrictions(laneId, restrictions);
        }

        internal static bool TryGetVehicleRestrictions(uint laneId, out VehicleRestrictionFlags restrictions)
        {
            return TmpeAdapter.TryGetVehicleRestrictions(laneId, out restrictions);
        }

        internal static bool ApplyParkingRestriction(
            ushort segmentId,
            ParkingRestrictionState state,
            bool ignoreScope)
        {
            if (ignoreScope)
            {
                using (CsmCompat.StartIgnore())
                {
                    return TmpeAdapter.ApplyParkingRestriction(segmentId, state);
                }
            }

            return TmpeAdapter.ApplyParkingRestriction(segmentId, state);
        }

        internal static bool TryGetParkingRestriction(ushort segmentId, out ParkingRestrictionState state)
        {
            return TmpeAdapter.TryGetParkingRestriction(segmentId, out state);
        }

        internal static bool ApplyLaneArrows(uint laneId, LaneArrowFlags arrows, bool ignoreScope)
        {
            if (ignoreScope)
            {
                using (CsmCompat.StartIgnore())
                {
                    return TmpeAdapter.ApplyLaneArrows(laneId, arrows);
                }
            }

            return TmpeAdapter.ApplyLaneArrows(laneId, arrows);
        }

        internal static bool TryGetLaneArrows(uint laneId, out LaneArrowFlags arrows)
        {
            return TmpeAdapter.TryGetLaneArrows(laneId, out arrows);
        }

        internal static bool ApplyLaneConnections(
            uint sourceLaneId,
            uint[] targetLaneIds,
            bool ignoreScope)
        {
            if (ignoreScope)
            {
                using (CsmCompat.StartIgnore())
                {
                    return TmpeAdapter.ApplyLaneConnections(sourceLaneId, targetLaneIds);
                }
            }

            return TmpeAdapter.ApplyLaneConnections(sourceLaneId, targetLaneIds);
        }

        internal static bool TryGetLaneConnections(uint laneId, out uint[] targetLaneIds)
        {
            return TmpeAdapter.TryGetLaneConnections(laneId, out targetLaneIds);
        }

        internal static bool ApplyJunctionRestrictions(
            ushort nodeId,
            JunctionRestrictionsState state,
            bool ignoreScope)
        {
            if (ignoreScope)
            {
                using (CsmCompat.StartIgnore())
                {
                    return TmpeAdapter.ApplyJunctionRestrictions(nodeId, state);
                }
            }

            return TmpeAdapter.ApplyJunctionRestrictions(nodeId, state);
        }

        internal static bool TryGetJunctionRestrictions(ushort nodeId, out JunctionRestrictionsState state)
        {
            return TmpeAdapter.TryGetJunctionRestrictions(nodeId, out state);
        }

        internal static bool ApplyPrioritySign(
            ushort nodeId,
            ushort segmentId,
            PrioritySignType signType,
            bool ignoreScope)
        {
            if (ignoreScope)
            {
                using (CsmCompat.StartIgnore())
                {
                    return TmpeAdapter.ApplyPrioritySign(nodeId, segmentId, signType);
                }
            }

            return TmpeAdapter.ApplyPrioritySign(nodeId, segmentId, signType);
        }

        internal static bool TryGetPrioritySign(
            ushort nodeId,
            ushort segmentId,
            out PrioritySignType signType)
        {
            return TmpeAdapter.TryGetPrioritySign(nodeId, segmentId, out signType);
        }
    }
}
