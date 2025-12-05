using System.Collections.Generic;
using System.Linq;
using CSM.TmpeSync.ToggleTrafficLights.Messages;

namespace CSM.TmpeSync.ToggleTrafficLights.Services
{
    /// <summary>
    /// Caches last applied traffic-light states so hosts can resync them to reconnecting clients.
    /// </summary>
    internal static class ToggleTrafficLightsStateCache
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<ushort, ToggleTrafficLightsAppliedCommand> Cache =
            new Dictionary<ushort, ToggleTrafficLightsAppliedCommand>();

        internal static void Store(ToggleTrafficLightsAppliedCommand state)
        {
            if (state == null || state.NodeId == 0)
                return;

            lock (Gate)
            {
                Cache[state.NodeId] = Clone(state);
            }
        }

        internal static List<ToggleTrafficLightsAppliedCommand> GetAll()
        {
            lock (Gate)
            {
                return Cache
                    .Values
                    .Select(Clone)
                    .ToList();
            }
        }

        private static ToggleTrafficLightsAppliedCommand Clone(ToggleTrafficLightsAppliedCommand source)
        {
            if (source == null)
                return null;

            return new ToggleTrafficLightsAppliedCommand
            {
                NodeId = source.NodeId,
                Enabled = source.Enabled
            };
        }
    }
}
