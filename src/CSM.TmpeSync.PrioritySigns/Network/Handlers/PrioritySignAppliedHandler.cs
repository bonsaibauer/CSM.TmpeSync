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
            Log.Info(
                LogCategory.Network,
                "PrioritySignApplied received | nodeId={0} segmentId={1} sign={2} origin={3}",
                nodeId,
                segmentId,
                signType,
                origin ?? "unknown");

            if (NetworkUtil.NodeExists(nodeId) && NetworkUtil.SegmentExists(segmentId))
            {
                if (TmpeBridgeAdapter.ApplyPrioritySign(nodeId, segmentId, signType))
                {
                    Log.Info(
                        LogCategory.Synchronization,
                        "PrioritySignApplied applied | nodeId={0} segmentId={1} sign={2}",
                        nodeId,
                        segmentId,
                        signType);
                }
                else
                {
                    Log.Error(
                        LogCategory.Synchronization,
                        "PrioritySignApplied failed | nodeId={0} segmentId={1} sign={2}",
                        nodeId,
                        segmentId,
                        signType);
                }
            }
            else
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    "PrioritySignApplied skipped | nodeId={0} segmentId={1} origin={2} reason=entity_missing",
                    nodeId,
                    segmentId,
                    origin ?? "unknown");
            }
        }
    }
}
