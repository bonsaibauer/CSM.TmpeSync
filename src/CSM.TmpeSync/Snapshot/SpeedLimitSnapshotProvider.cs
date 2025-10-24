using System.Collections.Generic;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.Snapshot
{
    public class SpeedLimitSnapshotProvider : ISnapshotProvider
    {
        private const int BatchSize = 128;

        public void Export()
        {
            Log.Info(LogCategory.Snapshot, "Exporting TM:PE speed limits snapshot");

            var buffer = new List<SpeedLimitBatchApplied.Entry>(BatchSize);
            var exported = 0;

            NetUtil.ForEachLane(laneId =>
            {
                if (!Tmpe.TmpeAdapter.TryGetSpeedLimit(laneId, out var kmh, out var defaultKmh, out var hasOverride, out var pending))
                    return;

                if (!NetUtil.TryGetLaneLocation(laneId, out var segmentId, out var laneIndex))
                    return;

                var encoded = SpeedLimitCodec.Encode(kmh, defaultKmh, hasOverride, pending);
                Log.Debug(
                    LogCategory.Snapshot,
                    "Speed limit snapshot entry | laneId={0} value={1}",
                    laneId,
                    SpeedLimitCodec.Describe(encoded));
                var mappingVersion = LaneMappingStore.Version;
                buffer.Add(new SpeedLimitBatchApplied.Entry
                {
                    LaneId = laneId,
                    Speed = encoded,
                    SegmentId = segmentId,
                    LaneIndex = laneIndex,
                    MappingVersion = mappingVersion
                });

                if (buffer.Count >= BatchSize)
                    exported += Flush(buffer);
            });

            exported += Flush(buffer);
            Log.Info(LogCategory.Snapshot, "Exported TM:PE speed limits snapshot | totalEntries={0}", exported);
        }

        public void Import()
        {
        }

        private static int Flush(List<SpeedLimitBatchApplied.Entry> buffer)
        {
            if (buffer.Count == 0)
                return 0;

            // Send one command per batch to avoid flooding the network channel.
            long mappingVersion = 0;
            foreach (var entry in buffer)
                if (entry.MappingVersion > mappingVersion)
                    mappingVersion = entry.MappingVersion;

            var payload = new SpeedLimitBatchApplied
            {
                Items = new List<SpeedLimitBatchApplied.Entry>(buffer),
                MappingVersion = mappingVersion
            };

            SnapshotDispatcher.Dispatch(payload);
            Log.Debug(LogCategory.Snapshot, "Speed limit snapshot batch sent | count={0}", payload.Items.Count);
            buffer.Clear();
            return payload.Items.Count;
        }
    }
}
