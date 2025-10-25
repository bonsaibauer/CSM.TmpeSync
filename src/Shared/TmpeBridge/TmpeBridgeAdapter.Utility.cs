using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Util;
using ColossalFramework;

namespace CSM.TmpeSync.TmpeBridge
{
    internal static partial class TmpeBridgeAdapter
    {
        private static object UtilityManagerInstance;
        private static MethodInfo UtilityManagerClearTrafficMethod;
        private static int PendingClearTrafficRequests;

        private static bool InitialiseUtilityBridge(Assembly tmpeAssembly)
        {
            UtilityManagerInstance = null;
            UtilityManagerClearTrafficMethod = null;

            try
            {
                var managerType = tmpeAssembly?.GetType("TrafficManager.Manager.Impl.UtilityManager");
                var manager = GetManagerFromFactory("UtilityManager", "Clear Traffic");

                if (manager != null)
                    managerType = manager.GetType();
                else if (managerType != null)
                    manager = TryGetStaticInstance(managerType, "Clear Traffic");

                if (managerType == null)
                    LogBridgeGap("Clear Traffic", "type", "TrafficManager.Manager.Impl.UtilityManager");

                UtilityManagerInstance = manager;

                if (managerType != null)
                {
                    UtilityManagerClearTrafficMethod = managerType.GetMethod(
                        "ClearTraffic",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        Type.EmptyTypes,
                        null);

                    if (UtilityManagerClearTrafficMethod == null)
                        LogBridgeGap("Clear Traffic", "ClearTraffic()", DescribeMethodOverloads(managerType, "ClearTraffic"));
                }
            }
            catch (Exception ex)
            {
                RecordFeatureGapDetail("clearTraffic", "exception", ex.GetType().Name);
                Log.Warn(LogCategory.Bridge, "TM:PE clear traffic bridge initialization failed | error={0}", ex);
            }

            var supported = UtilityManagerInstance != null && UtilityManagerClearTrafficMethod != null;
            SetFeatureStatus("clearTraffic", supported, null);
            return supported;
        }

        internal static bool ClearTraffic()
        {
            try
            {
                var manager = UtilityManagerInstance;
                var method = UtilityManagerClearTrafficMethod;

                if (manager != null && method != null)
                {
                    Log.Debug(LogCategory.Hook, "TM:PE clear traffic request");
                    method.Invoke(manager, EmptyInvocationParameters);
                    Log.Info(LogCategory.Synchronization, "TM:PE clear traffic applied via API");
                    return true;
                }

                int pending;
                lock (StateLock)
                {
                    PendingClearTrafficRequests++;
                    pending = PendingClearTrafficRequests;
                }

                Log.Info(
                    LogCategory.Synchronization,
                    "TM:PE clear traffic stored in stub | supported={0} pending={1}",
                    SupportsClearTraffic,
                    pending);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE ClearTraffic failed | error={0}", ex);
                return false;
            }
        }

    }
}
