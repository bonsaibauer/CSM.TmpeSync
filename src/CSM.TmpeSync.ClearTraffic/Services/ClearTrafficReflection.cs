using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CSM.TmpeSync.ClearTraffic.Services
{
    internal static class ClearTrafficReflection
    {
        private static readonly string[] CandidateTypeNames =
        {
            "TrafficManager.Manager.Impl.UtilityManager",
            "TrafficManager.Manager.UtilityManager",
            "TrafficManager.State.UtilityManager"
        };

        internal static MethodInfo FindClearTrafficMethod()
        {
            foreach (var type in CandidateTypes())
            {
                var method = FindMethodOnType(type, "ClearTraffic", Type.EmptyTypes);
                if (method != null)
                    return method;
            }

            foreach (var type in AllTmpeTypes())
            {
                var method = FindMethodOnType(type, "ClearTraffic", Type.EmptyTypes);
                if (method != null)
                    return method;
            }

            return null;
        }

        internal static object ResolveSingleton(Type managerType)
        {
            if (managerType == null)
                return null;

            var property = managerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (property != null)
                return property.GetValue(null, null);

            var field = managerType.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (field != null)
                return field.GetValue(null);

            return null;
        }

        private static IEnumerable<Type> CandidateTypes()
        {
            foreach (var name in CandidateTypeNames)
            {
                var type = ResolveType(name);
                if (type != null)
                    yield return type;
            }
        }

        private static Type ResolveType(string typeName)
        {
            var type = Type.GetType(typeName, throwOnError: false);
            if (type != null)
                return type;

            foreach (var assembly in CandidateAssemblies())
            {
                type = assembly.GetType(typeName, throwOnError: false);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static IEnumerable<Type> AllTmpeTypes()
        {
            foreach (var assembly in CandidateAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray();
                }

                foreach (var type in types)
                {
                    if (type != null)
                        yield return type;
                }
            }
        }

        private static IEnumerable<Assembly> CandidateAssemblies()
        {
            return AppDomain.CurrentDomain
                .GetAssemblies()
                .OrderByDescending(IsLikelyTmpeAssembly);
        }

        private static bool IsLikelyTmpeAssembly(Assembly assembly)
        {
            var name = assembly?.GetName()?.Name;
            return !string.IsNullOrEmpty(name) &&
                   name.IndexOf("TrafficManager", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static MethodInfo FindMethodOnType(Type type, string name, params Type[] parameterTypes)
        {
            if (type == null || name == null)
                return null;

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (!string.Equals(method.Name, name, StringComparison.Ordinal))
                    continue;

                var parameters = method.GetParameters();
                if (parameters.Length != parameterTypes.Length)
                    continue;

                return method;
            }

            return null;
        }
    }
}
