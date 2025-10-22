using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Snapshot
{
    public class JunctionRestrictionsSnapshotProvider : ISnapshotProvider
    {
        public void Export()
        {
            Log.Info(LogCategory.Snapshot, "Exporting TM:PE junction restrictions snapshot");
            NetUtil.ForEachNode(nodeId =>
            {
                if (!TmpeAdapter.TryGetJunctionRestrictions(nodeId, out var state))
                    return;

                if (state == null || state.IsDefault())
                    return;

                SnapshotDispatcher.Dispatch(new JunctionRestrictionsApplied { NodeId = nodeId, State = state });
            });
        }

        public void Import()
        {
        }
    }
}
