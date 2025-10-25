namespace CSM.TmpeSync.Snapshot
{
    public interface ISnapshotProvider
    {
        void Export();
        void Import();
    }
}

