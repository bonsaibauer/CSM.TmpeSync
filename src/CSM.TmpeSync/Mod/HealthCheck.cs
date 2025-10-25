using System;
using System.Collections.Generic;
using System.Linq;
using CSM.API.Commands;
using CSM.TmpeSync.Bridge;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Mod
{
    internal static class HealthCheck
    {
        internal static void Run()
        {
            Log.Info(LogCategory.Diagnostics, "Running integration health check | action=validate_bridges");

            var csmHealthy = EvaluateCsmIntegration();
            var tmpeHealthy = EvaluateTmpeIntegration();

            Log.Info(
                LogCategory.Diagnostics,
                "Integration health result | csm={0} tmpe={1}",
                csmHealthy ? "ok" : "degraded",
                tmpeHealthy ? "ok" : "degraded");

            CsmBridge.LogDiagnostics("HealthCheck");
        }

        private static bool EvaluateCsmIntegration()
        {
            var missing = new List<string>();

            try
            {
                if (Command.SendToAll == null)
                    missing.Add("SendToAll");
                if (Command.SendToServer == null)
                    missing.Add("SendToServer");
                if (Command.SendToClients == null)
                    missing.Add("SendToClients");
                if (Command.GetCommandHandler == null)
                    missing.Add("GetCommandHandler");
            }
            catch (Exception ex)
            {
                missing.Add("delegates_exception:" + ex.GetType().Name);
            }

            if (missing.Count == 0)
            {
                Log.Info(LogCategory.Network, "CSM bridge ready | delegates=available role={0}", CsmBridge.DescribeCurrentRole());
                return true;
            }

            var missingDelegates = missing.ToArray();

            Log.Warn(
                LogCategory.Network,
                "CSM bridge degraded | missing={0} role={1}",
                string.Join(", ", missingDelegates),
                CsmBridge.DescribeCurrentRole());
            return false;
        }

        private static bool EvaluateTmpeIntegration()
        {
            var featureMatrix = TmpeBridgeAdapter.GetFeatureSupportMatrix();

            if (TmpeBridgeAdapter.IsBridgeReady)
            {
                var supported = featureMatrix
                    .Where(pair => pair.Value)
                    .Select(pair => pair.Key)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                Log.Info(
                    LogCategory.Bridge,
                    "TM:PE bridge ready | features={0}",
                    supported.Length == 0 ? "<none>" : string.Join(", ", supported));
                return true;
            }

            var unsupported = featureMatrix
                .Where(pair => !pair.Value)
                .Select(pair =>
                {
                    var reason = TmpeBridgeAdapter.GetUnsupportedReason(pair.Key) ?? "unknown";
                    return pair.Key + "(" + reason + ")";
                })
                .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Log.Warn(
                LogCategory.Bridge,
                "TM:PE bridge unavailable | unsupported={0}",
                unsupported.Length == 0 ? "<unspecified>" : string.Join(", ", unsupported));

            return false;
        }
    }
}
