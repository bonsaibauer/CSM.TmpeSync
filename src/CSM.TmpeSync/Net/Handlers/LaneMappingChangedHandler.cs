using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Mapping;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class LaneMappingChangedHandler : CommandHandler<LaneMappingChanged>
    {
        protected override void Handle(LaneMappingChanged command)
        {
            if (command == null)
                return;

            if (!LaneMappingStore.ApplyRemoteChange(command.Version, command.HostLaneId, command.SegmentId, command.LaneIndex))
            {
                Log.Debug(
                    LogCategory.Synchronization,
                    "Ignoring stale lane mapping update | segment={0} laneIndex={1} version={2}",
                    command.SegmentId,
                    command.LaneIndex,
                    command.Version);
                return;
            }

            Log.Debug(
                LogCategory.Synchronization,
                "Lane mapping changed | segment={0} laneIndex={1} hostLane={2} version={3}",
                command.SegmentId,
                command.LaneIndex,
                command.HostLaneId,
                command.Version);

            LaneMappingBatchHandler.ResolveLocalLane(command.SegmentId, command.LaneIndex, command.HostLaneId);
        }
    }
}
