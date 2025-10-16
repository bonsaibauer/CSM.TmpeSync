using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Snapshot
{
    public class VehicleRestrictionsSnapshotProvider : ISnapshotProvider
    {
        public void Export()
        {
            Log.Info("Exporting TM:PE vehicle restrictions snapshot");
            NetUtil.ForEachLane(laneId =>
            {
                if (!TmpeAdapter.TryGetVehicleRestrictions(laneId, out var restrictions))
                    return;

                if (restrictions == VehicleRestrictionFlags.None)
                    return;

                CsmCompat.SendToAll(new VehicleRestrictionsApplied { LaneId = laneId, Restrictions = restrictions });
            });
        }

        public void Import()
        {
        }
    }
}
