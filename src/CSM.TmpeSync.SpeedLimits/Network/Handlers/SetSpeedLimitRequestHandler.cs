using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.Requests;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Network.Contracts.System;
using CSM.TmpeSync.SpeedLimits.Tmpe;
using CSM.TmpeSync.SpeedLimits.Util;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;
using CSM.TmpeSync.CsmBridge;

namespace CSM.TmpeSync.Network.Handlers
{
    public class SetSpeedLimitRequestHandler : CommandHandler<SetSpeedLimitRequest>
    {
        protected override void Handle(SetSpeedLimitRequest cmd)
        {
            var senderId = CsmBridge.GetSenderId(cmd);
            var segmentId = cmd.SegmentId;
            var laneIndex = cmd.LaneIndex;

            if ((segmentId == 0 || laneIndex < 0) && NetworkUtil.TryGetLaneLocation(cmd.LaneId, out var locatedSegment, out var locatedIndex))
            {
                segmentId = locatedSegment;
                laneIndex = locatedIndex;
            }

            var requestedValue = cmd.Speed ?? SpeedLimitCodec.Default();
            var requestedKmh = SpeedLimitCodec.DecodeToKmh(requestedValue);
            var requestedDescription = SpeedLimitCodec.Describe(requestedValue);

            Log.Info(
                LogCategory.Network,
                "SetSpeedLimitRequest received | laneId={0} segmentId={1} laneIndex={2} value={3} speedKmh={4} senderId={5} role={6}",
                cmd.LaneId,
                segmentId,
                laneIndex,
                requestedDescription,
                requestedKmh,
                senderId,
                CsmBridge.DescribeCurrentRole());

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, "Ignoring SetSpeedLimitRequest | reason=not_server_instance");
                return;
            }

            if (!NetworkUtil.TryResolveLane(cmd.LaneId, segmentId, laneIndex, out var resolvedLaneId))
            {
                Log.Warn(LogCategory.Network, "Rejecting SetSpeedLimitRequest | laneId={0} segmentId={1} laneIndex={2} reason=lane_missing", cmd.LaneId, segmentId, laneIndex);
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.LaneId, EntityType = 1 });
                return;
            }

            Log.Debug(
                LogCategory.Synchronization,
                "Queueing speed limit apply | laneId={0} value={1} speedKmh={2}",
                cmd.LaneId,
                requestedDescription,
                requestedKmh);
            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.TryResolveLane(resolvedLaneId, segmentId, laneIndex, out var simLaneId))
                {
                    Log.Warn(LogCategory.Synchronization, "Simulation step aborted | laneId={0} segmentId={1} laneIndex={2} reason=lane_disappeared_before_apply", resolvedLaneId, segmentId, laneIndex);
                    return;
                }

                using (EntityLocks.AcquireLane(simLaneId))
                {
                    if (!NetworkUtil.TryResolveLane(simLaneId, segmentId, laneIndex, out var lockedLaneId))
                    {
                        Log.Warn(LogCategory.Synchronization, "Skipping speed limit apply | laneId={0} segmentId={1} laneIndex={2} reason=lane_disappeared_while_locked", simLaneId, segmentId, laneIndex);
                        return;
                    }

                    if (PendingMap.ApplySpeedLimit(lockedLaneId, requestedKmh, ignoreScope: false))
                    {
                        SpeedLimitValue resultingValue = requestedValue;
                        float? resultingDefault = null;
                        var resultingSpeedKmh = SpeedLimitCodec.DecodeToKmh(resultingValue);

                        if (PendingMap.TryGetSpeedLimit(lockedLaneId, out var appliedSpeed, out var appliedDefault, out var hasOverride, out var pending))
                        {
                            resultingValue = SpeedLimitCodec.Encode(appliedSpeed, appliedDefault, hasOverride, pending);
                            resultingDefault = appliedDefault;
                            resultingSpeedKmh = appliedSpeed;
                        }
                        else
                        {
                            var assumeOverride = !SpeedLimitCodec.IsDefault(resultingValue);
                            resultingValue = SpeedLimitCodec.Encode(resultingSpeedKmh, null, assumeOverride, pending: assumeOverride);
                        }
                        if (!NetworkUtil.TryGetLaneLocation(lockedLaneId, out var currentSegment, out var currentLaneIndex))
                        {
                            currentSegment = segmentId;
                            currentLaneIndex = laneIndex;
                        }

                        var mappingVersion = LaneMappingStore.Version;
                        Log.Info(
                            LogCategory.Synchronization,
                            "Applied speed limit | laneId={0} segmentId={1} laneIndex={2} value={3} speedKmh={4} action=broadcast",
                            lockedLaneId,
                            currentSegment,
                            currentLaneIndex,
                            SpeedLimitCodec.Describe(resultingValue),
                            SpeedLimitCodec.DecodeToKmh(resultingValue));

                        SpeedLimitDiagnostics.LogOutgoingSpeedLimit(
                            lockedLaneId,
                            SpeedLimitCodec.DecodeToKmh(resultingValue),
                            resultingValue,
                            resultingDefault,
                            "request_handler");

                        CsmBridge.SendToAll(new SpeedLimitApplied
                        {
                            LaneId = lockedLaneId,
                            Speed = resultingValue,
                            SegmentId = currentSegment,
                            LaneIndex = currentLaneIndex,
                            MappingVersion = mappingVersion
                        });
                    }
                    else
                    {
                        Log.Error(
                            LogCategory.Synchronization,
                            "Failed to apply speed limit | laneId={0} segmentId={1} laneIndex={2} value={3} speedKmh={4} notifyClient={5}",
                            lockedLaneId,
                            segmentId,
                            laneIndex,
                            requestedDescription,
                            requestedKmh,
                            senderId);
                        CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = lockedLaneId, EntityType = 1 });
                    }
                }
            });
        }
    }
}
