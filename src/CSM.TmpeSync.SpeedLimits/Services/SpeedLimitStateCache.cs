using System.Collections.Generic;
using System.Linq;
using CSM.TmpeSync.SpeedLimits.Messages;

namespace CSM.TmpeSync.SpeedLimits.Services
{
    /// <summary>
    /// Tracks last known speed limit states so a host can resync them to newly joining clients.
    /// Uses lane ordinals/signatures only, avoiding lane-id mismatches on remote clients.
    /// </summary>
    internal static class SpeedLimitStateCache
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<ushort, SpeedLimitsAppliedCommand> Segments =
            new Dictionary<ushort, SpeedLimitsAppliedCommand>();

        private static readonly Dictionary<string, DefaultSpeedLimitAppliedCommand> Defaults =
            new Dictionary<string, DefaultSpeedLimitAppliedCommand>();

        internal static void StoreSegment(SpeedLimitsAppliedCommand state)
        {
            if (state == null || state.SegmentId == 0)
                return;

            lock (Gate)
            {
                Segments[state.SegmentId] = SpeedLimitSynchronization.CloneApplied(state);
            }
        }

        internal static void StoreDefault(DefaultSpeedLimitAppliedCommand state)
        {
            if (state == null || string.IsNullOrEmpty(state.NetInfoName))
                return;

            lock (Gate)
            {
                Defaults[state.NetInfoName] = SpeedLimitSynchronization.CloneDefault(state);
            }
        }

        internal static List<SpeedLimitsAppliedCommand> GetAllSegments()
        {
            lock (Gate)
            {
                return Segments.Values
                    .Select(SpeedLimitSynchronization.CloneApplied)
                    .ToList();
            }
        }

        internal static List<DefaultSpeedLimitAppliedCommand> GetAllDefaults()
        {
            lock (Gate)
            {
                return Defaults.Values
                    .Select(SpeedLimitSynchronization.CloneDefault)
                    .ToList();
            }
        }
    }
}
