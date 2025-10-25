using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    public class PrioritySignAppliedHandler : CommandHandler<PrioritySignApplied>
    {
        protected override void Handle(PrioritySignApplied cmd)
        {
            ProcessEntry(cmd.NodeId, cmd.SegmentId, cmd.SignType, "single_command");
        }

        internal static void ProcessEntry(ushort nodeId, ushort segmentId, PrioritySignType signType, string origin)
        {
            Log.Info("Received PrioritySignApplied node={0} segment={1} sign={2} origin={3}", nodeId, segmentId, signType, origin ?? "unknown");

            if (NetworkUtil.NodeExists(nodeId) && NetworkUtil.SegmentExists(segmentId))
            {
                if (TmpeBridgeAdapter.ApplyPrioritySign(nodeId, segmentId, signType))
                    Log.Info("Applied remote priority sign node={0} segment={1} -> {2}", nodeId, segmentId, signType);
                else
                    Log.Error("Failed to apply remote priority sign node={0} segment={1}", nodeId, segmentId);
            }
            else
            {
                Log.Warn("Node {0} or segment {1} missing – skipping priority sign apply (origin={2}).", nodeId, segmentId, origin ?? "unknown");
            }
        }
    }
}
