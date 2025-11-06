using System;
using System.Collections.Generic;
using System.Linq;
using CSM.TmpeSync.LaneConnector.Messages;
using CSM.TmpeSync.Services;
using TrafficManager.API;
using TrafficManager.API.Manager;

namespace CSM.TmpeSync.LaneConnector.Services
{
    internal static class LaneConnectorTmpeAdapter
    {
        internal static bool TryBuildSnapshot(
            ushort nodeId,
            ushort segmentId,
            out LaneConnectorAppliedCommand snapshot)
        {
            snapshot = null;

            if (!TryGetManager(out var manager))
                return false;

            if (!LaneConnectorEndSelector.TryGetCandidates(nodeId, segmentId, towardsNode: true, out var startNode, out var sources))
                return false;

            var outgoingMap = LaneConnectorEndSelector.BuildCandidateMap(nodeId, towardsNode: false);
            if (outgoingMap.Count == 0)
                return false;

            var orderedOutgoingKeys = outgoingMap.Keys
                .OrderBy(k => k.SegmentId)
                .ThenBy(k => k.StartNode ? 0 : 1)
                .ToList();

            var command = new LaneConnectorAppliedCommand
            {
                NodeId = nodeId,
                SegmentId = segmentId,
                StartNode = startNode
            };

            for (int sourceOrdinal = 0; sourceOrdinal < sources.Count; sourceOrdinal++)
            {
                var source = sources[sourceOrdinal];
                var entry = new LaneConnectorAppliedCommand.Entry
                {
                    SourceOrdinal = sourceOrdinal
                };

                foreach (var key in orderedOutgoingKeys)
                {
                    if (!outgoingMap.TryGetValue(key, out var targets))
                        continue;

                    for (int targetOrdinal = 0; targetOrdinal < targets.Count; targetOrdinal++)
                    {
                        var candidate = targets[targetOrdinal];
                        bool connected;
                        try
                        {
                            connected = manager.AreLanesConnected(source.LaneId, candidate.LaneId, startNode);
                        }
                        catch (Exception ex)
                        {
                            Log.Warn(
                                LogCategory.Bridge,
                                LogRole.Host,
                                "[LaneConnector] AreLanesConnected failed | source={0} target={1} error={2}",
                                source.LaneId,
                                candidate.LaneId,
                                ex);
                            connected = false;
                        }

                        if (connected)
                        {
                            entry.Targets.Add(new LaneConnectorAppliedCommand.Target
                            {
                                SegmentId = candidate.SegmentId,
                                StartNode = candidate.StartNode,
                                Ordinal = targetOrdinal
                            });
                        }
                    }
                }

                command.Items.Add(entry);
            }

            snapshot = command;
            return true;
        }

        internal static bool TryApply(
            LaneConnectorUpdateRequest request,
            out string failureReason)
        {
            failureReason = null;

            if (request == null)
            {
                failureReason = "request_null";
                return false;
            }

            if (!TryGetManager(out var manager))
            {
                failureReason = "manager_unavailable";
                return false;
            }

            if (!LaneConnectorEndSelector.TryGetCandidates(
                    request.NodeId,
                    request.SegmentId,
                    towardsNode: true,
                    out var startNode,
                    out var sourceCandidates))
            {
                failureReason = "source_candidates_missing";
                return false;
            }

            var outgoingMap = LaneConnectorEndSelector.BuildCandidateMap(request.NodeId, towardsNode: false);
            if (outgoingMap.Count == 0)
            {
                failureReason = "target_candidates_missing";
                return false;
            }

            var sourceLookup = BuildSourceLookup(sourceCandidates);
            if (sourceLookup.Count == 0)
            {
                failureReason = "source_lookup_empty";
                return false;
            }

            var targetLookup = BuildTargetLookup(outgoingMap);

            var desired = BuildDesiredMapping(request, sourceLookup, targetLookup);

            using (LaneConnectorScope.Begin())
            {
                foreach (var source in sourceCandidates)
                {
                    var sourceLaneId = source.LaneId;
                    if (!desired.TryGetValue(sourceLaneId, out var desiredTargets))
                        desiredTargets = new HashSet<uint>();

                    var existingTargets = GatherExistingTargets(
                        manager,
                        outgoingMap,
                        sourceLaneId,
                        startNode);

                    foreach (var obsolete in existingTargets.Except(desiredTargets).ToArray())
                    {
                        if (!LaneConnectionAdapter.TryRemoveConnection(sourceLaneId, obsolete, startNode))
                        {
                            failureReason = "remove_failed";
                            return false;
                        }
                    }

                    foreach (var missing in desiredTargets.Except(existingTargets).ToArray())
                    {
                        if (!LaneConnectionAdapter.TryAddConnection(sourceLaneId, missing, startNode))
                        {
                            failureReason = "add_failed";
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        internal static bool IsLocalApplyActive => LaneConnectorScope.IsActive;

        internal static IDisposable StartLocalApply() => LaneConnectorScope.Begin();

        private static Dictionary<int, uint> BuildSourceLookup(List<LaneConnectorEndSelector.Candidate> candidates)
        {
            var dict = new Dictionary<int, uint>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
                dict[i] = candidates[i].LaneId;
            return dict;
        }

        private static Dictionary<LaneConnectorEndSelector.SegmentEndKey, Dictionary<int, uint>> BuildTargetLookup(
            Dictionary<LaneConnectorEndSelector.SegmentEndKey, List<LaneConnectorEndSelector.Candidate>> map)
        {
            var dict = new Dictionary<LaneConnectorEndSelector.SegmentEndKey, Dictionary<int, uint>>();
            foreach (var kvp in map)
            {
                var sub = new Dictionary<int, uint>();
                var list = kvp.Value;
                for (int i = 0; i < list.Count; i++)
                    sub[i] = list[i].LaneId;

                dict[kvp.Key] = sub;
            }
            return dict;
        }

        private static Dictionary<uint, HashSet<uint>> BuildDesiredMapping(
            LaneConnectorUpdateRequest request,
            Dictionary<int, uint> sourceLookup,
            Dictionary<LaneConnectorEndSelector.SegmentEndKey, Dictionary<int, uint>> targetLookup)
        {
            var desired = new Dictionary<uint, HashSet<uint>>();

            foreach (var entry in request.Items ?? Enumerable.Empty<LaneConnectorUpdateRequest.Entry>())
            {
                if (!sourceLookup.TryGetValue(entry.SourceOrdinal, out var sourceLaneId))
                    continue;

                if (!desired.TryGetValue(sourceLaneId, out var set))
                {
                    set = new HashSet<uint>();
                    desired[sourceLaneId] = set;
                }

                foreach (var target in entry.Targets ?? Enumerable.Empty<LaneConnectorUpdateRequest.Target>())
                {
                    var key = new LaneConnectorEndSelector.SegmentEndKey(target.SegmentId, target.StartNode);
                    if (!targetLookup.TryGetValue(key, out var lookup))
                        continue;

                    if (!lookup.TryGetValue(target.Ordinal, out var targetLaneId))
                        continue;

                    set.Add(targetLaneId);
                }
            }

            return desired;
        }

        private static HashSet<uint> GatherExistingTargets(
            ILaneConnectionManager manager,
            Dictionary<LaneConnectorEndSelector.SegmentEndKey, List<LaneConnectorEndSelector.Candidate>> outgoingMap,
            uint sourceLaneId,
            bool sourceStartNode)
        {
            var set = new HashSet<uint>();

            foreach (var list in outgoingMap.Values)
            {
                foreach (var candidate in list)
                {
                    bool connected;
                    try
                    {
                        connected = manager.AreLanesConnected(sourceLaneId, candidate.LaneId, sourceStartNode);
                    }
                    catch
                    {
                        connected = false;
                    }

                    if (connected)
                        set.Add(candidate.LaneId);
                }
            }

            return set;
        }

        private static bool TryGetManager(out ILaneConnectionManager manager)
        {
            manager = null;
            try
            {
                manager = Implementations.ManagerFactory?.LaneConnectionManager;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, LogRole.General, "[LaneConnector] ManagerFactory resolution failed | error={0}", ex);
            }

            if (manager == null)
                Log.Debug(LogCategory.Bridge, "[LaneConnector] LaneConnectionManager unavailable.");

            return manager != null;
        }
    }
}
