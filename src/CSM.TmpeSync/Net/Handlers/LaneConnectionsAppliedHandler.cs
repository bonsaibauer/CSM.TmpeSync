using System.Globalization;
using System.Linq;
using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class LaneConnectionsAppliedHandler : CommandHandler<LaneConnectionsApplied>
    {
        protected override void Handle(LaneConnectionsApplied cmd)
        {
            Log.Info("Received LaneConnectionsApplied lane={0} targets=[{1}]", cmd.SourceLaneId, FormatLaneIds(cmd.TargetLaneIds));

            if (NetUtil.LaneExists(cmd.SourceLaneId))
            {
                using (CsmCompat.StartIgnore())
                {
                    if (Tmpe.TmpeAdapter.ApplyLaneConnections(cmd.SourceLaneId, cmd.TargetLaneIds ?? new uint[0]))
                        Log.Info("Applied remote lane connections lane={0} -> [{1}]", cmd.SourceLaneId, FormatLaneIds(cmd.TargetLaneIds));
                    else
                        Log.Error("Failed to apply remote lane connections lane={0}", cmd.SourceLaneId);
                }
            }
            else
            {
                Log.Warn("Lane {0} missing – queueing deferred lane connections apply.", cmd.SourceLaneId);
                DeferredApply.Enqueue(new LaneConnectionsDeferredOp(cmd));
            }
        }

        private static string FormatLaneIds(uint[] laneIds)
        {
            if (laneIds == null || laneIds.Length == 0)
                return string.Empty;

            return string.Join(",", laneIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray());
        }
    }
}
