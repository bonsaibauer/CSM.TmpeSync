namespace CSM.TmpeSync.Snapshot
{
    public interface ISnapshotProvider
    {
        void Export(); // Host sendet Applied-Events
        void Import(); // (optional)
    }
}
