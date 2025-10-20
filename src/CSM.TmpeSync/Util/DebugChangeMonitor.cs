using System;
using System.Globalization;
using System.Linq;
using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Tmpe;

namespace CSM.TmpeSync.Util
{
    internal static class DebugChangeMonitor
    {
        private static bool IsActive => Log.IsDebugEnabled;

        internal static void RecordSpeedLimitChange(uint laneId, float? previousSpeedKmh, float newSpeedKmh)
        {
            if (!IsActive)
                return;

            Log.Info(
                LogCategory.Diagnostics,
                "Monitoring change | feature=speed_limit laneId={0} previous={1} new={2}",
                laneId,
                FormatSpeed(previousSpeedKmh),
                FormatSpeed(newSpeedKmh));
        }

        internal static void RecordLaneArrowChange(uint laneId, LaneArrowFlags? previous, LaneArrowFlags updated)
        {
            if (!IsActive)
                return;

            Log.Info(
                LogCategory.Diagnostics,
                "Monitoring change | feature=lane_arrows laneId={0} previous={1} new={2}",
                laneId,
                FormatLaneArrows(previous),
                updated);
        }

        internal static void RecordLaneConnectionChange(uint sourceLaneId, uint[] previousTargets, uint[] newTargets)
        {
            if (!IsActive)
                return;

            Log.Info(
                LogCategory.Diagnostics,
                "Monitoring change | feature=lane_connections sourceLane={0} previous=[{1}] new=[{2}]",
                sourceLaneId,
                FormatLaneList(previousTargets),
                FormatLaneList(newTargets));
        }

        internal static void RecordVehicleRestrictionChange(uint laneId, VehicleRestrictionFlags? previous, VehicleRestrictionFlags updated)
        {
            if (!IsActive)
                return;

            Log.Info(
                LogCategory.Diagnostics,
                "Monitoring change | feature=vehicle_restrictions laneId={0} previous={1} new={2}",
                laneId,
                FormatVehicleRestrictions(previous),
                updated);
        }

        internal static void RecordJunctionRestrictionsChange(ushort nodeId, JunctionRestrictionsState previous, JunctionRestrictionsState updated)
        {
            if (!IsActive)
                return;

            Log.Info(
                LogCategory.Diagnostics,
                "Monitoring change | feature=junction_restrictions nodeId={0} previous={1} new={2}",
                nodeId,
                previous?.ToString() ?? "<unknown>",
                updated?.ToString() ?? "<null>");
        }

        internal static void RecordParkingRestrictionChange(ushort segmentId, ParkingRestrictionState previous, ParkingRestrictionState updated)
        {
            if (!IsActive)
                return;

            Log.Info(
                LogCategory.Diagnostics,
                "Monitoring change | feature=parking_restrictions segmentId={0} previous={1} new={2}",
                segmentId,
                previous?.ToString() ?? "<unknown>",
                updated?.ToString() ?? "<null>");
        }

        internal static void RecordPrioritySignChange(ushort nodeId, ushort segmentId, PrioritySignType? previous, PrioritySignType updated)
        {
            if (!IsActive)
                return;

            Log.Info(
                LogCategory.Diagnostics,
                "Monitoring change | feature=priority_signs nodeId={0} segmentId={1} previous={2} new={3}",
                nodeId,
                segmentId,
                previous?.ToString() ?? "<unknown>",
                updated);
        }

        internal static void RecordCrosswalkHiddenChange(ushort nodeId, ushort segmentId, bool? previousHidden, bool newHidden)
        {
            if (!IsActive)
                return;

            Log.Info(
                LogCategory.Diagnostics,
                "Monitoring change | feature=crosswalk_hidden nodeId={0} segmentId={1} previous={2} new={3}",
                nodeId,
                segmentId,
                FormatBool(previousHidden),
                FormatBool(newHidden));
        }

        internal static void RecordManualTrafficLightChange(ushort nodeId, bool? previousEnabled, bool newEnabled)
        {
            if (!IsActive)
                return;

            Log.Info(
                LogCategory.Diagnostics,
                "Monitoring change | feature=manual_traffic_light nodeId={0} previous={1} new={2}",
                nodeId,
                FormatBool(previousEnabled),
                FormatBool(newEnabled));
        }

        internal static void RecordTimedTrafficLightChange(ushort nodeId, TimedTrafficLightState previous, TimedTrafficLightState updated)
        {
            if (!IsActive)
                return;

            Log.Info(
                LogCategory.Diagnostics,
                "Monitoring change | feature=timed_traffic_light nodeId={0} previous={1} new={2}",
                nodeId,
                previous?.ToString() ?? "<unknown>",
                updated?.ToString() ?? "<null>");
        }

        private static string FormatSpeed(float? value)
        {
            if (!value.HasValue)
                return "<unknown>";

            return string.Format(CultureInfo.InvariantCulture, "{0:0.##} km/h", value.Value);
        }

        private static string FormatLaneArrows(LaneArrowFlags? value)
        {
            return value?.ToString() ?? "<unknown>";
        }

        private static string FormatLaneList(uint[] lanes)
        {
            if (lanes == null || lanes.Length == 0)
                return string.Empty;

            return string.Join(
                ",",
                lanes
                    .Where(id => id != 0)
                    .Distinct()
                    .OrderBy(id => id)
                    .Select(id => id.ToString(CultureInfo.InvariantCulture)));
        }

        private static string FormatVehicleRestrictions(VehicleRestrictionFlags? value)
        {
            return value?.ToString() ?? "<unknown>";
        }

        private static string FormatBool(bool? value)
        {
            if (!value.HasValue)
                return "<unknown>";

            return value.Value ? "true" : "false";
        }
    }
}
