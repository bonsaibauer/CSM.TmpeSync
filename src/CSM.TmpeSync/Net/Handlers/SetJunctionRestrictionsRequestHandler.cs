using ColossalFramework;
using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.Requests;
using CSM.TmpeSync.Net.Contracts.System;
using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class SetJunctionRestrictionsRequestHandler : CommandHandler<SetJunctionRestrictionsRequest>
    {
        protected override void Handle(SetJunctionRestrictionsRequest cmd)
        {
            var senderId = CsmCompat.GetSenderId(cmd);
            var state = cmd.State ?? new JunctionRestrictionsState();

            Log.Info("Received SetJunctionRestrictionsRequest node={0} state={1} from client={2} role={3}", cmd.NodeId, state, senderId, CsmCompat.DescribeCurrentRole());

            if (!CsmCompat.IsServerInstance())
            {
                Log.Debug("Ignoring SetJunctionRestrictionsRequest on non-server instance.");
                return;
            }

            if (!NetUtil.NodeExists(cmd.NodeId))
            {
                Log.Warn("Rejecting SetJunctionRestrictionsRequest node={0} – node missing on server.", cmd.NodeId);
                CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.NodeId, EntityType = 3 });
                return;
            }

            NetUtil.RunOnSimulation(() =>
            {
                if (!NetUtil.NodeExists(cmd.NodeId))
                {
                    Log.Warn("Simulation step aborted – node {0} vanished before junction restrictions apply.", cmd.NodeId);
                    return;
                }

                using (EntityLocks.AcquireNode(cmd.NodeId))
                {
                    if (!NetUtil.NodeExists(cmd.NodeId))
                    {
                        Log.Warn("Skipping junction restrictions apply – node {0} disappeared while locked.", cmd.NodeId);
                        return;
                    }

                    if (PendingMap.ApplyJunctionRestrictions(cmd.NodeId, state, ignoreScope: false))
                    {
                        var resultingState = state?.Clone();
                        if (PendingMap.TryGetJunctionRestrictions(cmd.NodeId, out var appliedState) && appliedState != null)
                            resultingState = appliedState.Clone();
                        resultingState = TransmissionDiagnostics.LogOutgoingJunctionRestrictions(
                            cmd.NodeId,
                            resultingState,
                            "request_handler");
                        if (NetUtil.NodeExists(cmd.NodeId))
                        {
                            ref var node = ref NetManager.instance.m_nodes.m_buffer[cmd.NodeId];
                            for (var i = 0; i < 8; i++)
                            {
                                var segmentId = node.GetSegment(i);
                                if (segmentId != 0 && NetUtil.SegmentExists(segmentId))
                                    LaneMappingTracker.SyncSegment(segmentId, "junction_restrictions_request");
                            }
                        }
                        var mappingVersion = LaneMappingStore.Version;
                        Log.Info("Applied junction restrictions node={0}; broadcasting update.", cmd.NodeId);
                        CsmCompat.SendToAll(new JunctionRestrictionsApplied
                        {
                            NodeId = cmd.NodeId,
                            State = resultingState?.Clone() ?? new JunctionRestrictionsState(),
                            MappingVersion = mappingVersion
                        });
                    }
                    else
                    {
                        Log.Error("Failed to apply junction restrictions node={0}; notifying client {1}.", cmd.NodeId, senderId);
                        CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = cmd.NodeId, EntityType = 3 });
                    }
                }
            });
        }
    }
}
