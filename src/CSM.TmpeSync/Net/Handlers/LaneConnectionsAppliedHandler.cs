using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class LaneConnectionsAppliedHandler : CommandHandler<LaneConnectionsApplied>
    {
        protected override void Handle(LaneConnectionsApplied cmd)
        {
            Log.Info(LogCategory.Synchronization, "LaneConnectionsApplied received | laneId={0} targets=[{1}]", cmd.SourceLaneId, FormatLaneIds(cmd.TargetLaneIds));

            if (NetUtil.LaneExists(cmd.SourceLaneId))
            {
                using (CsmCompat.StartIgnore())
                {
                    if (Tmpe.TmpeAdapter.ApplyLaneConnections(cmd.SourceLaneId, cmd.TargetLaneIds))
                        Log.Info(LogCategory.Synchronization, "Applied remote lane connections | laneId={0} targets=[{1}]", cmd.SourceLaneId, FormatLaneIds(cmd.TargetLaneIds));
                    else
                        Log.Error(LogCategory.Synchronization, "Failed to apply remote lane connections | laneId={0}", cmd.SourceLaneId);
                }
            }
            else
            {
                Log.Warn(LogCategory.Synchronization, "Lane missing for lane connection apply | laneId={0} action=queue_deferred", cmd.SourceLaneId);
                DeferredApply.Enqueue(new LaneConnectionsDeferredOp(cmd));
            }
        }

        private static string FormatLaneIds(uint[] laneIds)
        {
            return laneIds == null || laneIds.Length == 0 ? string.Empty : string.Join(", ", laneIds);
        }
    }
}
