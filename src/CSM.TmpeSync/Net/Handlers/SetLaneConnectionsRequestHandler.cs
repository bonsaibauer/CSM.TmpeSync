using System;
using System.Globalization;
using System.Linq;
using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.Requests;
using CSM.TmpeSync.Net.Contracts.System;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class SetLaneConnectionsRequestHandler : CommandHandler<SetLaneConnectionsRequest>
    {
        protected override void Handle(SetLaneConnectionsRequest cmd)
        {
            var senderId = CsmCompat.GetSenderId(cmd);
            var targets = (cmd.TargetLaneIds ?? new uint[0]).Where(id => id != 0).Distinct().ToArray();

            Log.Info("Received SetLaneConnectionsRequest lane={0} targets=[{1}] from client={2} role={3}", cmd.SourceLaneId, FormatLaneIds(targets), senderId, CsmCompat.DescribeCurrentRole());

            if (!CsmCompat.IsServerInstance())
            {
                Log.Debug("Ignoring SetLaneConnectionsRequest on non-server instance.");
                return;
            }

            if (!NetUtil.LaneExists(cmd.SourceLaneId))
            {
                Log.Warn("Rejecting SetLaneConnectionsRequest lane={0} – source lane missing on server.", cmd.SourceLaneId);
                CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.SourceLaneId, EntityType = 1 });
                return;
            }

            var missingTarget = targets.FirstOrDefault(id => !NetUtil.LaneExists(id));
            if (missingTarget != 0)
            {
                Log.Warn("Rejecting SetLaneConnectionsRequest lane={0} – target lane {1} missing.", cmd.SourceLaneId, missingTarget);
                CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = missingTarget, EntityType = 1 });
                return;
            }

            NetUtil.RunOnSimulation(() =>
            {
                if (!NetUtil.LaneExists(cmd.SourceLaneId))
                {
                    Log.Warn("Simulation step aborted – lane {0} vanished before lane connection apply.", cmd.SourceLaneId);
                    return;
                }

                using (EntityLocks.AcquireLane(cmd.SourceLaneId))
                {
                    if (!NetUtil.LaneExists(cmd.SourceLaneId))
                    {
                        Log.Warn("Skipping lane connection apply – lane {0} disappeared while locked.", cmd.SourceLaneId);
                        return;
                    }

                    var hadPrevious = TmpeAdapter.TryGetLaneConnections(cmd.SourceLaneId, out var previousTargets);
                    if (hadPrevious && previousTargets != null)
                        previousTargets = previousTargets.ToArray();
                    if (TmpeAdapter.ApplyLaneConnections(cmd.SourceLaneId, targets))
                    {
                        uint[] updatedTargets = null;
                        if (TmpeAdapter.TryGetLaneConnections(cmd.SourceLaneId, out var appliedTargets))
                            updatedTargets = appliedTargets?.ToArray();
                        if (updatedTargets == null)
                            updatedTargets = targets?.ToArray() ?? new uint[0];
                        DebugChangeMonitor.RecordLaneConnectionChange(cmd.SourceLaneId, hadPrevious ? previousTargets : null, updatedTargets);
                        Log.Info("Applied lane connections lane={0} -> [{1}]; broadcasting update.", cmd.SourceLaneId, FormatLaneIds(targets));
                        CsmCompat.SendToAll(new LaneConnectionsApplied { SourceLaneId = cmd.SourceLaneId, TargetLaneIds = targets });
                    }
                    else
                    {
                        Log.Error("Failed to apply lane connections lane={0}; notifying client {1}.", cmd.SourceLaneId, senderId);
                        CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = cmd.SourceLaneId, EntityType = 1 });
                    }
                }
            });
        }

        private static string FormatLaneIds(uint[] laneIds)
        {
            if (laneIds == null || laneIds.Length == 0)
                return string.Empty;

            return string.Join(",", laneIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray());
        }
    }
}
