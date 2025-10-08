using System;
using System.Collections.Generic;
using System.Linq;
using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Tmpe
{
    internal static class TmpeAdapter
    {
        private static readonly bool HasRealTmpe;
        private static readonly object StateLock = new object();

        private static readonly Dictionary<uint, float> SpeedLimits = new Dictionary<uint, float>();
        private static readonly Dictionary<uint, LaneArrowFlags> LaneArrows = new Dictionary<uint, LaneArrowFlags>();
        private static readonly Dictionary<uint, VehicleRestrictionFlags> VehicleRestrictions = new Dictionary<uint, VehicleRestrictionFlags>();
        private static readonly Dictionary<uint, uint[]> LaneConnections = new Dictionary<uint, uint[]>();
        private static readonly Dictionary<ushort, JunctionRestrictionsState> JunctionRestrictions = new Dictionary<ushort, JunctionRestrictionsState>();
        private static readonly Dictionary<(ushort node, ushort segment), PrioritySignType> PrioritySigns = new Dictionary<(ushort node, ushort segment), PrioritySignType>();
        private static readonly Dictionary<ushort, ParkingRestrictionState> ParkingRestrictions = new Dictionary<ushort, ParkingRestrictionState>();
        private static readonly Dictionary<ushort, TimedTrafficLightState> TimedTrafficLights = new Dictionary<ushort, TimedTrafficLightState>();

        static TmpeAdapter()
        {
            try
            {
                HasRealTmpe = Type.GetType("TrafficManager.Manager.Impl.SpeedLimitManager, TrafficManager") != null;
                if (HasRealTmpe)
                    Log.Info("TM:PE API detected – advanced tool synchronisation ready.");
                else
                    Log.Warn("TM:PE API not detected – falling back to stubbed TM:PE state storage.");
            }
            catch (Exception ex)
            {
                Log.Warn("TM:PE detection failed: {0}", ex);
            }
        }

        internal static bool ApplySpeedLimit(uint laneId, float speedKmh)
        {
            try
            {
                // TODO: echte TM:PE-Manager-Aufrufe einhängen:
                // var mgr = TrafficManager.Manager.Impl.SpeedLimitManager.Instance;
                // return mgr.SetLaneSpeedLimit(laneId, speedKmh/3.6f);
                if (HasRealTmpe)
                    Log.Debug("[TMPE] Request set speed lane={0} -> {1} km/h", laneId, speedKmh);
                else
                    Log.Info("[TMPE] Set speed lane={0} -> {1} km/h (stub)", laneId, speedKmh);

                lock (StateLock)
                {
                    SpeedLimits[laneId] = speedKmh;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE ApplySpeedLimit failed: " + ex);
                return false;
            }
        }

        internal static bool TryGetSpeedKmh(uint laneId, out float kmh)
        {
            try
            {
                // TODO: echte TM:PE-Reads
                lock (StateLock)
                {
                    if (!SpeedLimits.TryGetValue(laneId, out kmh))
                        kmh = 50f;
                }
                if (HasRealTmpe)
                    Log.Debug("[TMPE] Query speed lane={0} -> {1} km/h", laneId, kmh);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE TryGetSpeedKmh failed: " + ex);
                kmh = 0f;
                return false;
            }
        }

        internal static bool ApplyLaneArrows(uint laneId, LaneArrowFlags arrows)
        {
            try
            {
                if (HasRealTmpe)
                    Log.Debug("[TMPE] Request lane arrows lane={0} -> {1}", laneId, arrows);
                else
                    Log.Info("[TMPE] Lane arrows lane={0} -> {1} (stub)", laneId, arrows);

                lock (StateLock)
                {
                    if (arrows == LaneArrowFlags.None)
                        LaneArrows.Remove(laneId);
                    else
                        LaneArrows[laneId] = arrows;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE ApplyLaneArrows failed: " + ex);
                return false;
            }
        }

        internal static bool TryGetLaneArrows(uint laneId, out LaneArrowFlags arrows)
        {
            try
            {
                lock (StateLock)
                {
                    if (!LaneArrows.TryGetValue(laneId, out arrows))
                        arrows = LaneArrowFlags.None;
                }

                if (HasRealTmpe)
                    Log.Debug("[TMPE] Query lane arrows lane={0} -> {1}", laneId, arrows);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE TryGetLaneArrows failed: " + ex);
                arrows = LaneArrowFlags.None;
                return false;
            }
        }

        internal static bool ApplyVehicleRestrictions(uint laneId, VehicleRestrictionFlags restrictions)
        {
            try
            {
                if (HasRealTmpe)
                    Log.Debug("[TMPE] Request vehicle restrictions lane={0} -> {1}", laneId, restrictions);
                else
                    Log.Info("[TMPE] Vehicle restrictions lane={0} -> {1} (stub)", laneId, restrictions);

                lock (StateLock)
                {
                    if (restrictions == VehicleRestrictionFlags.None)
                        VehicleRestrictions.Remove(laneId);
                    else
                        VehicleRestrictions[laneId] = restrictions;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE ApplyVehicleRestrictions failed: " + ex);
                return false;
            }
        }

        internal static bool TryGetVehicleRestrictions(uint laneId, out VehicleRestrictionFlags restrictions)
        {
            try
            {
                lock (StateLock)
                {
                    if (!VehicleRestrictions.TryGetValue(laneId, out restrictions))
                        restrictions = VehicleRestrictionFlags.None;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE TryGetVehicleRestrictions failed: " + ex);
                restrictions = VehicleRestrictionFlags.None;
                return false;
            }
        }

        internal static bool ApplyLaneConnections(uint sourceLaneId, uint[] targetLaneIds)
        {
            try
            {
                var sanitizedTargets = (targetLaneIds ?? Array.Empty<uint>())
                    .Where(id => id != 0)
                    .Distinct()
                    .ToArray();

                if (HasRealTmpe)
                    Log.Debug("[TMPE] Request lane connections lane={0} -> [{1}]", sourceLaneId, string.Join(",", sanitizedTargets));
                else
                    Log.Info("[TMPE] Lane connections lane={0} -> [{1}] (stub)", sourceLaneId, string.Join(",", sanitizedTargets));

                lock (StateLock)
                {
                    if (sanitizedTargets.Length == 0)
                        LaneConnections.Remove(sourceLaneId);
                    else
                        LaneConnections[sourceLaneId] = sanitizedTargets;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE ApplyLaneConnections failed: " + ex);
                return false;
            }
        }

        internal static bool TryGetLaneConnections(uint sourceLaneId, out uint[] targetLaneIds)
        {
            try
            {
                lock (StateLock)
                {
                    if (!LaneConnections.TryGetValue(sourceLaneId, out var stored))
                    {
                        targetLaneIds = Array.Empty<uint>();
                    }
                    else
                    {
                        targetLaneIds = stored.ToArray();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE TryGetLaneConnections failed: " + ex);
                targetLaneIds = Array.Empty<uint>();
                return false;
            }
        }

        internal static bool ApplyJunctionRestrictions(ushort nodeId, JunctionRestrictionsState state)
        {
            try
            {
                var normalized = state?.Clone() ?? new JunctionRestrictionsState();
                if (HasRealTmpe)
                    Log.Debug("[TMPE] Request junction restrictions node={0} -> {1}", nodeId, normalized);
                else
                    Log.Info("[TMPE] Junction restrictions node={0} -> {1} (stub)", nodeId, normalized);

                lock (StateLock)
                {
                    if (normalized.IsDefault())
                        JunctionRestrictions.Remove(nodeId);
                    else
                        JunctionRestrictions[nodeId] = normalized;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE ApplyJunctionRestrictions failed: " + ex);
                return false;
            }
        }

        internal static bool TryGetJunctionRestrictions(ushort nodeId, out JunctionRestrictionsState state)
        {
            try
            {
                lock (StateLock)
                {
                    if (!JunctionRestrictions.TryGetValue(nodeId, out var stored))
                        state = new JunctionRestrictionsState();
                    else
                        state = stored.Clone();
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE TryGetJunctionRestrictions failed: " + ex);
                state = new JunctionRestrictionsState();
                return false;
            }
        }

        internal static bool ApplyPrioritySign(ushort nodeId, ushort segmentId, PrioritySignType signType)
        {
            try
            {
                if (HasRealTmpe)
                    Log.Debug("[TMPE] Request priority sign node={0} segment={1} -> {2}", nodeId, segmentId, signType);
                else
                    Log.Info("[TMPE] Priority sign node={0} segment={1} -> {2} (stub)", nodeId, segmentId, signType);

                lock (StateLock)
                {
                    var key = (nodeId, segmentId);
                    if (signType == PrioritySignType.None)
                        PrioritySigns.Remove(key);
                    else
                        PrioritySigns[key] = signType;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE ApplyPrioritySign failed: " + ex);
                return false;
            }
        }

        internal static bool TryGetPrioritySign(ushort nodeId, ushort segmentId, out PrioritySignType signType)
        {
            try
            {
                lock (StateLock)
                {
                    if (!PrioritySigns.TryGetValue((nodeId, segmentId), out signType))
                        signType = PrioritySignType.None;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE TryGetPrioritySign failed: " + ex);
                signType = PrioritySignType.None;
                return false;
            }
        }

        internal static bool ApplyParkingRestriction(ushort segmentId, ParkingRestrictionState state)
        {
            try
            {
                var normalized = state ?? new ParkingRestrictionState();
                if (HasRealTmpe)
                    Log.Debug("[TMPE] Request parking restriction segment={0} -> {1}", segmentId, normalized);
                else
                    Log.Info("[TMPE] Parking restriction segment={0} -> {1} (stub)", segmentId, normalized);

                lock (StateLock)
                {
                    if (normalized.AllowParkingBothDirections)
                        ParkingRestrictions.Remove(segmentId);
                    else
                        ParkingRestrictions[segmentId] = normalized.Clone();
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE ApplyParkingRestriction failed: " + ex);
                return false;
            }
        }

        internal static bool TryGetParkingRestriction(ushort segmentId, out ParkingRestrictionState state)
        {
            try
            {
                lock (StateLock)
                {
                    if (!ParkingRestrictions.TryGetValue(segmentId, out var stored))
                        state = new ParkingRestrictionState { AllowParkingForward = true, AllowParkingBackward = true };
                    else
                        state = stored.Clone();
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE TryGetParkingRestriction failed: " + ex);
                state = new ParkingRestrictionState { AllowParkingForward = true, AllowParkingBackward = true };
                return false;
            }
        }

        internal static bool ApplyTimedTrafficLight(ushort nodeId, TimedTrafficLightState state)
        {
            try
            {
                var normalized = state?.Clone() ?? new TimedTrafficLightState();
                if (HasRealTmpe)
                    Log.Debug("[TMPE] Request timed traffic light node={0} -> {1}", nodeId, normalized);
                else
                    Log.Info("[TMPE] Timed traffic light node={0} -> {1} (stub)", nodeId, normalized);

                lock (StateLock)
                {
                    if (!normalized.Enabled)
                        TimedTrafficLights.Remove(nodeId);
                    else
                        TimedTrafficLights[nodeId] = normalized;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE ApplyTimedTrafficLight failed: " + ex);
                return false;
            }
        }

        internal static bool TryGetTimedTrafficLight(ushort nodeId, out TimedTrafficLightState state)
        {
            try
            {
                lock (StateLock)
                {
                    if (!TimedTrafficLights.TryGetValue(nodeId, out var stored))
                        state = new TimedTrafficLightState();
                    else
                        state = stored.Clone();
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE TryGetTimedTrafficLight failed: " + ex);
                state = new TimedTrafficLightState();
                return false;
            }
        }
    }
}
