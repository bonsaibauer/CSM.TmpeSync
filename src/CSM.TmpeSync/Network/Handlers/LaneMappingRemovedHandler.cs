using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Mapping;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    public class LaneMappingRemovedHandler : CommandHandler<LaneMappingRemoved>
    {
        protected override void Handle(LaneMappingRemoved command)
        {
            if (command == null)
                return;

            if (!command.LaneGuid.IsValid)
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    "Lane mapping removal ignored due to invalid GUID | segment={0} laneIndex={1} version={2}",
                    command.SegmentId,
                    command.LaneIndex,
                    command.Version);

                LaneMappingStore.ApplyRemoteRemoval(command.Version, command.LaneGuid, command.SegmentId, command.LaneIndex, out _);
                return;
            }

            if (!LaneMappingStore.ApplyRemoteRemoval(command.Version, command.LaneGuid, command.SegmentId, command.LaneIndex, out var removed))
            {
                Log.Debug(
                    LogCategory.Synchronization,
                    "Ignoring stale lane mapping removal | segment={0} laneIndex={1} guid={2} version={3}",
                    command.SegmentId,
                    command.LaneIndex,
                    command.LaneGuid,
                    command.Version);
                return;
            }

            Log.Debug(
                LogCategory.Synchronization,
                "Lane mapping removed | segment={0} laneIndex={1} guid={2} version={3}",
                command.SegmentId,
                command.LaneIndex,
                command.LaneGuid,
                command.Version);

            if (removed?.LaneGuid.IsValid == true)
            {
                PendingMap.RemoveLaneAssignment(removed.LaneGuid);
                LaneGuidRegistry.InvalidateGuid(removed.LaneGuid);
            }
        }
    }
}
