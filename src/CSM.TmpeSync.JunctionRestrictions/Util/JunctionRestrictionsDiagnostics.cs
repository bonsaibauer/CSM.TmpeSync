using System;
using System.Collections.Generic;
using System.Text;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Util;
using CSM.TmpeSync.JunctionRestrictions.Bridge;

namespace CSM.TmpeSync.JunctionRestrictions.Util
{
    internal static class JunctionRestrictionsDiagnostics
    {
        private static readonly object JunctionLock = new object();
        private static readonly Dictionary<ushort, JunctionRestrictionsState> LastReceivedJunctions =
            new Dictionary<ushort, JunctionRestrictionsState>();
        private static readonly Dictionary<ushort, JunctionRestrictionsState> LastSentJunctions =
            new Dictionary<ushort, JunctionRestrictionsState>();

        internal static JunctionRestrictionsState LogOutgoingJunctionRestrictions(
            ushort nodeId,
            JunctionRestrictionsState state,
            string origin)
        {
            var snapshot = Clone(state);

            if (CsmBridge.IsServerInstance())
                snapshot = PrepareOutgoingJunctionRestrictions(nodeId, snapshot);

            Log.Debug(
                LogCategory.Diagnostics,
                "Junction restrictions dispatch | origin={0} nodeId={1} state={2}",
                origin,
                nodeId,
                snapshot);

            if (snapshot.HasAnyValue())
            {
                var missing = DescribeUnsetFlags(snapshot);
                if (!string.IsNullOrEmpty(missing))
                {
                    Log.Warn(
                        LogCategory.Diagnostics,
                        "Junction restrictions dispatch missing values | origin={0} nodeId={1} missing={2}",
                        origin,
                        nodeId,
                        missing);
                }
            }

            return snapshot.Clone();
        }

        internal static void LogIncomingJunctionRestrictions(
            ushort nodeId,
            JunctionRestrictionsState state,
            string origin)
        {
            var snapshot = Clone(state);
            Log.Debug(
                LogCategory.Diagnostics,
                "Junction restrictions received | origin={0} nodeId={1} state={2}",
                origin,
                nodeId,
                snapshot);

            if (snapshot.HasAnyValue())
            {
                var missing = DescribeUnsetFlags(snapshot);
                if (!string.IsNullOrEmpty(missing))
                {
                    Log.Warn(
                        LogCategory.Diagnostics,
                        "Junction restrictions received with missing values | origin={0} nodeId={1} missing={2}",
                        origin,
                        nodeId,
                        missing);
                }
            }

            JunctionRestrictionsState previous = null;
            lock (JunctionLock)
            {
                if (LastReceivedJunctions.TryGetValue(nodeId, out var last))
                    previous = Clone(last);

                LastReceivedJunctions[nodeId] = snapshot;
            }

            if (previous == null)
                return;

            var lost = DescribeLostFlags(previous, snapshot);
            if (!string.IsNullOrEmpty(lost))
            {
                Log.Warn(
                    LogCategory.Diagnostics,
                    "Junction restrictions lost previously received flags | origin={0} nodeId={1} lost={2} previous={3}",
                    origin,
                    nodeId,
                    lost,
                    previous);
            }
        }

        private static JunctionRestrictionsState PrepareOutgoingJunctionRestrictions(
            ushort nodeId,
            JunctionRestrictionsState snapshot)
        {
            if (snapshot == null)
                snapshot = new JunctionRestrictionsState();

            if (!snapshot.HasAnyValue())
            {
                lock (JunctionLock)
                {
                    LastSentJunctions.Remove(nodeId);
                }

                return snapshot;
            }

            lock (JunctionLock)
            {
                if (LastSentJunctions.TryGetValue(nodeId, out var previous) && previous != null)
                    MergeMissingFlags(snapshot, previous);

                LastSentJunctions[nodeId] = snapshot.Clone();
            }

            return snapshot;
        }

        private static void MergeMissingFlags(JunctionRestrictionsState target, JunctionRestrictionsState previous)
        {
            if (target == null || previous == null)
                return;

            if (!target.AllowUTurns.HasValue)
                target.AllowUTurns = previous.AllowUTurns;
            if (!target.AllowLaneChangesWhenGoingStraight.HasValue)
                target.AllowLaneChangesWhenGoingStraight = previous.AllowLaneChangesWhenGoingStraight;
            if (!target.AllowEnterWhenBlocked.HasValue)
                target.AllowEnterWhenBlocked = previous.AllowEnterWhenBlocked;
            if (!target.AllowPedestrianCrossing.HasValue)
                target.AllowPedestrianCrossing = previous.AllowPedestrianCrossing;
            if (!target.AllowNearTurnOnRed.HasValue)
                target.AllowNearTurnOnRed = previous.AllowNearTurnOnRed;
            if (!target.AllowFarTurnOnRed.HasValue)
                target.AllowFarTurnOnRed = previous.AllowFarTurnOnRed;
        }

        private static string DescribeUnsetFlags(JunctionRestrictionsState state)
        {
            var builder = new StringBuilder();

            AppendMissing(builder, state.AllowUTurns, "AllowUTurns");
            AppendMissing(builder, state.AllowLaneChangesWhenGoingStraight, "AllowLaneChangesWhenGoingStraight");
            AppendMissing(builder, state.AllowEnterWhenBlocked, "AllowEnterWhenBlocked");
            AppendMissing(builder, state.AllowPedestrianCrossing, "AllowPedestrianCrossing");
            AppendMissing(builder, state.AllowNearTurnOnRed, "AllowNearTurnOnRed");
            AppendMissing(builder, state.AllowFarTurnOnRed, "AllowFarTurnOnRed");

            return builder.ToString();
        }

        private static string DescribeLostFlags(JunctionRestrictionsState previous, JunctionRestrictionsState snapshot)
        {
            var builder = new StringBuilder();

            AppendLost(builder, previous.AllowUTurns, snapshot.AllowUTurns, "AllowUTurns");
            AppendLost(builder, previous.AllowLaneChangesWhenGoingStraight, snapshot.AllowLaneChangesWhenGoingStraight, "AllowLaneChangesWhenGoingStraight");
            AppendLost(builder, previous.AllowEnterWhenBlocked, snapshot.AllowEnterWhenBlocked, "AllowEnterWhenBlocked");
            AppendLost(builder, previous.AllowPedestrianCrossing, snapshot.AllowPedestrianCrossing, "AllowPedestrianCrossing");
            AppendLost(builder, previous.AllowNearTurnOnRed, snapshot.AllowNearTurnOnRed, "AllowNearTurnOnRed");
            AppendLost(builder, previous.AllowFarTurnOnRed, snapshot.AllowFarTurnOnRed, "AllowFarTurnOnRed");

            return builder.ToString();
        }

        private static void AppendMissing(StringBuilder builder, bool? value, string name)
        {
            if (value.HasValue)
                return;

            if (builder.Length > 0)
                builder.Append(", ");

            builder.Append(name);
        }

        private static void AppendLost(StringBuilder builder, bool? previous, bool? current, string name)
        {
            if (!previous.HasValue || current.HasValue)
                return;

            if (builder.Length > 0)
                builder.Append(", ");

            builder.Append(name);
        }

        private static JunctionRestrictionsState Clone(JunctionRestrictionsState state)
        {
            if (state == null)
                return null;

            var clone = state.Clone();
            clone.Pending = state.Pending?.Clone();
            return clone;
        }
    }
}
