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
        private static readonly HashSet<ushort> ToggleTrafficLights = new HashSet<ushort>();
        private static object TrafficLightManagerInstance;
        private static MethodInfo GetHasTrafficLightMethod;
        private static MethodInfo SetHasTrafficLightMethod;

        private static bool InitialiseToggleTrafficLightBridge(Assembly tmpeAssembly)
        {
            TrafficLightManagerInstance = null;
            GetHasTrafficLightMethod = null;
            SetHasTrafficLightMethod = null;

            try
            {
                var trafficLightManagerType = tmpeAssembly?.GetType("TrafficManager.Manager.Impl.TrafficLightManager");
                if (trafficLightManagerType == null)
                    LogBridgeGap("Toggle Traffic Lights", "type", "TrafficManager.Manager.Impl.TrafficLightManager");

                TrafficLightManagerInstance = TryGetStaticInstance(trafficLightManagerType, "Toggle Traffic Lights", trafficLightManagerType?.FullName + ".Instance");
                GetHasTrafficLightMethod = trafficLightManagerType?.GetMethod(
                    "GetHasTrafficLight",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(ushort) },
                    null);
                SetHasTrafficLightMethod = trafficLightManagerType?.GetMethod(
                    "SetHasTrafficLight",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(ushort), typeof(bool?) },
                    null);
            }
            catch (Exception ex)
            {
                RecordFeatureGapDetail("toggleTrafficLights", "exception", ex.GetType().Name);
                Log.Warn(LogCategory.Bridge, "TM:PE toggle traffic light bridge initialization failed | error={0}", ex);
            }

            var supported = TrafficLightManagerInstance != null &&
                            GetHasTrafficLightMethod != null &&
                            SetHasTrafficLightMethod != null;
            SetFeatureStatus("toggleTrafficLights", supported, null);
            return supported;
        }

        internal static bool ApplyToggleTrafficLight(ushort nodeId, bool enabled)
        {
            try
            {
                if (TrafficLightManagerInstance != null && SetHasTrafficLightMethod != null)
                {
                    Log.Debug(LogCategory.Hook, "TM:PE toggle traffic light request | nodeId={0} enabled={1}", nodeId, enabled);
                    SetHasTrafficLightMethod.Invoke(TrafficLightManagerInstance, new object[] { nodeId, (bool?)enabled });
                    if (!TryGetToggleTrafficLight(nodeId, out var actual))
                        actual = enabled;

                    lock (StateLock)
                    {
                        if (actual)
                            ToggleTrafficLights.Add(nodeId);
                        else
                            ToggleTrafficLights.Remove(nodeId);
                    }

                    Log.Info(LogCategory.Synchronization, "TM:PE toggle traffic light applied via API | nodeId={0} enabled={1}", nodeId, actual);
                    return true;
                }

                Log.Info(LogCategory.Synchronization, "TM:PE toggle traffic light stored in stub | nodeId={0} enabled={1}", nodeId, enabled);
                lock (StateLock)
                {
                    if (enabled)
                        ToggleTrafficLights.Add(nodeId);
                    else
                        ToggleTrafficLights.Remove(nodeId);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE ApplyToggleTrafficLight failed | error={0}", ex);
                return false;
            }
        }

        internal static bool TryGetToggleTrafficLight(ushort nodeId, out bool enabled)
        {
            try
            {
                if (TrafficLightManagerInstance != null && GetHasTrafficLightMethod != null)
                {
                    var result = GetHasTrafficLightMethod.Invoke(TrafficLightManagerInstance, new object[] { nodeId });
                    enabled = result is bool has && has;

                    lock (StateLock)
                    {
                        if (enabled)
                            ToggleTrafficLights.Add(nodeId);
                        else
                            ToggleTrafficLights.Remove(nodeId);
                    }

                    return true;
                }

                lock (StateLock)
                {
                    enabled = ToggleTrafficLights.Contains(nodeId);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE TryGetToggleTrafficLight failed | error={0}", ex);
                enabled = false;
                return false;
            }
        }

    }
}
