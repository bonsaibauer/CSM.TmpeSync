using System.Collections.Generic;
using CSM.TmpeSync.Net.Contracts.Applied;
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
                if (!Tmpe.TmpeAdapter.TryGetSpeedKmh(laneId, out var kmh))
                    return;

                if (!NetUtil.TryGetLaneLocation(laneId, out var segmentId, out var laneIndex))
                    return;

                Log.Debug(LogCategory.Snapshot, "Speed limit snapshot entry | laneId={0} speedKmh={1}", laneId, kmh);
                buffer.Add(new SpeedLimitBatchApplied.Entry
                {
                    LaneId = laneId,
                    SpeedKmh = kmh,
                    SegmentId = segmentId,
                    LaneIndex = laneIndex
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
            var payload = new SpeedLimitBatchApplied
            {
                Items = new List<SpeedLimitBatchApplied.Entry>(buffer)
            };

            SnapshotDispatcher.Dispatch(payload);
            Log.Debug(LogCategory.Snapshot, "Speed limit snapshot batch sent | count={0}", payload.Items.Count);
            buffer.Clear();
            return payload.Items.Count;
        }
    }
}
