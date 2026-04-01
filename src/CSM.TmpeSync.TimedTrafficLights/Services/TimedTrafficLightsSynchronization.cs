using System;
using System.Collections.Generic;
using System.Linq;
using ColossalFramework;
using CSM.API.Commands;
using CSM.API.Networking;
using CSM.TmpeSync.Messages.System;
using CSM.TmpeSync.Services;
using CSM.TmpeSync.TimedTrafficLights.Messages;

namespace CSM.TmpeSync.TimedTrafficLights.Services
{
    internal static class TimedTrafficLightsSynchronization
    {
        private const ulong RemovedPendingHash = ulong.MaxValue;
        private const uint PendingTimeoutFrames = 300;
        private const uint ResyncRequestCooldownFrames = 120;
        private const string TimedRejectionPrefix = "timed_tl:";

        private static readonly object DefinitionGate = new object();
        private static readonly object ClientGate = new object();

        private struct PendingEntry
        {
            internal ulong Hash;
            internal uint SentAtFrame;
            internal int Attempt;
        }

        private static readonly Dictionary<ushort, PendingEntry> ClientPendingDefinitionRequests =
            new Dictionary<ushort, PendingEntry>();
        private static readonly Dictionary<ushort, PendingEntry> ClientPendingRuntimeRequests =
            new Dictionary<ushort, PendingEntry>();

        private static readonly HashSet<ushort> ClientDirtyDefinitionMasters = new HashSet<ushort>();
        private static readonly HashSet<ushort> ClientDirtyRuntimeMasters = new HashSet<ushort>();
        private static readonly Dictionary<ushort, uint> ClientResyncRequestedAtFrame =
            new Dictionary<ushort, uint>();
        private static bool _clientDirtyAllDefinitionHint;
        private static bool _clientDirtyAllRuntimeHint;

        internal static void HandleClientConnect(Player player)
        {
            if (!CsmBridge.IsServerInstance())
                return;

            var clientId = CsmBridge.TryGetClientId(player);
            if (clientId < 0)
                return;

            NetworkUtil.RunOnSimulation(() =>
            {
                lock (DefinitionGate)
                {
                    PublishDefinitionDeltaFromCurrentState("client_connect");

                    var definitions = TimedTrafficLightsStateCache.GetAll();
                    for (var i = 0; i < definitions.Count; i++)
                        CsmBridge.SendToClient(clientId, definitions[i]);

                    var masters = TimedTrafficLightsStateCache.GetKnownMasterNodeIds();
                    for (var i = 0; i < masters.Count; i++)
                    {
                        TimedTrafficLightsRuntimeState runtime;
                        if (!TimedTrafficLightsTmpeAdapter.TryReadRuntime(masters[i], out runtime) || runtime == null)
                            continue;

                        CsmBridge.SendToClient(clientId, new TimedTrafficLightsRuntimeAppliedCommand
                        {
                            Runtime = TimedTrafficLightsRuntimeCache.CloneRuntime(runtime)
                        });
                    }
                }
            });
        }

        internal static void NotifyLocalInteraction(string origin)
        {
            if (CsmBridge.IsServerInstance())
                return;

            lock (ClientGate)
            {
                _clientDirtyAllDefinitionHint = true;
            }

            Log.Debug(
                LogCategory.Network,
                LogRole.Client,
                "[TimedTrafficLights] Local interaction captured (definition dirty hint) | origin={0}",
                origin ?? "unknown");
        }

        internal static void NotifyLocalInteraction(ushort masterNodeId, string origin)
        {
            if (CsmBridge.IsServerInstance())
                return;

            if (masterNodeId == 0)
            {
                NotifyLocalInteraction(origin);
                return;
            }

            var changed = false;
            lock (ClientGate)
            {
                changed = ClientDirtyDefinitionMasters.Add(masterNodeId);
            }

            if (changed)
            {
                Log.Debug(
                    LogCategory.Network,
                    LogRole.Client,
                    "[TimedTrafficLights] Local interaction captured | master={0} origin={1}",
                    masterNodeId,
                    origin ?? "unknown");
            }
        }

        internal static void NotifyLocalRuntimeInteraction(string origin)
        {
            if (CsmBridge.IsServerInstance())
                return;

            lock (ClientGate)
            {
                _clientDirtyAllRuntimeHint = true;
            }

            Log.Debug(
                LogCategory.Network,
                LogRole.Client,
                "[TimedTrafficLights] Local runtime interaction captured (runtime dirty hint) | origin={0}",
                origin ?? "unknown");
        }

        internal static void NotifyLocalRuntimeInteraction(ushort masterNodeId, string origin)
        {
            if (CsmBridge.IsServerInstance())
                return;

            if (masterNodeId == 0)
            {
                NotifyLocalRuntimeInteraction(origin);
                return;
            }

            var changed = false;
            lock (ClientGate)
            {
                changed = ClientDirtyRuntimeMasters.Add(masterNodeId);
            }

            if (changed)
            {
                Log.Debug(
                    LogCategory.Network,
                    LogRole.Client,
                    "[TimedTrafficLights] Local runtime interaction captured | master={0} origin={1}",
                    masterNodeId,
                    origin ?? "unknown");
            }
        }

        internal static void NotifyLocalNodeInteraction(ushort nodeId, string origin)
        {
            if (CsmBridge.IsServerInstance() || nodeId == 0)
                return;

            ushort masterNodeId;
            if (!TimedTrafficLightsStateCache.TryGetMasterForNode(nodeId, out masterNodeId) || masterNodeId == 0)
            {
                if (!TimedTrafficLightsTmpeAdapter.TryResolveMasterNodeId(nodeId, out masterNodeId) || masterNodeId == 0)
                    masterNodeId = nodeId;
            }

            NotifyLocalInteraction(masterNodeId, origin);
        }

        internal static void HandleDefinitionUpdateRequest(TimedTrafficLightsDefinitionUpdateRequest request)
        {
            if (request == null)
                return;

            var senderId = CsmBridge.GetSenderId(request);
            var masterNodeId = request.MasterNodeId != 0
                ? request.MasterNodeId
                : request.Definition != null ? request.Definition.MasterNodeId : (ushort)0;

            if (masterNodeId != 0 && request.Definition != null && request.Definition.MasterNodeId == 0)
                request.Definition.MasterNodeId = masterNodeId;

            Log.Info(
                LogCategory.Network,
                LogRole.Host,
                "[TimedTrafficLights] DefinitionUpdateRequest received | sender={0} master={1} removed={2}",
                senderId,
                masterNodeId,
                request.Removed);

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, LogRole.Client, "[TimedTrafficLights] Ignoring DefinitionUpdateRequest on client.");
                return;
            }

            if (masterNodeId == 0)
            {
                SendRejection(senderId, "invalid_payload", 0);
                return;
            }

            if (!request.Removed && request.Definition == null)
            {
                SendRejection(senderId, "invalid_payload", masterNodeId);
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                lock (DefinitionGate)
                {
                    var lockNodeIds = BuildLockNodeIdsForRequest(request, masterNodeId);
                    using (AcquireNodeLocks(lockNodeIds))
                    {
                        string reason;
                        var success = false;

                        using (CsmBridge.StartIgnore())
                        using (TimedTrafficLightsTmpeAdapter.StartLocalApply())
                        {
                            if (request.Removed)
                                success = TimedTrafficLightsTmpeAdapter.TryApplyRemoval(masterNodeId, out reason);
                            else
                                success = TimedTrafficLightsTmpeAdapter.TryApplyDefinition(request.Definition, out reason);
                        }

                        if (!success)
                        {
                            reason = string.IsNullOrEmpty(reason) ? "tmpe_apply_failed" : reason;
                            Log.Warn(
                                LogCategory.Synchronization,
                                LogRole.Host,
                                "[TimedTrafficLights] DefinitionUpdateRequest failed | sender={0} master={1} reason={2}",
                                senderId,
                                masterNodeId,
                                reason);

                            SendRejection(senderId, reason, masterNodeId);
                            return;
                        }
                    }

                    PublishDefinitionDeltaFromCurrentState("host_apply_request");
                }
            });
        }

        internal static void HandleRuntimeUpdateRequest(TimedTrafficLightsRuntimeUpdateRequest request)
        {
            if (request == null)
                return;

            var runtime = TimedTrafficLightsRuntimeCache.CloneRuntime(request.Runtime);
            var senderId = CsmBridge.GetSenderId(request);
            var masterNodeId = runtime != null ? runtime.MasterNodeId : (ushort)0;

            Log.Info(
                LogCategory.Network,
                LogRole.Host,
                "[TimedTrafficLights] RuntimeUpdateRequest received | sender={0} master={1}",
                senderId,
                masterNodeId);

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, LogRole.Client, "[TimedTrafficLights] Ignoring RuntimeUpdateRequest on client.");
                return;
            }

            if (runtime == null || masterNodeId == 0)
            {
                SendRejection(senderId, "runtime_invalid_payload", masterNodeId);
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                lock (DefinitionGate)
                {
                    var lockNodeIds = BuildLockNodeIdsForRuntime(masterNodeId);
                    using (AcquireNodeLocks(lockNodeIds))
                    {
                        string reason;
                        var success = false;
                        using (CsmBridge.StartIgnore())
                        using (TimedTrafficLightsTmpeAdapter.StartLocalApply())
                        {
                            success = TimedTrafficLightsTmpeAdapter.TryApplyRuntime(runtime, out reason);
                        }

                        if (!success)
                        {
                            reason = string.IsNullOrEmpty(reason) ? "tmpe_runtime_apply_failed" : reason;
                            Log.Warn(
                                LogCategory.Synchronization,
                                LogRole.Host,
                                "[TimedTrafficLights] RuntimeUpdateRequest failed | sender={0} master={1} reason={2}",
                                senderId,
                                masterNodeId,
                                reason);
                            SendRejection(senderId, "runtime_" + reason, masterNodeId);
                            return;
                        }
                    }

                    TimedTrafficLightsRuntimeState appliedRuntime;
                    if (!TimedTrafficLightsTmpeAdapter.TryReadRuntime(masterNodeId, out appliedRuntime) || appliedRuntime == null)
                        appliedRuntime = TimedTrafficLightsRuntimeCache.CloneRuntime(runtime);

                    if (appliedRuntime == null)
                        return;

                    if (appliedRuntime.MasterNodeId == 0)
                        appliedRuntime.MasterNodeId = masterNodeId;

                    appliedRuntime.Epoch = GetCurrentFrame();
                    Dispatch(new TimedTrafficLightsRuntimeAppliedCommand
                    {
                        Runtime = TimedTrafficLightsRuntimeCache.CloneRuntime(appliedRuntime)
                    });
                    TimedTrafficLightsRuntimeCache.StoreBroadcast(appliedRuntime, GetCurrentFrame());
                }
            });
        }

        internal static void HandleDefinitionAppliedCommand(TimedTrafficLightsDefinitionAppliedCommand command)
        {
            if (command == null)
                return;

            var masterNodeId = command.MasterNodeId != 0
                ? command.MasterNodeId
                : command.Definition != null ? command.Definition.MasterNodeId : (ushort)0;

            if (masterNodeId == 0)
                return;

            AcceptHostState(masterNodeId);

            if (CsmBridge.IsServerInstance())
                return;

            NetworkUtil.RunOnSimulation(() =>
            {
                lock (DefinitionGate)
                {
                    if (command.Removed)
                    {
                        using (CsmBridge.StartIgnore())
                        using (TimedTrafficLightsTmpeAdapter.StartLocalApply())
                        {
                            string removeReason;
                            if (!TimedTrafficLightsTmpeAdapter.TryApplyRemoval(masterNodeId, out removeReason))
                            {
                                Log.Warn(
                                    LogCategory.Synchronization,
                                    LogRole.Client,
                                    "[TimedTrafficLights] Failed to apply removed definition | master={0} reason={1}",
                                    masterNodeId,
                                    removeReason ?? "unknown");

                                RequestMasterResync(masterNodeId, "definition_remove_apply_failed:" + (removeReason ?? "unknown"));
                            }
                        }

                        TimedTrafficLightsStateCache.Store(new TimedTrafficLightsDefinitionAppliedCommand
                        {
                            MasterNodeId = masterNodeId,
                            Removed = true,
                            Definition = null
                        });

                        TimedTrafficLightsRuntimeCache.RemoveReceived(masterNodeId);
                        TimedTrafficLightsRuntimeCache.RemoveBroadcast(masterNodeId);
                        return;
                    }

                    var normalizedDefinition = TimedTrafficLightsStateCache.CloneDefinition(command.Definition);
                    if (normalizedDefinition != null && normalizedDefinition.MasterNodeId == 0)
                        normalizedDefinition.MasterNodeId = masterNodeId;

                    using (CsmBridge.StartIgnore())
                    using (TimedTrafficLightsTmpeAdapter.StartLocalApply())
                    {
                        string applyReason;
                        if (!TimedTrafficLightsTmpeAdapter.TryApplyDefinition(normalizedDefinition, out applyReason))
                        {
                            Log.Warn(
                                LogCategory.Synchronization,
                                LogRole.Client,
                                "[TimedTrafficLights] Failed to apply host definition | master={0} reason={1}",
                                masterNodeId,
                                applyReason ?? "unknown");

                            RequestMasterResync(masterNodeId, "definition_apply_failed:" + (applyReason ?? "unknown"));
                        }
                    }

                    TimedTrafficLightsStateCache.Store(new TimedTrafficLightsDefinitionAppliedCommand
                    {
                        MasterNodeId = masterNodeId,
                        Removed = false,
                        Definition = normalizedDefinition
                    });

                    TimedTrafficLightsRuntimeState pendingRuntime;
                    if (!TimedTrafficLightsRuntimeCache.TryGetReceived(masterNodeId, out pendingRuntime) || pendingRuntime == null)
                        return;

                    using (CsmBridge.StartIgnore())
                    using (TimedTrafficLightsTmpeAdapter.StartLocalApply())
                    {
                        string runtimeReason;
                        if (!TimedTrafficLightsTmpeAdapter.TryApplyRuntime(pendingRuntime, out runtimeReason))
                        {
                            Log.Debug(
                                LogCategory.Synchronization,
                                LogRole.Client,
                                "[TimedTrafficLights] Pending runtime apply skipped | master={0} reason={1}",
                                masterNodeId,
                                runtimeReason ?? "unknown");

                            RequestMasterResync(masterNodeId, "runtime_after_definition_failed:" + (runtimeReason ?? "unknown"));
                        }
                    }
                }
            });
        }

        internal static void HandleRuntimeAppliedCommand(TimedTrafficLightsRuntimeAppliedCommand command)
        {
            if (command == null || command.Runtime == null)
                return;

            var runtime = TimedTrafficLightsRuntimeCache.CloneRuntime(command.Runtime);
            if (runtime == null || runtime.MasterNodeId == 0)
                return;

            if (CsmBridge.IsServerInstance())
                return;

            AcceptHostRuntimeState(runtime.MasterNodeId);
            TimedTrafficLightsRuntimeCache.StoreReceived(runtime);

            NetworkUtil.RunOnSimulation(() =>
            {
                if (TimedTrafficLightsTmpeAdapter.IsLocalApplyActive)
                    return;

                using (CsmBridge.StartIgnore())
                using (TimedTrafficLightsTmpeAdapter.StartLocalApply())
                {
                    string reason;
                    if (!TimedTrafficLightsTmpeAdapter.TryApplyRuntime(runtime, out reason))
                    {
                        Log.Debug(
                            LogCategory.Synchronization,
                            LogRole.Client,
                            "[TimedTrafficLights] Runtime apply deferred | master={0} reason={1}",
                            runtime.MasterNodeId,
                            reason ?? "unknown");

                        var shouldRequestResync = !string.Equals(reason, "master_not_found", StringComparison.Ordinal);
                        if (!shouldRequestResync)
                        {
                            TimedTrafficLightsDefinitionState definition;
                            shouldRequestResync = TimedTrafficLightsStateCache.TryGetDefinition(runtime.MasterNodeId, out definition) && definition != null;
                        }

                        if (shouldRequestResync)
                            RequestMasterResync(runtime.MasterNodeId, "runtime_apply_failed:" + (reason ?? "unknown"));
                    }
                }
            });
        }

        internal static void HandleResyncRequest(TimedTrafficLightsResyncRequest request)
        {
            if (request == null)
                return;

            if (!CsmBridge.IsServerInstance())
                return;

            var senderId = CsmBridge.GetSenderId(request);
            if (senderId < 0)
                return;

            NetworkUtil.RunOnSimulation(() =>
            {
                lock (DefinitionGate)
                {
                    PublishDefinitionDeltaFromCurrentState("resync_request");

                    if (!TrySendMasterResyncToClient(senderId, request.MasterNodeId))
                    {
                        Log.Warn(
                            LogCategory.Synchronization,
                            LogRole.Host,
                            "[TimedTrafficLights] Resync request unresolved | sender={0} requestedMaster={1}",
                            senderId,
                            request.MasterNodeId);
                    }
                }
            });
        }

        internal static void HandleRequestRejected(RequestRejected command)
        {
            if (command == null || CsmBridge.IsServerInstance())
                return;

            if (command.EntityType != 3)
                return;

            var masterNodeId = command.EntityId > ushort.MaxValue ? (ushort)0 : (ushort)command.EntityId;
            if (masterNodeId == 0)
                return;

            if (!IsTimedRejection(command.Reason))
                return;

            lock (ClientGate)
            {
                ClientPendingDefinitionRequests.Remove(masterNodeId);
                ClientPendingRuntimeRequests.Remove(masterNodeId);
                ClientDirtyDefinitionMasters.Add(masterNodeId);
                ClientDirtyRuntimeMasters.Add(masterNodeId);
            }

            Log.Warn(
                LogCategory.Network,
                LogRole.Client,
                "[TimedTrafficLights] Request rejected | master={0} reason={1}",
                masterNodeId,
                StripTimedRejectionTag(command.Reason));
        }

        internal static void ProcessDefinitionTick()
        {
            if (TimedTrafficLightsTmpeAdapter.IsLocalApplyActive)
                return;

            lock (DefinitionGate)
            {
                if (!CsmBridge.IsServerInstance())
                    ProcessPendingTimeouts();

                PublishDefinitionDeltaFromCurrentState("periodic_poll");
            }
        }

        internal static void ProcessRuntimeTick()
        {
            if (TimedTrafficLightsTmpeAdapter.IsLocalApplyActive)
                return;

            lock (DefinitionGate)
            {
                if (!CsmBridge.IsServerInstance())
                {
                    PublishClientRuntimeDelta("periodic_runtime_poll");
                    return;
                }

                var masters = TimedTrafficLightsStateCache.GetKnownMasterNodeIds();
                if (masters.Count == 0)
                    return;

                var activeMasters = new HashSet<ushort>();
                var frameNow = GetCurrentFrame();

                for (var i = 0; i < masters.Count; i++)
                {
                    TimedTrafficLightsRuntimeState runtime;
                    if (!TimedTrafficLightsTmpeAdapter.TryReadRuntime(masters[i], out runtime) || runtime == null || runtime.MasterNodeId == 0)
                        continue;

                    activeMasters.Add(runtime.MasterNodeId);

                    TimedTrafficLightsRuntimeState lastBroadcast;
                    uint lastSentAt;
                    var hasLast = TimedTrafficLightsRuntimeCache.TryGetBroadcast(runtime.MasterNodeId, out lastBroadcast, out lastSentAt);

                    var changed = !hasLast ||
                                  lastBroadcast == null ||
                                  lastBroadcast.IsRunning != runtime.IsRunning ||
                                  lastBroadcast.CurrentStep != runtime.CurrentStep;

                    var keyframeDue = !hasLast || IsFrameElapsed(frameNow, lastSentAt, TimedTrafficLightsRuntimeTracker.KeyframeIntervalFrames);

                    if (!changed && !keyframeDue)
                        continue;

                    var command = new TimedTrafficLightsRuntimeAppliedCommand
                    {
                        Runtime = TimedTrafficLightsRuntimeCache.CloneRuntime(runtime)
                    };

                    Dispatch(command);
                    TimedTrafficLightsRuntimeCache.StoreBroadcast(runtime, frameNow);
                }

                var staleBroadcastMasters = TimedTrafficLightsRuntimeCache.GetBroadcastMasterNodeIds();
                for (var i = 0; i < staleBroadcastMasters.Count; i++)
                {
                    var masterNodeId = staleBroadcastMasters[i];
                    if (activeMasters.Contains(masterNodeId))
                        continue;

                    TimedTrafficLightsRuntimeCache.RemoveBroadcast(masterNodeId);
                }
            }
        }

        internal static void Dispatch(CommandBase command)
        {
            if (command == null)
                return;

            if (CsmBridge.IsServerInstance())
                CsmBridge.SendToAll(command);
            else
                CsmBridge.SendToServer(command);
        }

        private static void PublishDefinitionDeltaFromCurrentState(string origin)
        {
            Dictionary<ushort, TimedTrafficLightsDefinitionState> currentDefinitions;
            TimedTrafficLightsTmpeAdapter.TryCaptureAllDefinitions(out currentDefinitions);

            if (CsmBridge.IsServerInstance())
                PublishHostDefinitionDelta(currentDefinitions, origin);
            else
                PublishClientDefinitionDelta(currentDefinitions, origin);
        }

        private static void PublishHostDefinitionDelta(Dictionary<ushort, TimedTrafficLightsDefinitionState> currentDefinitions, string origin)
        {
            currentDefinitions = currentDefinitions ?? new Dictionary<ushort, TimedTrafficLightsDefinitionState>();
            var cachedHashes = TimedTrafficLightsStateCache.GetHashes();

            var changedMasters = new List<ushort>();
            foreach (var kvp in currentDefinitions)
            {
                var hash = TimedTrafficLightsStateCache.ComputeHash(kvp.Value);
                ulong cachedHash;
                if (!cachedHashes.TryGetValue(kvp.Key, out cachedHash) || cachedHash != hash)
                    changedMasters.Add(kvp.Key);
            }

            changedMasters.Sort();
            for (var i = 0; i < changedMasters.Count; i++)
            {
                var master = changedMasters[i];
                TimedTrafficLightsDefinitionState definition;
                if (!currentDefinitions.TryGetValue(master, out definition) || definition == null)
                    continue;

                var applied = new TimedTrafficLightsDefinitionAppliedCommand
                {
                    MasterNodeId = master,
                    Removed = false,
                    Definition = TimedTrafficLightsStateCache.CloneDefinition(definition)
                };

                if (!TimedTrafficLightsStateCache.Store(applied))
                    continue;

                Log.Debug(
                    LogCategory.Synchronization,
                    LogRole.Host,
                    "[TimedTrafficLights] Host definition broadcast | master={0} origin={1}",
                    master,
                    origin ?? "unspecified");

                Dispatch(TimedTrafficLightsStateCache.CloneApplied(applied));
            }

            var removedMasters = cachedHashes.Keys
                .Where(master => !currentDefinitions.ContainsKey(master))
                .OrderBy(master => master)
                .ToList();

            for (var i = 0; i < removedMasters.Count; i++)
            {
                var master = removedMasters[i];
                var removed = new TimedTrafficLightsDefinitionAppliedCommand
                {
                    MasterNodeId = master,
                    Removed = true,
                    Definition = null
                };

                if (!TimedTrafficLightsStateCache.Store(removed))
                    continue;

                TimedTrafficLightsRuntimeCache.RemoveBroadcast(master);
                TimedTrafficLightsRuntimeCache.RemoveReceived(master);
                Dispatch(removed);
            }
        }

        private static void PublishClientDefinitionDelta(Dictionary<ushort, TimedTrafficLightsDefinitionState> currentDefinitions, string origin)
        {
            currentDefinitions = currentDefinitions ?? new Dictionary<ushort, TimedTrafficLightsDefinitionState>();
            var cachedHashes = TimedTrafficLightsStateCache.GetHashes();
            var dirtyMasters = TryCollectDirtyDefinitionMasters(currentDefinitions, cachedHashes);
            if (dirtyMasters.Count == 0)
                return;

            var frameNow = GetCurrentFrame();
            var orderedDirtyMasters = dirtyMasters.OrderBy(id => id).ToList();
            for (var i = 0; i < orderedDirtyMasters.Count; i++)
            {
                var master = orderedDirtyMasters[i];
                if (master == 0)
                    continue;

                TimedTrafficLightsDefinitionState definition;
                var hasCurrent = currentDefinitions.TryGetValue(master, out definition) && definition != null;

                ulong cachedHash;
                var hasCached = cachedHashes.TryGetValue(master, out cachedHash);

                if (hasCurrent)
                {
                    var hash = TimedTrafficLightsStateCache.ComputeHash(definition);
                    if (hasCached && cachedHash == hash)
                    {
                        if (!HasDefinitionPending(master))
                            ClearClientDefinitionDirty(master);
                        continue;
                    }

                    if (!TryMarkClientDefinitionPending(master, hash, frameNow))
                        continue;

                    Dispatch(new TimedTrafficLightsDefinitionUpdateRequest
                    {
                        MasterNodeId = master,
                        Removed = false,
                        Definition = TimedTrafficLightsStateCache.CloneDefinition(definition)
                    });

                    Log.Debug(
                        LogCategory.Network,
                        LogRole.Client,
                        "[TimedTrafficLights] Client definition request sent | master={0} origin={1}",
                        master,
                        origin ?? "unspecified");
                    continue;
                }

                if (hasCached)
                {
                    if (!TryMarkClientDefinitionPending(master, RemovedPendingHash, frameNow))
                        continue;

                    Dispatch(new TimedTrafficLightsDefinitionUpdateRequest
                    {
                        MasterNodeId = master,
                        Removed = true,
                        Definition = null
                    });

                    Log.Debug(
                        LogCategory.Network,
                        LogRole.Client,
                        "[TimedTrafficLights] Client removal request sent | master={0} origin={1}",
                        master,
                        origin ?? "unspecified");
                    continue;
                }

                if (!HasDefinitionPending(master))
                    ClearClientDefinitionDirty(master);
            }
        }

        private static void PublishClientRuntimeDelta(string origin)
        {
            var dirtyMasters = TryCollectDirtyRuntimeMasters();
            if (dirtyMasters.Count == 0)
                return;

            var frameNow = GetCurrentFrame();
            var orderedDirtyMasters = dirtyMasters.OrderBy(id => id).ToList();
            for (var i = 0; i < orderedDirtyMasters.Count; i++)
            {
                var masterNodeId = orderedDirtyMasters[i];
                if (masterNodeId == 0)
                    continue;

                TimedTrafficLightsRuntimeState runtime;
                var hasRuntime = TimedTrafficLightsTmpeAdapter.TryReadRuntime(masterNodeId, out runtime) &&
                                 runtime != null;

                if (!hasRuntime)
                {
                    if (!HasRuntimePending(masterNodeId))
                        ClearClientRuntimeDirty(masterNodeId);
                    continue;
                }

                if (runtime.MasterNodeId == 0)
                    runtime.MasterNodeId = masterNodeId;

                TimedTrafficLightsRuntimeState received;
                var hasReceived = TimedTrafficLightsRuntimeCache.TryGetReceived(runtime.MasterNodeId, out received) && received != null;
                if (hasReceived && RuntimeEquivalent(received, runtime))
                {
                    if (!HasRuntimePending(runtime.MasterNodeId))
                        ClearClientRuntimeDirty(runtime.MasterNodeId);
                    continue;
                }

                var hash = ComputeRuntimeHash(runtime);
                if (!TryMarkClientRuntimePending(runtime.MasterNodeId, hash, frameNow))
                    continue;

                Dispatch(new TimedTrafficLightsRuntimeUpdateRequest
                {
                    Runtime = TimedTrafficLightsRuntimeCache.CloneRuntime(runtime)
                });

                Log.Debug(
                    LogCategory.Network,
                    LogRole.Client,
                    "[TimedTrafficLights] Client runtime request sent | master={0} running={1} step={2} origin={3}",
                    runtime.MasterNodeId,
                    runtime.IsRunning,
                    runtime.CurrentStep,
                    origin ?? "unspecified");
            }
        }

        private static void SendRejection(int clientId, string reason, ushort masterNodeId)
        {
            if (clientId < 0)
                return;

            CsmBridge.SendToClient(clientId, new RequestRejected
            {
                Reason = EnsureTimedRejectionTag(reason),
                EntityId = masterNodeId,
                EntityType = 3
            });
        }

        private static bool TryMarkClientDefinitionPending(ushort masterNodeId, ulong hash, uint frameNow)
        {
            return TryMarkClientPending(ClientPendingDefinitionRequests, masterNodeId, hash, frameNow);
        }

        private static bool TryMarkClientRuntimePending(ushort masterNodeId, ulong hash, uint frameNow)
        {
            return TryMarkClientPending(ClientPendingRuntimeRequests, masterNodeId, hash, frameNow);
        }

        private static bool TryMarkClientPending(
            Dictionary<ushort, PendingEntry> pendingByMaster,
            ushort masterNodeId,
            ulong hash,
            uint frameNow)
        {
            lock (ClientGate)
            {
                PendingEntry existing;
                if (pendingByMaster.TryGetValue(masterNodeId, out existing))
                {
                    if (existing.Hash == hash && !IsFrameElapsed(frameNow, existing.SentAtFrame, PendingTimeoutFrames))
                        return false;

                    existing.Hash = hash;
                    existing.SentAtFrame = frameNow;
                    existing.Attempt++;
                    pendingByMaster[masterNodeId] = existing;
                    return true;
                }

                pendingByMaster[masterNodeId] = new PendingEntry
                {
                    Hash = hash,
                    SentAtFrame = frameNow,
                    Attempt = 1
                };
                return true;
            }
        }

        private static bool HasDefinitionPending(ushort masterNodeId)
        {
            lock (ClientGate)
            {
                return ClientPendingDefinitionRequests.ContainsKey(masterNodeId);
            }
        }

        private static bool HasRuntimePending(ushort masterNodeId)
        {
            lock (ClientGate)
            {
                return ClientPendingRuntimeRequests.ContainsKey(masterNodeId);
            }
        }

        private static void AcceptHostState(ushort masterNodeId)
        {
            lock (ClientGate)
            {
                ClientPendingDefinitionRequests.Remove(masterNodeId);
                ClientPendingRuntimeRequests.Remove(masterNodeId);
                ClientDirtyDefinitionMasters.Remove(masterNodeId);
                ClientDirtyRuntimeMasters.Remove(masterNodeId);
                ClientResyncRequestedAtFrame.Remove(masterNodeId);
            }
        }

        private static void AcceptHostRuntimeState(ushort masterNodeId)
        {
            lock (ClientGate)
            {
                ClientPendingRuntimeRequests.Remove(masterNodeId);
                ClientDirtyRuntimeMasters.Remove(masterNodeId);
                ClientResyncRequestedAtFrame.Remove(masterNodeId);
            }
        }

        private static void ClearClientDefinitionDirty(ushort masterNodeId)
        {
            lock (ClientGate)
            {
                ClientDirtyDefinitionMasters.Remove(masterNodeId);
            }
        }

        private static void ClearClientRuntimeDirty(ushort masterNodeId)
        {
            lock (ClientGate)
            {
                ClientDirtyRuntimeMasters.Remove(masterNodeId);
            }
        }

        private static void ProcessPendingTimeouts()
        {
            var frameNow = GetCurrentFrame();
            var expiredDefinition = new List<ushort>();
            var expiredRuntime = new List<ushort>();

            lock (ClientGate)
            {
                foreach (var kvp in ClientPendingDefinitionRequests)
                {
                    if (IsFrameElapsed(frameNow, kvp.Value.SentAtFrame, PendingTimeoutFrames))
                        expiredDefinition.Add(kvp.Key);
                }

                foreach (var kvp in ClientPendingRuntimeRequests)
                {
                    if (IsFrameElapsed(frameNow, kvp.Value.SentAtFrame, PendingTimeoutFrames))
                        expiredRuntime.Add(kvp.Key);
                }

                for (var i = 0; i < expiredDefinition.Count; i++)
                {
                    var masterNodeId = expiredDefinition[i];
                    ClientPendingDefinitionRequests.Remove(masterNodeId);
                    ClientDirtyDefinitionMasters.Add(masterNodeId);
                }

                for (var i = 0; i < expiredRuntime.Count; i++)
                {
                    var masterNodeId = expiredRuntime[i];
                    ClientPendingRuntimeRequests.Remove(masterNodeId);
                    ClientDirtyRuntimeMasters.Add(masterNodeId);
                }
            }

            for (var i = 0; i < expiredDefinition.Count; i++)
            {
                Log.Warn(
                    LogCategory.Network,
                    LogRole.Client,
                    "[TimedTrafficLights] Pending definition request timed out | master={0}",
                    expiredDefinition[i]);
            }

            for (var i = 0; i < expiredRuntime.Count; i++)
            {
                Log.Warn(
                    LogCategory.Network,
                    LogRole.Client,
                    "[TimedTrafficLights] Pending runtime request timed out | master={0}",
                    expiredRuntime[i]);
            }
        }

        private static HashSet<ushort> TryCollectDirtyDefinitionMasters(
            Dictionary<ushort, TimedTrafficLightsDefinitionState> currentDefinitions,
            Dictionary<ushort, ulong> cachedHashes)
        {
            var dirtySnapshot = new HashSet<ushort>();

            lock (ClientGate)
            {
                if (_clientDirtyAllDefinitionHint)
                {
                    foreach (var master in currentDefinitions.Keys)
                        if (master != 0)
                            ClientDirtyDefinitionMasters.Add(master);

                    foreach (var master in cachedHashes.Keys)
                        if (master != 0)
                            ClientDirtyDefinitionMasters.Add(master);

                    _clientDirtyAllDefinitionHint = false;
                }

                foreach (var master in ClientDirtyDefinitionMasters)
                    dirtySnapshot.Add(master);
            }

            return dirtySnapshot;
        }

        private static HashSet<ushort> TryCollectDirtyRuntimeMasters()
        {
            var dirtySnapshot = new HashSet<ushort>();

            lock (ClientGate)
            {
                if (_clientDirtyAllRuntimeHint)
                {
                    var masters = TimedTrafficLightsStateCache.GetKnownMasterNodeIds();
                    for (var i = 0; i < masters.Count; i++)
                    {
                        var master = masters[i];
                        if (master != 0)
                            ClientDirtyRuntimeMasters.Add(master);
                    }

                    _clientDirtyAllRuntimeHint = false;
                }

                foreach (var master in ClientDirtyRuntimeMasters)
                    dirtySnapshot.Add(master);
            }

            return dirtySnapshot;
        }

        private static void RequestMasterResync(ushort masterNodeId, string reason)
        {
            if (CsmBridge.IsServerInstance() || masterNodeId == 0)
                return;

            if (!ShouldSendResyncRequest(masterNodeId))
                return;

            Dispatch(new TimedTrafficLightsResyncRequest
            {
                MasterNodeId = masterNodeId
            });

            Log.Warn(
                LogCategory.Network,
                LogRole.Client,
                "[TimedTrafficLights] Resync request sent | master={0} reason={1}",
                masterNodeId,
                reason ?? "unknown");
        }

        private static bool ShouldSendResyncRequest(ushort masterNodeId)
        {
            var frameNow = GetCurrentFrame();
            lock (ClientGate)
            {
                uint previous;
                if (ClientResyncRequestedAtFrame.TryGetValue(masterNodeId, out previous) &&
                    !IsFrameElapsed(frameNow, previous, ResyncRequestCooldownFrames))
                    return false;

                ClientResyncRequestedAtFrame[masterNodeId] = frameNow;
                ClientPendingDefinitionRequests.Remove(masterNodeId);
                ClientPendingRuntimeRequests.Remove(masterNodeId);
                ClientDirtyDefinitionMasters.Add(masterNodeId);
                ClientDirtyRuntimeMasters.Add(masterNodeId);
                return true;
            }
        }

        private static bool TrySendMasterResyncToClient(int clientId, ushort requestedMasterNodeId)
        {
            if (clientId < 0 || requestedMasterNodeId == 0)
                return false;

            var masterNodeId = ResolveMasterForResyncRequest(requestedMasterNodeId);
            if (masterNodeId == 0)
                return false;

            TimedTrafficLightsDefinitionState definition;
            var hasDefinition = TimedTrafficLightsStateCache.TryGetDefinition(masterNodeId, out definition);
            if (!hasDefinition || definition == null)
            {
                Dictionary<ushort, TimedTrafficLightsDefinitionState> capturedDefinitions;
                TimedTrafficLightsTmpeAdapter.TryCaptureAllDefinitions(out capturedDefinitions);

                TimedTrafficLightsDefinitionState captured;
                if (capturedDefinitions != null && capturedDefinitions.TryGetValue(masterNodeId, out captured))
                {
                    definition = captured;
                    hasDefinition = true;
                }
            }

            if (hasDefinition && definition != null)
            {
                CsmBridge.SendToClient(clientId, new TimedTrafficLightsDefinitionAppliedCommand
                {
                    MasterNodeId = masterNodeId,
                    Removed = false,
                    Definition = TimedTrafficLightsStateCache.CloneDefinition(definition)
                });

                TimedTrafficLightsRuntimeState runtime;
                if (TimedTrafficLightsTmpeAdapter.TryReadRuntime(masterNodeId, out runtime) && runtime != null)
                {
                    CsmBridge.SendToClient(clientId, new TimedTrafficLightsRuntimeAppliedCommand
                    {
                        Runtime = TimedTrafficLightsRuntimeCache.CloneRuntime(runtime)
                    });
                }

                return true;
            }

            CsmBridge.SendToClient(clientId, new TimedTrafficLightsDefinitionAppliedCommand
            {
                MasterNodeId = masterNodeId,
                Removed = true,
                Definition = null
            });
            return true;
        }

        private static ushort ResolveMasterForResyncRequest(ushort requestedMasterNodeId)
        {
            if (requestedMasterNodeId == 0)
                return 0;

            TimedTrafficLightsDefinitionState definition;
            if (TimedTrafficLightsStateCache.TryGetDefinition(requestedMasterNodeId, out definition) && definition != null)
                return requestedMasterNodeId;

            ushort mappedMasterNodeId;
            if (TimedTrafficLightsStateCache.TryGetMasterForNode(requestedMasterNodeId, out mappedMasterNodeId) && mappedMasterNodeId != 0)
                return mappedMasterNodeId;

            ushort resolvedMasterNodeId;
            if (TimedTrafficLightsTmpeAdapter.TryResolveMasterNodeId(requestedMasterNodeId, out resolvedMasterNodeId) && resolvedMasterNodeId != 0)
                return resolvedMasterNodeId;

            return requestedMasterNodeId;
        }

        private static uint GetCurrentFrame()
        {
            var simulationManager = Singleton<SimulationManager>.instance;
            return simulationManager != null ? simulationManager.m_currentFrameIndex : 0u;
        }

        private static bool IsTimedRejection(string reason) =>
            !string.IsNullOrEmpty(reason) &&
            reason.StartsWith(TimedRejectionPrefix, StringComparison.Ordinal);

        private static string EnsureTimedRejectionTag(string reason)
        {
            reason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
            return IsTimedRejection(reason) ? reason : TimedRejectionPrefix + reason;
        }

        private static string StripTimedRejectionTag(string reason)
        {
            if (string.IsNullOrEmpty(reason))
                return "unknown";

            return IsTimedRejection(reason)
                ? reason.Substring(TimedRejectionPrefix.Length)
                : reason;
        }

        private static bool IsFrameElapsed(uint currentFrame, uint previousFrame, uint threshold) =>
            currentFrame - previousFrame >= threshold;

        private static ulong ComputeRuntimeHash(TimedTrafficLightsRuntimeState runtime)
        {
            if (runtime == null)
                return 0;

            unchecked
            {
                var hash = 14695981039346656037UL;
                hash ^= runtime.MasterNodeId;
                hash *= 1099511628211UL;
                hash ^= runtime.IsRunning ? 1UL : 0UL;
                hash *= 1099511628211UL;
                hash ^= (ulong)runtime.CurrentStep;
                hash *= 1099511628211UL;
                return hash;
            }
        }

        private static bool RuntimeEquivalent(TimedTrafficLightsRuntimeState left, TimedTrafficLightsRuntimeState right)
        {
            if (left == null || right == null)
                return false;

            return left.MasterNodeId == right.MasterNodeId &&
                   left.IsRunning == right.IsRunning &&
                   left.CurrentStep == right.CurrentStep;
        }

        private static List<ushort> BuildLockNodeIdsForRuntime(ushort masterNodeId)
        {
            var unique = new HashSet<ushort>();
            if (masterNodeId != 0)
                unique.Add(masterNodeId);

            TimedTrafficLightsDefinitionState definition;
            if (masterNodeId != 0 &&
                TimedTrafficLightsStateCache.TryGetDefinition(masterNodeId, out definition) &&
                definition != null)
            {
                AddDefinitionNodes(definition, unique);
            }

            if (unique.Count <= 1 && masterNodeId != 0)
            {
                ushort resolvedMaster;
                if (TimedTrafficLightsTmpeAdapter.TryResolveMasterNodeId(masterNodeId, out resolvedMaster) && resolvedMaster != 0)
                    unique.Add(resolvedMaster);
            }

            var ordered = unique
                .Where(nodeId => nodeId != 0)
                .OrderBy(nodeId => nodeId)
                .ToList();

            if (ordered.Count == 0 && masterNodeId != 0)
                ordered.Add(masterNodeId);

            return ordered;
        }

        private static List<ushort> BuildLockNodeIdsForRequest(
            TimedTrafficLightsDefinitionUpdateRequest request,
            ushort masterNodeId)
        {
            var unique = new HashSet<ushort>();
            if (masterNodeId != 0)
                unique.Add(masterNodeId);

            if (request != null && request.Definition != null)
                AddDefinitionNodes(request.Definition, unique);

            TimedTrafficLightsDefinitionState cachedDefinition;
            if (masterNodeId != 0 &&
                TimedTrafficLightsStateCache.TryGetDefinition(masterNodeId, out cachedDefinition) &&
                cachedDefinition != null)
            {
                AddDefinitionNodes(cachedDefinition, unique);
            }

            var ordered = unique
                .Where(nodeId => nodeId != 0)
                .OrderBy(nodeId => nodeId)
                .ToList();

            if (ordered.Count == 0 && masterNodeId != 0)
                ordered.Add(masterNodeId);

            return ordered;
        }

        private static void AddDefinitionNodes(TimedTrafficLightsDefinitionState definition, HashSet<ushort> unique)
        {
            if (definition == null || unique == null)
                return;

            if (definition.MasterNodeId != 0)
                unique.Add(definition.MasterNodeId);

            if (definition.NodeGroup != null)
            {
                for (var i = 0; i < definition.NodeGroup.Count; i++)
                {
                    var nodeId = definition.NodeGroup[i];
                    if (nodeId != 0)
                        unique.Add(nodeId);
                }
            }

            if (definition.Nodes != null)
            {
                for (var i = 0; i < definition.Nodes.Count; i++)
                {
                    var nodeState = definition.Nodes[i];
                    if (nodeState != null && nodeState.NodeId != 0)
                        unique.Add(nodeState.NodeId);
                }
            }
        }

        private static IDisposable AcquireNodeLocks(List<ushort> nodeIds)
        {
            if (nodeIds == null || nodeIds.Count == 0)
                return NoopScope.Instance;

            return new NodeLockScope(nodeIds);
        }

        private sealed class NodeLockScope : IDisposable
        {
            private List<EntityLocks.Releaser> _locks;

            internal NodeLockScope(List<ushort> nodeIds)
            {
                _locks = new List<EntityLocks.Releaser>();
                for (var i = 0; i < nodeIds.Count; i++)
                    _locks.Add(EntityLocks.AcquireNode(nodeIds[i]));
            }

            public void Dispose()
            {
                if (_locks == null)
                    return;

                for (var i = _locks.Count - 1; i >= 0; i--)
                    _locks[i].Dispose();

                _locks = null;
            }
        }

        private sealed class NoopScope : IDisposable
        {
            internal static readonly NoopScope Instance = new NoopScope();

            public void Dispose()
            {
            }
        }
    }
}
