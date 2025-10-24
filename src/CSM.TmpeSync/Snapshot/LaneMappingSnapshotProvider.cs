using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Snapshot
{
    public class LaneMappingSnapshotProvider : ISnapshotProvider
    {
        public void Export()
        {
            if (!CsmCompat.IsServerInstance())
                return;

            LaneMappingTracker.SyncAllSegments("snapshot_export", SnapshotDispatcher.CurrentTargetClientId);
        }

        public void Import()
        {
            // Mapping imports are handled by the handlers automatically.
        }
    }
}
