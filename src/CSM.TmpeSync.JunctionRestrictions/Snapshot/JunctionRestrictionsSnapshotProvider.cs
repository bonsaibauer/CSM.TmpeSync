using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Snapshot
{
    public class JunctionRestrictionsSnapshotProvider : ISnapshotProvider
    {
        public void Export()
        {
            Log.Info(LogCategory.Snapshot, "Exporting TM:PE junction restrictions snapshot");
            NetworkUtil.ForEachNode(nodeId =>
            {
                if (!TmpeBridgeAdapter.TryGetJunctionRestrictions(nodeId, out var state))
                    return;

                if (state == null || state.IsDefault())
                    return;

                SnapshotDispatcher.Dispatch(new JunctionRestrictionsApplied
                {
                    NodeId = nodeId,
                    State = state
                });
            });
        }

        public void Import()
        {
        }
    }
}
