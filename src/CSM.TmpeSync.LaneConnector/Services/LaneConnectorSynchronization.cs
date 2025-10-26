using System;
using System.Linq;
using CSM.API.Commands;
using CSM.TmpeSync.Bridge;
using CSM.TmpeSync.LaneConnector.Services;
using CSM.TmpeSync.LaneConnector.Messages;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.LaneConnector.Services
{
    internal static class LaneConnectorSynchronization
    {
        internal static bool TryRead(uint laneId, out ushort srcSegmentId, out int srcLaneIndex, out uint[] targetLaneIds, out ushort[] targetSegmentIds, out int[] targetLaneIndexes)
        {
            targetLaneIds = new uint[0];
            targetSegmentIds = new ushort[0];
            targetLaneIndexes = new int[0];
            srcSegmentId = 0;
            srcLaneIndex = -1;

            if (!NetworkUtil.LaneExists(laneId))
                return false;

            if (!NetworkUtil.TryGetLaneLocation(laneId, out srcSegmentId, out srcLaneIndex))
                return false;

            if (!LaneConnectorTmpeAdapter.TryGetLaneConnections(laneId, out var targets) || targets == null)
                targets = new uint[0];

            targetLaneIds = targets;
            targetSegmentIds = new ushort[targets.Length];
            targetLaneIndexes = new int[targets.Length];
            for (var i = 0; i < targets.Length; i++)
            {
                if (!NetworkUtil.TryGetLaneLocation(targets[i], out var tSeg, out var tIdx))
                {
                    tSeg = 0; tIdx = -1;
                }
                targetSegmentIds[i] = tSeg;
                targetLaneIndexes[i] = tIdx;
            }

            return true;
        }

        internal static bool Apply(uint sourceLaneId, uint[] targets)
        {
            return LaneConnectorTmpeAdapter.ApplyLaneConnections(sourceLaneId, targets);
        }

        internal static void Dispatch(CommandBase command)
        {
            if (command == null) return;
            if (CsmBridge.IsServerInstance()) CsmBridge.SendToAll(command); else CsmBridge.SendToServer(command);
        }

        internal static bool IsLocalApplyActive => LaneConnectorTmpeAdapter.IsLocalApplyActive;
    }
}
