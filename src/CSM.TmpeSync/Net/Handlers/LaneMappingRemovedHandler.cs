using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Mapping;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class LaneMappingRemovedHandler : CommandHandler<LaneMappingRemoved>
    {
        protected override void Handle(LaneMappingRemoved command)
        {
            if (command == null)
                return;

            if (!LaneMappingStore.ApplyRemoteRemoval(command.Version, command.SegmentId, command.LaneIndex))
            {
                Log.Debug(
                    LogCategory.Synchronization,
                    "Ignoring stale lane mapping removal | segment={0} laneIndex={1} version={2}",
                    command.SegmentId,
                    command.LaneIndex,
                    command.Version);
                return;
            }

            Log.Debug(
                LogCategory.Synchronization,
                "Lane mapping removed | segment={0} laneIndex={1} version={2}",
                command.SegmentId,
                command.LaneIndex,
                command.Version);
        }
    }
}
