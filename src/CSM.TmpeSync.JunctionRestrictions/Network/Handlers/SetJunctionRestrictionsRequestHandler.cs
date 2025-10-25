using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.Requests;
using CSM.TmpeSync.Network.Contracts.System;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.JunctionRestrictions.Util;
using CSM.TmpeSync.JunctionRestrictions.Bridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    public class SetJunctionRestrictionsRequestHandler : CommandHandler<SetJunctionRestrictionsRequest>
    {
        protected override void Handle(SetJunctionRestrictionsRequest cmd)
        {
            var senderId = CsmBridge.GetSenderId(cmd);
            var state = cmd.State ?? new JunctionRestrictionsState();

            Log.Info(
                LogCategory.Network,
                "SetJunctionRestrictionsRequest received | nodeId={0} state={1} senderId={2} role={3}",
                cmd.NodeId,
                state,
                senderId,
                CsmBridge.DescribeCurrentRole());

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, "SetJunctionRestrictionsRequest ignored | reason=not_server_instance");
                return;
            }

            if (!NetworkUtil.NodeExists(cmd.NodeId))
            {
                Log.Warn(
                    LogCategory.Network,
                    "SetJunctionRestrictionsRequest rejected | nodeId={0} reason=node_missing",
                    cmd.NodeId);
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.NodeId, EntityType = 3 });
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.NodeExists(cmd.NodeId))
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        "Junction restrictions apply aborted | nodeId={0} reason=node_missing_before_apply",
                        cmd.NodeId);
                    return;
                }

                using (EntityLocks.AcquireNode(cmd.NodeId))
                {
                    if (!NetworkUtil.NodeExists(cmd.NodeId))
                    {
                        Log.Warn(
                            LogCategory.Synchronization,
                            "Junction restrictions apply skipped | nodeId={0} reason=node_missing_while_locked",
                            cmd.NodeId);
                        return;
                    }

                    if (!TmpeBridge.ApplyJunctionRestrictions(cmd.NodeId, state))
                    {
                        Log.Error(
                            LogCategory.Synchronization,
                            "Junction restrictions apply failed | nodeId={0} senderId={1}",
                            cmd.NodeId,
                            senderId);
                        CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = cmd.NodeId, EntityType = 3 });
                        return;
                    }

                    var resultingState = state?.Clone();
                    if (TmpeBridge.TryGetJunctionRestrictions(cmd.NodeId, out var appliedState) && appliedState != null)
                        resultingState = appliedState.Clone();

                    resultingState = JunctionRestrictionsDiagnostics.LogOutgoingJunctionRestrictions(
                        cmd.NodeId,
                        resultingState,
                        "request_handler");

                    Log.Info(
                        LogCategory.Synchronization,
                        "Junction restrictions applied | nodeId={0} action=broadcast",
                        cmd.NodeId);
                    CsmBridge.SendToAll(new JunctionRestrictionsApplied
                    {
                        NodeId = cmd.NodeId,
                        State = resultingState?.Clone() ?? new JunctionRestrictionsState()
                    });
                }
            });
        }
    }
}
