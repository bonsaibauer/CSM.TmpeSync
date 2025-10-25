using ColossalFramework;
using CSM.API.Commands;
using CSM.TmpeSync.JunctionRestrictions.Net.Contracts.Applied;
using CSM.TmpeSync.JunctionRestrictions.Net.Contracts.Requests;
using CSM.TmpeSync.Network.Contracts.System;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.JunctionRestrictions.Util;
using CSM.TmpeSync.Util;
using CSM.TmpeSync.Bridge;

namespace CSM.TmpeSync.Network.Handlers
{
    public class SetJunctionRestrictionsRequestHandler : CommandHandler<SetJunctionRestrictionsRequest>
    {
        protected override void Handle(SetJunctionRestrictionsRequest cmd)
        {
            var senderId = CsmBridge.GetSenderId(cmd);
            var state = cmd.State ?? new JunctionRestrictionsState();

            Log.Info("Received SetJunctionRestrictionsRequest node={0} state={1} from client={2} role={3}", cmd.NodeId, state, senderId, CsmBridge.DescribeCurrentRole());

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug("Ignoring SetJunctionRestrictionsRequest on non-server instance.");
                return;
            }

            if (!NetworkUtil.NodeExists(cmd.NodeId))
            {
                Log.Warn("Rejecting SetJunctionRestrictionsRequest node={0} – node missing on server.", cmd.NodeId);
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.NodeId, EntityType = 3 });
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.NodeExists(cmd.NodeId))
                {
                    Log.Warn("Simulation step aborted – node {0} vanished before junction restrictions apply.", cmd.NodeId);
                    return;
                }

                using (EntityLocks.AcquireNode(cmd.NodeId))
                {
                    if (!NetworkUtil.NodeExists(cmd.NodeId))
                    {
                        Log.Warn("Skipping junction restrictions apply – node {0} disappeared while locked.", cmd.NodeId);
                        return;
                    }

                    if (PendingMap.ApplyJunctionRestrictions(cmd.NodeId, state, ignoreScope: false))
                    {
                        var resultingState = state?.Clone();
                        if (PendingMap.TryGetJunctionRestrictions(cmd.NodeId, out var appliedState) && appliedState != null)
                            resultingState = appliedState.Clone();
                        resultingState = JunctionRestrictionsDiagnostics.LogOutgoingJunctionRestrictions(
                            cmd.NodeId,
                            resultingState,
                            "request_handler");
                        if (NetworkUtil.NodeExists(cmd.NodeId))
                        {
                            ref var node = ref NetManager.instance.m_nodes.m_buffer[cmd.NodeId];
                            for (var i = 0; i < 8; i++)
                            {
                                var segmentId = node.GetSegment(i);
                                if (segmentId != 0 && NetworkUtil.SegmentExists(segmentId))
                                    LaneMappingTracker.SyncSegment(segmentId, "junction_restrictions_request");
                            }
                        }
                        var mappingVersion = LaneMappingStore.Version;
                        Log.Info("Applied junction restrictions node={0}; broadcasting update.", cmd.NodeId);
                        CsmBridge.SendToAll(new JunctionRestrictionsApplied
                        {
                            NodeId = cmd.NodeId,
                            State = resultingState?.Clone() ?? new JunctionRestrictionsState(),
                            MappingVersion = mappingVersion
                        });
                    }
                    else
                    {
                        Log.Error("Failed to apply junction restrictions node={0}; notifying client {1}.", cmd.NodeId, senderId);
                        CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = cmd.NodeId, EntityType = 3 });
                    }
                }
            });
        }
    }
}
