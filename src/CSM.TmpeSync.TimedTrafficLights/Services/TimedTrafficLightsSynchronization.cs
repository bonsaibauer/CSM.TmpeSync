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
        private const uint ResyncRequestCooldownFrames = 120;
        private const string TimedRejectionPrefix = "timed_tl:";

        private static readonly object DefinitionGate = new object();
        private static readonly object ClientGate = new object();
        private static readonly object EventGate = new object();

        private static readonly HashSet<ushort> PendingDefinitionMasters = new HashSet<ushort>();
        private static readonly HashSet<ushort> PendingRuntimeMasters = new HashSet<ushort>();
        private static readonly Dictionary<ushort, uint> ClientResyncRequestedAtFrame = new Dictionary<ushort, uint>();

        private static bool _pendingFullDefinitionSync;
        private static bool _pendingFullRuntimeSync;
        private static bool _flushScheduled;
        private static string _pendingDefinitionOrigin;
        private static string _pendingRuntimeOrigin;

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
                    var currentDefinitions = CaptureAllDefinitions();
                    SyncHostCachesWithoutBroadcast(currentDefinitions);

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
            EnqueueDefinitionSync(0, origin);
        }

        internal static void NotifyLocalInteraction(ushort masterNodeId, string origin)
        {
            EnqueueDefinitionSync(masterNodeId, origin);
        }

        internal static void NotifyLocalRuntimeInteraction(string origin)
        {
            EnqueueRuntimeSync(0, origin);
        }

        internal static void NotifyLocalRuntimeInteraction(ushort masterNodeId, string origin)
        {
            EnqueueRuntimeSync(masterNodeId, origin);
        }

        internal static void NotifyLocalNodeInteraction(ushort nodeId, string origin)
        {
            if (nodeId == 0)
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
                "[TimedTrafficLights] Definition update request received | sender={0} master={1} removed={2}.",
                senderId,
                masterNodeId,
                request.Removed);

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, LogRole.Client, "[TimedTrafficLights] Definition update request ignored | reason=not_server_instance.");
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
                                "[TimedTrafficLights] Definition update request failed | sender={0} master={1} reason={2}.",
                                senderId,
                                masterNodeId,
                                reason);

                            SendRejection(senderId, reason, masterNodeId);
                            return;
                        }
                    }

                    PublishDefinitionForMaster(masterNodeId, "host_apply_request");
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
                "[TimedTrafficLights] Runtime update request received | sender={0} master={1}.",
                senderId,
                masterNodeId);

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, LogRole.Client, "[TimedTrafficLights] Runtime update request ignored | reason=not_server_instance.");
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
                                "[TimedTrafficLights] Runtime update request failed | sender={0} master={1} reason={2}.",
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

                    TimedTrafficLightsRuntimeCache.StoreBroadcast(appliedRuntime, appliedRuntime.Epoch);
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
                                    "[TimedTrafficLights] Failed to apply removed definition | master={0} reason={1}.",
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
                                "[TimedTrafficLights] Failed to apply host definition | master={0} reason={1}.",
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
                                "[TimedTrafficLights] Pending runtime apply skipped | master={0} reason={1}.",
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
                            "[TimedTrafficLights] Runtime apply skipped | master={0} reason={1}.",
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
                    var currentDefinitions = CaptureAllDefinitions();
                    SyncHostCachesWithoutBroadcast(currentDefinitions);

                    if (!TrySendMasterResyncToClient(senderId, request.MasterNodeId))
                    {
                        Log.Warn(
                            LogCategory.Synchronization,
                            LogRole.Host,
                            "[TimedTrafficLights] Resync request unresolved | sender={0} requestedMaster={1}.",
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
                ClientResyncRequestedAtFrame.Remove(masterNodeId);
            }

            var reason = StripTimedRejectionTag(command.Reason);
            Log.Warn(
                LogCategory.Network,
                LogRole.Client,
                "[TimedTrafficLights] Request rejected | master={0} reason={1}.",
                masterNodeId,
                reason);

            RequestMasterResync(masterNodeId, "request_rejected:" + reason);
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

        private static void EnqueueDefinitionSync(ushort masterNodeId, string origin)
        {
            if (!NetworkUtil.IsSynchronizationReady())
                return;

            var shouldScheduleFlush = false;

            lock (EventGate)
            {
                if (masterNodeId == 0)
                {
                    _pendingFullDefinitionSync = true;
                }
                else
                {
                    PendingDefinitionMasters.Add(masterNodeId);
                }

                if (!string.IsNullOrEmpty(origin))
                    _pendingDefinitionOrigin = origin;

                if (!_flushScheduled)
                {
                    _flushScheduled = true;
                    shouldScheduleFlush = true;
                }
            }

            Log.Debug(
                LogCategory.Network,
                CsmBridge.IsServerInstance() ? LogRole.Host : LogRole.Client,
                "[TimedTrafficLights] Local definition event queued | master={0} full={1} origin={2}.",
                masterNodeId,
                masterNodeId == 0,
                origin ?? "unknown");

            if (shouldScheduleFlush)
                NetworkUtil.RunOnSimulation(FlushPendingLocalChanges);
        }

        private static void EnqueueRuntimeSync(ushort masterNodeId, string origin)
        {
            if (!NetworkUtil.IsSynchronizationReady())
                return;

            var shouldScheduleFlush = false;

            lock (EventGate)
            {
                if (masterNodeId == 0)
                {
                    _pendingFullRuntimeSync = true;
                }
                else
                {
                    PendingRuntimeMasters.Add(masterNodeId);
                }

                if (!string.IsNullOrEmpty(origin))
                    _pendingRuntimeOrigin = origin;

                if (!_flushScheduled)
                {
                    _flushScheduled = true;
                    shouldScheduleFlush = true;
                }
            }

            Log.Debug(
                LogCategory.Network,
                CsmBridge.IsServerInstance() ? LogRole.Host : LogRole.Client,
                "[TimedTrafficLights] Local runtime event queued | master={0} full={1} origin={2}.",
                masterNodeId,
                masterNodeId == 0,
                origin ?? "unknown");

            if (shouldScheduleFlush)
                NetworkUtil.RunOnSimulation(FlushPendingLocalChanges);
        }

        private static void FlushPendingLocalChanges()
        {
            HashSet<ushort> pendingDefinitionMasters;
            HashSet<ushort> pendingRuntimeMasters;
            bool fullDefinitionSync;
            bool fullRuntimeSync;
            string definitionOrigin;
            string runtimeOrigin;

            lock (EventGate)
            {
                pendingDefinitionMasters = new HashSet<ushort>(PendingDefinitionMasters);
                pendingRuntimeMasters = new HashSet<ushort>(PendingRuntimeMasters);
                fullDefinitionSync = _pendingFullDefinitionSync;
                fullRuntimeSync = _pendingFullRuntimeSync;
                definitionOrigin = string.IsNullOrEmpty(_pendingDefinitionOrigin) ? "local_event" : _pendingDefinitionOrigin;
                runtimeOrigin = string.IsNullOrEmpty(_pendingRuntimeOrigin) ? "local_runtime_event" : _pendingRuntimeOrigin;

                PendingDefinitionMasters.Clear();
                PendingRuntimeMasters.Clear();
                _pendingFullDefinitionSync = false;
                _pendingFullRuntimeSync = false;
                _pendingDefinitionOrigin = null;
                _pendingRuntimeOrigin = null;
                _flushScheduled = false;
            }

            if (TimedTrafficLightsTmpeAdapter.IsLocalApplyActive)
                return;

            if (!fullDefinitionSync && !fullRuntimeSync && pendingDefinitionMasters.Count == 0 && pendingRuntimeMasters.Count == 0)
                return;

            if (!NetworkUtil.IsSynchronizationReady())
                return;

            lock (DefinitionGate)
            {
                if (TimedTrafficLightsTmpeAdapter.IsLocalApplyActive)
                    return;

                if (fullDefinitionSync)
                {
                    PublishDefinitionDeltaFromCurrentState(definitionOrigin + ":full");
                }
                else if (pendingDefinitionMasters.Count > 0)
                {
                    var orderedMasters = pendingDefinitionMasters.OrderBy(id => id).ToList();
                    for (var i = 0; i < orderedMasters.Count; i++)
                        PublishDefinitionForMaster(orderedMasters[i], definitionOrigin);
                }

                if (fullRuntimeSync)
                {
                    PublishRuntimeDeltaForKnownMasters(runtimeOrigin + ":full");
                }
                else if (pendingRuntimeMasters.Count > 0)
                {
                    var orderedMasters = pendingRuntimeMasters.OrderBy(id => id).ToList();
                    for (var i = 0; i < orderedMasters.Count; i++)
                        PublishRuntimeForMaster(orderedMasters[i], runtimeOrigin);
                }
            }
        }

        private static void PublishDefinitionDeltaFromCurrentState(string origin)
        {
            var currentDefinitions = CaptureAllDefinitions();

            if (CsmBridge.IsServerInstance())
                PublishHostDefinitionDelta(currentDefinitions, origin);
            else
                PublishClientDefinitionDelta(currentDefinitions, origin);
        }

        private static Dictionary<ushort, TimedTrafficLightsDefinitionState> CaptureAllDefinitions()
        {
            Dictionary<ushort, TimedTrafficLightsDefinitionState> definitions;
            TimedTrafficLightsTmpeAdapter.TryCaptureAllDefinitions(out definitions);
            return definitions ?? new Dictionary<ushort, TimedTrafficLightsDefinitionState>();
        }

        private static void PublishDefinitionForMaster(ushort masterNodeId, string origin)
        {
            if (masterNodeId == 0)
            {
                PublishDefinitionDeltaFromCurrentState(origin + ":master_unknown");
                return;
            }

            if (CsmBridge.IsServerInstance())
                PublishHostDefinitionForMaster(masterNodeId, origin);
            else
                PublishClientDefinitionForMaster(masterNodeId, origin);
        }

        private static void PublishHostDefinitionForMaster(ushort masterNodeId, string origin)
        {
            TimedTrafficLightsDefinitionState definition;
            if (TimedTrafficLightsTmpeAdapter.TryCaptureDefinitionForMaster(masterNodeId, out definition) && definition != null)
            {
                var resolvedMasterNodeId = definition.MasterNodeId != 0 ? definition.MasterNodeId : masterNodeId;
                if (resolvedMasterNodeId != masterNodeId)
                {
                    PublishDefinitionDeltaFromCurrentState(origin + ":master_redirect");
                    return;
                }

                var applied = new TimedTrafficLightsDefinitionAppliedCommand
                {
                    MasterNodeId = resolvedMasterNodeId,
                    Removed = false,
                    Definition = TimedTrafficLightsStateCache.CloneDefinition(definition)
                };

                if (!TimedTrafficLightsStateCache.Store(applied))
                    return;

                Log.Debug(
                    LogCategory.Synchronization,
                    LogRole.Host,
                    "[TimedTrafficLights] Host definition broadcast | master={0} origin={1}.",
                    resolvedMasterNodeId,
                    origin ?? "unspecified");

                Dispatch(TimedTrafficLightsStateCache.CloneApplied(applied));
                PublishHostRuntimeForMaster(resolvedMasterNodeId, origin + ":definition");
                return;
            }

            var removed = new TimedTrafficLightsDefinitionAppliedCommand
            {
                MasterNodeId = masterNodeId,
                Removed = true,
                Definition = null
            };

            if (!TimedTrafficLightsStateCache.Store(removed))
                return;

            TimedTrafficLightsRuntimeCache.RemoveBroadcast(masterNodeId);
            TimedTrafficLightsRuntimeCache.RemoveReceived(masterNodeId);

            Log.Debug(
                LogCategory.Synchronization,
                LogRole.Host,
                "[TimedTrafficLights] Host definition removed | master={0} origin={1}.",
                masterNodeId,
                origin ?? "unspecified");

            Dispatch(removed);
        }

        private static void PublishClientDefinitionForMaster(ushort masterNodeId, string origin)
        {
            TimedTrafficLightsDefinitionState definition;
            if (TimedTrafficLightsTmpeAdapter.TryCaptureDefinitionForMaster(masterNodeId, out definition) && definition != null)
            {
                var resolvedMasterNodeId = definition.MasterNodeId != 0 ? definition.MasterNodeId : masterNodeId;
                if (resolvedMasterNodeId != masterNodeId)
                {
                    PublishDefinitionDeltaFromCurrentState(origin + ":master_redirect");
                    return;
                }

                var hash = TimedTrafficLightsStateCache.ComputeHash(definition);
                ulong cachedHash;
                if (TimedTrafficLightsStateCache.TryGetHash(resolvedMasterNodeId, out cachedHash) && cachedHash == hash)
                    return;

                Dispatch(new TimedTrafficLightsDefinitionUpdateRequest
                {
                    MasterNodeId = resolvedMasterNodeId,
                    Removed = false,
                    Definition = TimedTrafficLightsStateCache.CloneDefinition(definition)
                });

                Log.Debug(
                    LogCategory.Network,
                    LogRole.Client,
                    "[TimedTrafficLights] Client definition request sent | master={0} origin={1}.",
                    resolvedMasterNodeId,
                    origin ?? "unspecified");
                return;
            }

            if (!TimedTrafficLightsStateCache.TryGetHash(masterNodeId, out _))
                return;

            Dispatch(new TimedTrafficLightsDefinitionUpdateRequest
            {
                MasterNodeId = masterNodeId,
                Removed = true,
                Definition = null
            });

            Log.Debug(
                LogCategory.Network,
                LogRole.Client,
                "[TimedTrafficLights] Client definition removal request sent | master={0} origin={1}.",
                masterNodeId,
                origin ?? "unspecified");
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
                    "[TimedTrafficLights] Host definition broadcast | master={0} origin={1}.",
                    master,
                    origin ?? "unspecified");

                Dispatch(TimedTrafficLightsStateCache.CloneApplied(applied));
                PublishHostRuntimeForMaster(master, origin + ":definition_delta");
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

            var currentMasters = currentDefinitions.Keys.OrderBy(master => master).ToList();
            for (var i = 0; i < currentMasters.Count; i++)
            {
                var master = currentMasters[i];
                TimedTrafficLightsDefinitionState definition;
                if (!currentDefinitions.TryGetValue(master, out definition) || definition == null)
                    continue;

                var hash = TimedTrafficLightsStateCache.ComputeHash(definition);
                ulong cachedHash;
                if (cachedHashes.TryGetValue(master, out cachedHash) && cachedHash == hash)
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
                    "[TimedTrafficLights] Client definition request sent | master={0} origin={1}.",
                    master,
                    origin ?? "unspecified");
            }

            var removedMasters = cachedHashes.Keys
                .Where(master => !currentDefinitions.ContainsKey(master))
                .OrderBy(master => master)
                .ToList();

            for (var i = 0; i < removedMasters.Count; i++)
            {
                var master = removedMasters[i];
                Dispatch(new TimedTrafficLightsDefinitionUpdateRequest
                {
                    MasterNodeId = master,
                    Removed = true,
                    Definition = null
                });

                Log.Debug(
                    LogCategory.Network,
                    LogRole.Client,
                    "[TimedTrafficLights] Client definition removal request sent | master={0} origin={1}.",
                    master,
                    origin ?? "unspecified");
            }
        }

        private static void PublishRuntimeDeltaForKnownMasters(string origin)
        {
            var masters = TimedTrafficLightsStateCache.GetKnownMasterNodeIds();
            if (masters.Count == 0)
            {
                var definitions = CaptureAllDefinitions();
                if (definitions.Count > 0)
                    masters = definitions.Keys.OrderBy(key => key).ToList();
            }

            for (var i = 0; i < masters.Count; i++)
                PublishRuntimeForMaster(masters[i], origin);
        }

        private static void PublishRuntimeForMaster(ushort masterNodeId, string origin)
        {
            if (masterNodeId == 0)
                return;

            if (CsmBridge.IsServerInstance())
                PublishHostRuntimeForMaster(masterNodeId, origin);
            else
                PublishClientRuntimeForMaster(masterNodeId, origin);
        }

        private static void PublishHostRuntimeForMaster(ushort masterNodeId, string origin)
        {
            TimedTrafficLightsRuntimeState runtime;
            if (!TimedTrafficLightsTmpeAdapter.TryReadRuntime(masterNodeId, out runtime) || runtime == null)
            {
                TimedTrafficLightsRuntimeCache.RemoveBroadcast(masterNodeId);
                return;
            }

            if (runtime.MasterNodeId == 0)
                runtime.MasterNodeId = masterNodeId;

            TimedTrafficLightsRuntimeState lastBroadcast;
            uint lastSentAtFrame;
            var hasLastBroadcast = TimedTrafficLightsRuntimeCache.TryGetBroadcast(runtime.MasterNodeId, out lastBroadcast, out lastSentAtFrame);
            if (hasLastBroadcast && RuntimeEquivalent(lastBroadcast, runtime))
                return;

            runtime.Epoch = GetCurrentFrame();
            Dispatch(new TimedTrafficLightsRuntimeAppliedCommand
            {
                Runtime = TimedTrafficLightsRuntimeCache.CloneRuntime(runtime)
            });

            TimedTrafficLightsRuntimeCache.StoreBroadcast(runtime, runtime.Epoch);

            Log.Debug(
                LogCategory.Synchronization,
                LogRole.Host,
                "[TimedTrafficLights] Host runtime broadcast | master={0} running={1} step={2} origin={3}.",
                runtime.MasterNodeId,
                runtime.IsRunning,
                runtime.CurrentStep,
                origin ?? "unspecified");
        }

        private static void PublishClientRuntimeForMaster(ushort masterNodeId, string origin)
        {
            TimedTrafficLightsRuntimeState runtime;
            if (!TimedTrafficLightsTmpeAdapter.TryReadRuntime(masterNodeId, out runtime) || runtime == null)
                return;

            if (runtime.MasterNodeId == 0)
                runtime.MasterNodeId = masterNodeId;

            TimedTrafficLightsRuntimeState received;
            if (TimedTrafficLightsRuntimeCache.TryGetReceived(runtime.MasterNodeId, out received) && RuntimeEquivalent(received, runtime))
                return;

            Dispatch(new TimedTrafficLightsRuntimeUpdateRequest
            {
                Runtime = TimedTrafficLightsRuntimeCache.CloneRuntime(runtime)
            });

            Log.Debug(
                LogCategory.Network,
                LogRole.Client,
                "[TimedTrafficLights] Client runtime request sent | master={0} running={1} step={2} origin={3}.",
                runtime.MasterNodeId,
                runtime.IsRunning,
                runtime.CurrentStep,
                origin ?? "unspecified");
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

        private static void AcceptHostState(ushort masterNodeId)
        {
            lock (ClientGate)
            {
                ClientResyncRequestedAtFrame.Remove(masterNodeId);
            }
        }

        private static void AcceptHostRuntimeState(ushort masterNodeId)
        {
            lock (ClientGate)
            {
                ClientResyncRequestedAtFrame.Remove(masterNodeId);
            }
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
                "[TimedTrafficLights] Resync request sent | master={0} reason={1}.",
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
                if (TimedTrafficLightsTmpeAdapter.TryCaptureDefinitionForMaster(masterNodeId, out definition) && definition != null)
                {
                    hasDefinition = true;
                    TimedTrafficLightsStateCache.Store(new TimedTrafficLightsDefinitionAppliedCommand
                    {
                        MasterNodeId = definition.MasterNodeId != 0 ? definition.MasterNodeId : masterNodeId,
                        Removed = false,
                        Definition = TimedTrafficLightsStateCache.CloneDefinition(definition)
                    });
                }
            }

            if (hasDefinition && definition != null)
            {
                var resolvedMaster = definition.MasterNodeId != 0 ? definition.MasterNodeId : masterNodeId;
                CsmBridge.SendToClient(clientId, new TimedTrafficLightsDefinitionAppliedCommand
                {
                    MasterNodeId = resolvedMaster,
                    Removed = false,
                    Definition = TimedTrafficLightsStateCache.CloneDefinition(definition)
                });

                TimedTrafficLightsRuntimeState runtime;
                if (TimedTrafficLightsTmpeAdapter.TryReadRuntime(resolvedMaster, out runtime) && runtime != null)
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

        private static void SyncHostCachesWithoutBroadcast(Dictionary<ushort, TimedTrafficLightsDefinitionState> currentDefinitions)
        {
            currentDefinitions = currentDefinitions ?? new Dictionary<ushort, TimedTrafficLightsDefinitionState>();

            var currentMasters = new HashSet<ushort>(currentDefinitions.Keys);
            var cachedMasters = TimedTrafficLightsStateCache.GetKnownMasterNodeIds();

            var orderedCurrent = currentDefinitions.Keys.OrderBy(master => master).ToList();
            for (var i = 0; i < orderedCurrent.Count; i++)
            {
                var master = orderedCurrent[i];
                TimedTrafficLightsDefinitionState definition;
                if (!currentDefinitions.TryGetValue(master, out definition) || definition == null)
                    continue;

                TimedTrafficLightsStateCache.Store(new TimedTrafficLightsDefinitionAppliedCommand
                {
                    MasterNodeId = master,
                    Removed = false,
                    Definition = TimedTrafficLightsStateCache.CloneDefinition(definition)
                });
            }

            for (var i = 0; i < cachedMasters.Count; i++)
            {
                var master = cachedMasters[i];
                if (currentMasters.Contains(master))
                    continue;

                TimedTrafficLightsStateCache.Store(new TimedTrafficLightsDefinitionAppliedCommand
                {
                    MasterNodeId = master,
                    Removed = true,
                    Definition = null
                });

                TimedTrafficLightsRuntimeCache.RemoveBroadcast(master);
                TimedTrafficLightsRuntimeCache.RemoveReceived(master);
            }
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
