using System.Collections.Generic;
using System.Linq;
using CSM.TmpeSync.ParkingRestrictions.Messages;

namespace CSM.TmpeSync.ParkingRestrictions.Services
{
    /// <summary>
    /// Caches last applied parking restriction states per segment so the host can resync them to reconnecting clients.
    /// </summary>
    internal static class ParkingRestrictionStateCache
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<ushort, ParkingRestrictionAppliedCommand> Cache =
            new Dictionary<ushort, ParkingRestrictionAppliedCommand>();

        internal static void Store(ParkingRestrictionAppliedCommand state)
        {
            if (state == null || state.SegmentId == 0)
                return;

            lock (Gate)
            {
                Cache[state.SegmentId] = Clone(state);
            }
        }

        internal static List<ParkingRestrictionAppliedCommand> GetAll()
        {
            lock (Gate)
            {
                return Cache
                    .Values
                    .Select(Clone)
                    .ToList();
            }
        }

        private static ParkingRestrictionAppliedCommand Clone(ParkingRestrictionAppliedCommand source)
        {
            if (source == null)
                return null;

            return new ParkingRestrictionAppliedCommand
            {
                SegmentId = source.SegmentId,
                State = source.State?.Clone()
            };
        }
    }
}
