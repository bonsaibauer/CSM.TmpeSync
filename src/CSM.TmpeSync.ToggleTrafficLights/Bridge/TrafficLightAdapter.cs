using System;
using CSM.TmpeSync.Util;
using TrafficManager.API;
using TrafficManager.API.Manager;

namespace CSM.TmpeSync.ToggleTrafficLights.Bridge
{
    internal static class TrafficLightAdapter
    {
        internal static bool IsFeatureSupported(string featureKey)
        {
            try
            {
                return Implementations.ManagerFactory?.TrafficLightManager != null;
            }
            catch
            {
                return false;
            }
        }

        internal static bool ApplyToggleTrafficLight(ushort nodeId, bool enabled)
        {
            try
            {
                var mgr = Implementations.ManagerFactory?.TrafficLightManager;
                if (mgr == null)
                    return false;

                var current = mgr.HasTrafficLight(nodeId);
                if (current == enabled)
                    return true;

                if (!mgr.CanToggleTrafficLight(nodeId))
                    return false;

                return mgr.ToggleTrafficLight(nodeId);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, "ToggleTrafficLight Apply failed | nodeId={0} enabled={1} error={2}", nodeId, enabled, ex);
                return false;
            }
        }

        internal static bool TryGetToggleTrafficLight(ushort nodeId, out bool enabled)
        {
            enabled = false;
            try
            {
                var mgr = Implementations.ManagerFactory?.TrafficLightManager;
                if (mgr == null)
                    return false;

                enabled = mgr.HasTrafficLight(nodeId);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, "ToggleTrafficLight TryGet failed | nodeId={0} error={1}", nodeId, ex);
                return false;
            }
        }
    }
}
