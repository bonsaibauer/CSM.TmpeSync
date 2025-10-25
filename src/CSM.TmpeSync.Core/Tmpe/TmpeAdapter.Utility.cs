using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Util;
using ColossalFramework;

namespace CSM.TmpeSync.Tmpe
{
    internal static partial class TmpeAdapter
    {
        private static object UtilityManagerInstance;
        private static MethodInfo UtilityManagerClearTrafficMethod;

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
                if (!SupportsClearTraffic || UtilityManagerInstance == null || UtilityManagerClearTrafficMethod == null)
                {
                    Log.Warn(
                        LogCategory.Bridge,
                        "TM:PE clear traffic unavailable | supported={0} hasInstance={1} hasMethod={2}",
                        SupportsClearTraffic,
                        UtilityManagerInstance != null,
                        UtilityManagerClearTrafficMethod != null);
                    return false;
                }

                Log.Debug(LogCategory.Hook, "TM:PE clear traffic request");
                UtilityManagerClearTrafficMethod.Invoke(UtilityManagerInstance, new object[0]);
                Log.Info(LogCategory.Synchronization, "TM:PE clear traffic applied via API");
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
