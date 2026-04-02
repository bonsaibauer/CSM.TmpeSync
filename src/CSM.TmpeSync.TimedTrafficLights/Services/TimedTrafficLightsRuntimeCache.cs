using System.Collections.Generic;
using System.Linq;
using CSM.TmpeSync.TimedTrafficLights.Messages;

namespace CSM.TmpeSync.TimedTrafficLights.Services
{
    internal static class TimedTrafficLightsRuntimeCache
    {
        private sealed class BroadcastEntry
        {
            internal TimedTrafficLightsRuntimeState State;
            internal uint SentAtFrame;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<ushort, BroadcastEntry> BroadcastByMaster = new Dictionary<ushort, BroadcastEntry>();
        private static readonly Dictionary<ushort, TimedTrafficLightsRuntimeState> ReceivedByMaster = new Dictionary<ushort, TimedTrafficLightsRuntimeState>();

        internal static void StoreBroadcast(TimedTrafficLightsRuntimeState state, uint sentAtFrame)
        {
            if (state == null || state.MasterNodeId == 0)
                return;

            lock (Gate)
            {
                BroadcastByMaster[state.MasterNodeId] = new BroadcastEntry
                {
                    State = CloneRuntime(state),
                    SentAtFrame = sentAtFrame
                };
            }
        }

        internal static bool TryGetBroadcast(ushort masterNodeId, out TimedTrafficLightsRuntimeState state, out uint sentAtFrame)
        {
            lock (Gate)
            {
                BroadcastEntry entry;
                if (BroadcastByMaster.TryGetValue(masterNodeId, out entry) && entry != null && entry.State != null)
                {
                    state = CloneRuntime(entry.State);
                    sentAtFrame = entry.SentAtFrame;
                    return true;
                }
            }

            state = null;
            sentAtFrame = 0;
            return false;
        }

        internal static void RemoveBroadcast(ushort masterNodeId)
        {
            lock (Gate)
            {
                BroadcastByMaster.Remove(masterNodeId);
            }
        }

        internal static List<ushort> GetBroadcastMasterNodeIds()
        {
            lock (Gate)
            {
                return BroadcastByMaster.Keys.OrderBy(id => id).ToList();
            }
        }

        internal static void StoreReceived(TimedTrafficLightsRuntimeState state)
        {
            if (state == null || state.MasterNodeId == 0)
                return;

            lock (Gate)
            {
                ReceivedByMaster[state.MasterNodeId] = CloneRuntime(state);
            }
        }

        internal static bool TryGetReceived(ushort masterNodeId, out TimedTrafficLightsRuntimeState state)
        {
            lock (Gate)
            {
                TimedTrafficLightsRuntimeState existing;
                if (ReceivedByMaster.TryGetValue(masterNodeId, out existing) && existing != null)
                {
                    state = CloneRuntime(existing);
                    return true;
                }
            }

            state = null;
            return false;
        }

        internal static List<TimedTrafficLightsRuntimeState> GetAllReceived()
        {
            lock (Gate)
            {
                return ReceivedByMaster.Values
                    .Select(CloneRuntime)
                    .Where(state => state != null)
                    .ToList();
            }
        }

        internal static void RemoveReceived(ushort masterNodeId)
        {
            lock (Gate)
            {
                ReceivedByMaster.Remove(masterNodeId);
            }
        }

        internal static TimedTrafficLightsRuntimeState CloneRuntime(TimedTrafficLightsRuntimeState source)
        {
            if (source == null)
                return null;

            return new TimedTrafficLightsRuntimeState
            {
                MasterNodeId = source.MasterNodeId,
                IsRunning = source.IsRunning,
                CurrentStep = source.CurrentStep,
                Epoch = source.Epoch
            };
        }
    }
}