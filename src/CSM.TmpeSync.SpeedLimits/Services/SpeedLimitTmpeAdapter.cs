using System;
using System.Collections.Generic;
using ColossalFramework;
using CSM.TmpeSync.Services;
using CSM.TmpeSync.SpeedLimits.Messages;
using CSM.TmpeSync.SpeedLimits.Services;

namespace CSM.TmpeSync.SpeedLimits.Services
{
    internal static class SpeedLimitTmpeAdapter
    {
        internal static bool TryGet(ushort segmentId, out SpeedLimitsAppliedCommand command)
        {
            command = null;

            try
            {
                Log.Info(LogCategory.Diagnostics, LogRole.Host, "[SpeedLimits][Readback] Begin TryGet | segmentId={0}", segmentId);
                if (!NetworkUtil.SegmentExists(segmentId))
                    return false;

                ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
                var info = segment.Info;
                if (info?.m_lanes == null)
                    return false;

                var items = new List<SpeedLimitsAppliedCommand.Entry>();
                var lanes = info.m_lanes;
                for (int i = 0; i < lanes.Length; i++)
                {
                    var laneInfo = lanes[i];
                    if (!IsLaneConfigurable(laneInfo))
                        continue;

                    if (!NetworkUtil.TryGetLaneId(segmentId, i, out var laneId))
                        continue;

                    if (!SpeedLimitAdapter.TryGetSpeedLimit(laneId, out var kmh, out var defaultKmh, out var hasOverride))
                        continue;

                    if (!hasOverride && (!defaultKmh.HasValue || defaultKmh.Value <= 0f))
                        continue;

                    var encoded = SpeedLimitCodec.Encode(kmh, defaultKmh, hasOverride);
                    Log.Info(LogCategory.Diagnostics,
                        LogRole.Host,
                        "[SpeedLimits][Readback] laneOrdinal={0} laneId={1} kmh={2:F1} defaultKmh={3} hasOverride={4} encoded={5}",
                        i, laneId, kmh, defaultKmh.HasValue ? defaultKmh.Value.ToString("F1") : "null", hasOverride, encoded);

                    var sig = new SpeedLimitsAppliedCommand.LaneSignature
                    {
                        LaneTypeRaw = (int)laneInfo.m_laneType,
                        VehicleTypeRaw = (int)laneInfo.m_vehicleType,
                        DirectionRaw = (int)laneInfo.m_direction
                    };

                    items.Add(new SpeedLimitsAppliedCommand.Entry
                    {
                        LaneOrdinal = i,
                        Speed = encoded,
                        Signature = sig
                    });
                }

                command = new SpeedLimitsAppliedCommand
                {
                    SegmentId = segmentId,
                    Items = items
                };
                Log.Info(LogCategory.Diagnostics,
                    LogRole.Host,
                    "[SpeedLimits][Readback] End TryGet | segmentId={0} items={1}",
                    segmentId, items.Count);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, LogRole.Host, "SpeedLimits TryGet failed | segmentId={0} error={1}", segmentId, ex);
                return false;
            }
        }

        internal static bool Apply(ushort segmentId, SpeedLimitsUpdateRequest request)
        {
            if (request == null || request.Items == null)
                return true;

            try
            {
                if (!NetworkUtil.SegmentExists(segmentId))
                    return false;

                ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
                var info = segment.Info;
                if (info?.m_lanes == null)
                    return false;

                using (LocalIgnore.Scoped())
                {
                    bool okAll = true;

                    foreach (var item in request.Items)
                    {
                        var idx = item.LaneOrdinal;
                        if (idx < 0 || idx >= info.m_lanes.Length)
                        {
                            Log.Warn(LogCategory.Synchronization, LogRole.Host, "[SpeedLimits] Apply skipped: invalid ordinal | seg={0} ord={1}", segmentId, idx);
                            okAll = false; continue;
                        }

                        var laneInfo = info.m_lanes[idx];
                        if (!IsLaneConfigurable(laneInfo))
                        {
                            Log.Warn(LogCategory.Synchronization, LogRole.Host, "[SpeedLimits] Not a vehicle lane, skip | seg={0} ord={1}", segmentId, idx);
                            okAll = false; continue;
                        }
                        if (!SignatureMatches(laneInfo, item.Signature))
                        {
                            Log.Warn(LogCategory.Synchronization, LogRole.Host, "[SpeedLimits] Signature mismatch, skip | seg={0} ord={1}", segmentId, idx);
                            okAll = false; continue;
                        }

                        if (!NetworkUtil.TryGetLaneId(segmentId, idx, out var laneId))
                        {
                            Log.Warn(LogCategory.Synchronization, LogRole.Host, "[SpeedLimits] LaneId resolve failed | seg={0} ord={1}", segmentId, idx);
                            okAll = false; continue;
                        }

                        var speedKmh = SpeedLimitCodec.DecodeToKmh(item.Speed);
                        if (!SpeedLimitAdapter.ApplySpeedLimit(laneId, speedKmh))
                        {
                            Log.Warn(LogCategory.Synchronization, LogRole.Host, "[SpeedLimits] TM:PE apply failed | seg={0} ord={1}", segmentId, idx);
                            okAll = false; continue;
                        }
                    }

                    return okAll;
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, LogRole.Host, "SpeedLimits Apply failed | segmentId={0} error={1}", segmentId, ex);
                return false;
            }
        }

        private static bool SignatureMatches(NetInfo.Lane laneInfo, SpeedLimitsAppliedCommand.LaneSignature sig)
        {
            if (laneInfo == null || sig == null)
                return false;
            return (int)laneInfo.m_laneType == sig.LaneTypeRaw &&
                   (int)laneInfo.m_vehicleType == sig.VehicleTypeRaw &&
                   (int)laneInfo.m_direction == sig.DirectionRaw;
        }

        private static bool IsLaneConfigurable(NetInfo.Lane laneInfo)
        {
            // Mirror TM:PE tool logic at a high level: only consider lanes that carry vehicles.
            return (laneInfo.m_laneType & NetInfo.LaneType.Vehicle) != 0;
        }

        private static class LocalIgnore
        {
            [ThreadStatic]
            private static int _depth;

            public static bool IsActive => _depth > 0;

            public static IDisposable Scoped()
            {
                _depth++;
                return new Scope();
            }

            private sealed class Scope : IDisposable
            {
                private bool _disposed;
                public void Dispose()
                {
                    if (_disposed) return;
                    _disposed = true;
                    _depth--;
                }
            }
        }

        internal static bool IsLocalApplyActive => LocalIgnore.IsActive;
    }
}
