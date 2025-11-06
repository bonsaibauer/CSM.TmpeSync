using System;
using System.Linq;
using System.Reflection;
using CSM.TmpeSync.Services;
using HarmonyLib;
using TrafficManager.API.Traffic.Enums;

namespace CSM.TmpeSync.LaneConnector.Services
{
    internal static class LaneConnectionAdapter
    {
        private static readonly object ManagerInstance;
        private static readonly MethodInfo AddConnectionMethod;
        private static readonly MethodInfo RemoveConnectionMethod;

        static LaneConnectionAdapter()
        {
            try
            {
                var managerType = ResolveManagerType();
                var instanceProperty = managerType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProperty != null)
                    ManagerInstance = instanceProperty.GetValue(null, null);

                if (ManagerInstance != null)
                {
                    AddConnectionMethod = ResolveMethod(managerType, "AddLaneConnection");
                    RemoveConnectionMethod = ResolveMethod(managerType, "RemoveLaneConnection");
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, LogRole.General, "[LaneConnector] Failed to resolve LaneConnectionManager | error={0}", ex);
            }
        }

        internal static bool IsAvailable => ManagerInstance != null && AddConnectionMethod != null && RemoveConnectionMethod != null;

        internal static bool TryAddConnection(uint sourceLaneId, uint targetLaneId, bool sourceStartNode)
        {
            if (!IsAvailable)
                return false;

            try
            {
                var result = AddConnectionMethod.Invoke(
                    ManagerInstance,
                    new object[] { sourceLaneId, targetLaneId, sourceStartNode, LaneEndTransitionGroup.Vehicle });

                return result is bool b ? b : true;
            }
            catch (Exception ex)
            {
                Log.Warn(
                    LogCategory.Bridge,
                    LogRole.Host,
                    "[LaneConnector] AddLaneConnection failed | source={0} target={1} startNode={2} error={3}",
                    sourceLaneId,
                    targetLaneId,
                    sourceStartNode,
                    ex);
                return false;
            }
        }

        internal static bool TryRemoveConnection(uint sourceLaneId, uint targetLaneId, bool sourceStartNode)
        {
            if (!IsAvailable)
                return false;

            try
            {
                var result = RemoveConnectionMethod.Invoke(
                    ManagerInstance,
                    new object[] { sourceLaneId, targetLaneId, sourceStartNode, LaneEndTransitionGroup.Vehicle });

                return result is bool b ? b : true;
            }
            catch (Exception ex)
            {
                Log.Warn(
                    LogCategory.Bridge,
                    LogRole.Host,
                    "[LaneConnector] RemoveLaneConnection failed | source={0} target={1} startNode={2} error={3}",
                    sourceLaneId,
                    targetLaneId,
                    sourceStartNode,
                    ex);
                return false;
            }
        }

        private static Type ResolveManagerType()
        {
            var type =
                AccessTools.TypeByName("TrafficManager.Manager.Impl.LaneConnection.LaneConnectionManager") ??
                AccessTools.TypeByName("TrafficManager.Manager.LaneConnectionManager");

            if (type != null)
                return type;

            return AppDomain.CurrentDomain
                .GetAssemblies()
                .Where(a =>
                {
                    var name = a.GetName().Name;
                    return name != null && name.IndexOf("TrafficManager", StringComparison.OrdinalIgnoreCase) >= 0;
                })
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch (ReflectionTypeLoadException rtle) { return rtle.Types.Where(t => t != null); }
                })
                .FirstOrDefault(t => t.Name == "LaneConnectionManager");
        }

        private static MethodInfo ResolveMethod(Type managerType, string methodName)
        {
            if (managerType == null)
                return null;

            foreach (var method in managerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    continue;

                var parameters = method.GetParameters();
                if (parameters.Length == 4 &&
                    parameters[0].ParameterType == typeof(uint) &&
                    parameters[1].ParameterType == typeof(uint) &&
                    parameters[2].ParameterType == typeof(bool))
                {
                    return method;
                }
            }

            return null;
        }
    }
}
