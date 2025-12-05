using System.Collections.Generic;
using System.Linq;
using CSM.TmpeSync.VehicleRestrictions.Messages;

namespace CSM.TmpeSync.VehicleRestrictions.Services
{
    /// <summary>
    /// Caches last applied vehicle restriction states per segment for host-side resync to reconnecting clients.
    /// Uses lane ordinals/signatures (not lane ids) so remote clients can resolve locally.
    /// </summary>
    internal static class VehicleRestrictionStateCache
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<ushort, VehicleRestrictionsAppliedCommand> Cache =
            new Dictionary<ushort, VehicleRestrictionsAppliedCommand>();

        internal static void Store(VehicleRestrictionsAppliedCommand state)
        {
            if (state == null || state.SegmentId == 0)
                return;

            lock (Gate)
            {
                Cache[state.SegmentId] = Clone(state);
            }
        }

        internal static List<VehicleRestrictionsAppliedCommand> GetAll()
        {
            lock (Gate)
            {
                return Cache
                    .Values
                    .Select(Clone)
                    .ToList();
            }
        }

        internal static VehicleRestrictionsAppliedCommand Clone(VehicleRestrictionsAppliedCommand source)
        {
            if (source == null)
                return null;

            var clone = new VehicleRestrictionsAppliedCommand
            {
                SegmentId = source.SegmentId
            };

            if (source.Items != null)
            {
                foreach (var entry in source.Items)
                {
                    if (entry == null)
                        continue;

                    clone.Items.Add(new VehicleRestrictionsAppliedCommand.Entry
                    {
                        LaneOrdinal = entry.LaneOrdinal,
                        Restrictions = entry.Restrictions,
                        Signature = entry.Signature
                    });
                }
            }

            return clone;
        }
    }
}
