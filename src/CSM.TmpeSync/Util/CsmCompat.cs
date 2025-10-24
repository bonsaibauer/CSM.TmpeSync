using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSM.API;
using CSM.API.Commands;
using CSM.API.Helpers;

namespace CSM.TmpeSync.Util
{
    internal static class CsmCompat
    {
        private static bool _stubSimulationLogged;
        private static bool _loggedMissingSendToAll;
        private static bool _loggedMissingSendToClients;
        private static bool _loggedMissingSendToServer;
        private static bool _loggedMissingSendToClient;
        private static bool _loggedMissingRegister;
        private static bool _loggedMissingUnregister;

        private static readonly object LegacySync = new object();
        private static Func<Connection, bool> _legacyRegisterConnection;
        private static Func<Connection, bool> _legacyUnregisterConnection;
        private static Func<Connection[]> _legacyGetRegisteredConnections;
        private static Action<int, CommandBase> _legacySendToClient;

        internal enum ConnectionRegistrationResult
        {
            Failure,
            Registered,
            AlreadyRegistered
        }

        internal static ConnectionRegistrationResult RegisterConnection(Connection connection)
        {
            if (connection == null)
                return ConnectionRegistrationResult.Failure;

            var register = GetRegisterConnectionHook();
            if (register == null)
            {
                LogMissingRegister();
                return ConnectionRegistrationResult.Failure;
            }

            try
            {
                if (IsConnectionPresent(connection))
                    return ConnectionRegistrationResult.AlreadyRegistered;

                return register(connection)
                    ? ConnectionRegistrationResult.Registered
                    : ConnectionRegistrationResult.Failure;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "Unable to register CSM connection | error={0}", ex);
                return ConnectionRegistrationResult.Failure;
            }
        }

        internal static bool UnregisterConnection(Connection connection)
        {
            if (connection == null)
                return false;

            var unregister = GetUnregisterConnectionHook();
            if (unregister == null)
            {
                LogMissingUnregister();
                return false;
            }

            try
            {
                return unregister(connection);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "Unable to unregister CSM connection | error={0}", ex);
                return false;
            }
        }

        private static bool IsConnectionPresent(Connection connection)
        {
            var getConnections = GetRegisteredConnectionsHook();
            if (getConnections == null)
                return false;

            try
            {
                var connections = getConnections();
                if (connections == null)
                    return false;

                foreach (var existing in connections)
                {
                    if (existing == null)
                        continue;

                    if (ReferenceEquals(existing, connection))
                        return true;

                    if (existing.GetType() == connection.GetType())
                        return true;

                    if (existing.ModClass != null && connection.ModClass != null && existing.ModClass == connection.ModClass)
                        return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, "Failed to inspect registered CSM connections | error={0}", ex);
            }

            return false;
        }

        internal static void SendToAll(CommandBase command)
        {
            if (command == null)
                return;

            var commandName = command.GetType().Name;
            Log.Debug(LogCategory.Network, "Dispatch command | direction=all type={0}", commandName);

            if (Invoke(Command.SendToAll, command))
            {
                Log.Debug(LogCategory.Network, "Dispatch complete | direction=all type={0}", commandName);
                return;
            }

            Log.Debug(LogCategory.Network, "Dispatch delegate missing | direction=all type={0} action=fallback", commandName);

            if (!_loggedMissingSendToAll)
            {
                _loggedMissingSendToAll = true;
                Log.Warn(LogCategory.Network, "Unable to broadcast command | reason=no_send_delegate type={0}", command.GetType().FullName);
            }

            if (IsServerInstance())
            {
                Log.Debug(LogCategory.Network, "Fallback via SendToClients | type={0}", commandName);
                SendToClients(command);
            }
            else
            {
                Log.Debug(LogCategory.Network, "Fallback via SendToServer | type={0}", commandName);
                SendToServer(command);
            }
        }

        internal static void SendToClients(CommandBase command)
        {
            if (command == null)
                return;

            var commandName = command.GetType().Name;
            Log.Debug(LogCategory.Network, "Dispatch command | direction=clients type={0}", commandName);

            if (!IsServerInstance())
            {
                Log.Debug(LogCategory.Network, "Forwarding client-bound command to server | type={0}", commandName);
                SendToServer(command);
                return;
            }

            if (Invoke(Command.SendToClients, command))
            {
                Log.Debug(LogCategory.Network, "Dispatch complete | direction=clients type={0}", commandName);
                return;
            }

            Log.Debug(LogCategory.Network, "Dispatch delegate missing | direction=clients type={0}", commandName);

            if (!_loggedMissingSendToClients)
            {
                _loggedMissingSendToClients = true;
                Log.Warn(LogCategory.Network, "Unable to send command to connected clients | reason=no_send_delegate type={0}", command.GetType().FullName);
            }
        }

        internal static void SendToServer(CommandBase command)
        {
            if (command == null)
                return;

            var commandName = command.GetType().Name;
            Log.Debug(LogCategory.Network, "Dispatch command | direction=server type={0}", commandName);

            if (Invoke(Command.SendToServer, command))
            {
                Log.Debug(LogCategory.Network, "Dispatch complete | direction=server type={0}", commandName);
                return;
            }

            Log.Debug(LogCategory.Network, "Dispatch delegate missing | direction=server type={0}", commandName);

            if (!_loggedMissingSendToServer)
            {
                _loggedMissingSendToServer = true;
                Log.Warn(LogCategory.Network, "Unable to send command to server | reason=no_send_delegate type={0}", command.GetType().FullName);
            }
        }

        internal static void SendToClient(int clientId, CommandBase command)
        {
            if (command == null)
                return;

            var commandName = command.GetType().Name;
            Log.Debug(LogCategory.Network, "Dispatch command | direction=client type={0} clientId={1}", commandName, clientId);

            if (!IsServerInstance())
            {
                if (!_loggedMissingSendToClient)
                {
                    _loggedMissingSendToClient = true;
                    Log.Warn(LogCategory.Network, "Ignoring SendToClient call while not acting as server");
                }

                Log.Debug(LogCategory.Network, "SendToClient ignored on non-server instance | type={0} clientId={1}", commandName, clientId);
                return;
            }

            var sendToClient = GetSendToClientDelegate();
            if (sendToClient != null)
            {
                try
                {
                    sendToClient(clientId, command);
                    Log.Debug(LogCategory.Network, "Dispatch complete | direction=client type={0} clientId={1}", commandName, clientId);
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warn(LogCategory.Network, "Failed to send command to client | id={0} command={1} error={2}", clientId, command.GetType().Name, ex);
                }
            }

            Log.Debug(LogCategory.Network, "Dispatch delegate missing | direction=client type={0} clientId={1}", commandName, clientId);

            if (!_loggedMissingSendToClient)
            {
                _loggedMissingSendToClient = true;
                Log.Warn(LogCategory.Network, "Unable to send command to client | id={0} command={1} reason=no_send_delegate", clientId, command.GetType().Name);
            }

            Log.Debug(LogCategory.Network, "Command not delivered to client | type={0} clientId={1}", commandName, clientId);
        }

        private static bool Invoke(Action<CommandBase> action, CommandBase command)
        {
            if (action == null)
                return false;

            try
            {
                action(command);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "Failed to invoke CSM delegate {0} | error={1}", DescribeDelegate(action), ex);
                return false;
            }
        }

        private static void LogMissingRegister()
        {
            if (_loggedMissingRegister)
                return;

            _loggedMissingRegister = true;
            Log.Warn(LogCategory.Network, "CSM.API register hook missing");
        }

        private static void LogMissingUnregister()
        {
            if (_loggedMissingUnregister)
                return;

            _loggedMissingUnregister = true;
            Log.Warn(LogCategory.Network, "CSM.API unregister hook missing");
        }

        internal static int GetSenderId(CommandBase command) => command?.SenderId ?? -1;

        internal static bool IsServerInstance() => Command.CurrentRole == MultiplayerRole.Server;

        internal static string DescribeCurrentRole() => Command.CurrentRole.ToString();

        internal static IDisposable StartIgnore()
        {
            try
            {
                var helper = IgnoreHelper.Instance;
                if (helper == null)
                    return DummyScope.Instance;

                helper.StartIgnore();
                return new IgnoreScope(helper);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "Failed to start ignore scope | error={0}", ex);
                return DummyScope.Instance;
            }
        }

        internal static void EnsureStubSimulationActive()
        {
            if (_stubSimulationLogged)
                return;

            _stubSimulationLogged = true;
            Log.Debug(LogCategory.Diagnostics, "Stub simulation hooks unavailable | action=skip_autostart");
        }

        internal static void ResetStubSimulationState()
        {
            _stubSimulationLogged = false;
        }

        internal static void LogDiagnostics(string context)
        {
            try
            {
                Log.Info(LogCategory.Diagnostics, "Diagnostics snapshot | context={0}", string.IsNullOrEmpty(context) ? "<unspecified>" : context);
                Log.Info(LogCategory.Diagnostics, "Command.CurrentRole | value={0}", Command.CurrentRole);
                Log.Info(LogCategory.Diagnostics, "Command.SendToAll delegate | value={0}", DescribeDelegate(Command.SendToAll));
                Log.Info(LogCategory.Diagnostics, "Command.SendToServer delegate | value={0}", DescribeDelegate(Command.SendToServer));
                Log.Info(LogCategory.Diagnostics, "Command.SendToClients delegate | value={0}", DescribeDelegate(Command.SendToClients));
                Log.Info(LogCategory.Diagnostics, "Command.SendToClient delegate | value={0}", DescribeDelegate(GetSendToClientDelegate()));
                Log.Info(LogCategory.Diagnostics, "Command.RegisterConnection hook | value={0}", DescribeDelegate(GetRegisterConnectionHook()));
                Log.Info(LogCategory.Diagnostics, "Command.UnregisterConnection hook | value={0}", DescribeDelegate(GetUnregisterConnectionHook()));

                var getConnections = GetRegisteredConnectionsHook();
                var connections = getConnections != null ? getConnections() : null;
                if (connections != null)
                {
                    var summary = connections
                        .Where(c => c != null)
                        .Select(c => c.Name ?? c.GetType().Name)
                        .ToArray();
                    Log.Info(LogCategory.Diagnostics, "Registered connections | count={0} items={1}",
                        summary.Length,
                        summary.Length == 0 ? "<empty>" : string.Join(", ", summary));
                }
                else
                {
                    Log.Info(LogCategory.Diagnostics, "Registered connections | value=<unavailable>");
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, "Failed to log CSM diagnostics | error={0}", ex);
            }
        }

        private static string DescribeDelegate(Delegate del)
        {
            if (del == null)
                return "<null>";

            var method = del.Method;
            var typeName = method.DeclaringType != null ? method.DeclaringType.FullName : "<unknown>";
            return typeName + "." + method.Name;
        }

        private static Func<Connection, bool> GetRegisterConnectionHook()
        {
            var hook = GetCommandDelegate("RegisterConnection") as Func<Connection, bool>;
            if (hook != null)
                return hook;

            EnsureLegacyHooks();
            return _legacyRegisterConnection;
        }

        private static Func<Connection, bool> GetUnregisterConnectionHook()
        {
            var hook = GetCommandDelegate("UnregisterConnection") as Func<Connection, bool>;
            if (hook != null)
                return hook;

            EnsureLegacyHooks();
            return _legacyUnregisterConnection;
        }

        private static Func<Connection[]> GetRegisteredConnectionsHook()
        {
            var hook = GetCommandDelegate("GetRegisteredConnections") as Func<Connection[]>;
            if (hook != null)
                return hook;

            EnsureLegacyHooks();
            return _legacyGetRegisteredConnections;
        }

        private static Action<int, CommandBase> GetSendToClientDelegate()
        {
            var hook = GetCommandDelegate("SendToClient") as Action<int, CommandBase>;
            if (hook != null)
                return hook;

            EnsureLegacyHooks();
            return _legacySendToClient;
        }

        private static Delegate GetCommandDelegate(string memberName)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;

            try
            {
                var field = typeof(Command).GetField(memberName, flags);
                if (field != null)
                {
                    return field.GetValue(null) as Delegate;
                }

                var property = typeof(Command).GetProperty(memberName, flags);
                if (property != null)
                {
                    return property.GetValue(null, null) as Delegate;
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, "Failed to inspect CSM command delegate | member={0} error={1}", memberName, ex);
            }

            return null;
        }

        private static void EnsureLegacyHooks()
        {
            if (_legacyRegisterConnection != null &&
                _legacyUnregisterConnection != null &&
                _legacyGetRegisteredConnections != null &&
                _legacySendToClient != null)
            {
                return;
            }

            lock (LegacySync)
            {
                if (_legacyRegisterConnection != null &&
                    _legacyUnregisterConnection != null &&
                    _legacyGetRegisteredConnections != null &&
                    _legacySendToClient != null)
                {
                    return;
                }

                var csmAssembly = FindCsmAssembly();
                if (csmAssembly == null)
                    return;

                ResolveLegacyModSupportHooks(csmAssembly);
                ResolveLegacyCommandInternalHooks(csmAssembly);
            }
        }

        private static Assembly FindCsmAssembly()
        {
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly == null)
                        continue;

                    if (assembly.GetType("CSM.Mods.ModSupport") != null ||
                        assembly.GetType("CSM.Commands.CommandInternal") != null)
                    {
                        return assembly;
                    }
                }

                // Try to resolve by name if the assembly was not yet loaded into the AppDomain.
                var knownNames = new[]
                {
                    "CSM",
                    "CSM.Mods",
                    "CitiesSkylinesMultiplayer"
                };

                foreach (var name in knownNames)
                {
                    try
                    {
                        var candidate = Assembly.Load(name);
                        if (candidate != null &&
                            (candidate.GetType("CSM.Mods.ModSupport") != null ||
                             candidate.GetType("CSM.Commands.CommandInternal") != null))
                        {
                            return candidate;
                        }
                    }
                    catch
                    {
                        // Ignored — assembly might not exist under this name.
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, "Failed to probe CSM assembly for legacy hooks | error={0}", ex);
            }

            return null;
        }

        private static void ResolveLegacyModSupportHooks(Assembly csmAssembly)
        {
            var modSupportType = csmAssembly.GetType("CSM.Mods.ModSupport");
            if (modSupportType == null)
                return;

            const BindingFlags methodFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            const BindingFlags instanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            var instanceAccessor = modSupportType.GetProperty("Instance", instanceFlags)
                ?? (MemberInfo)modSupportType.GetField("Instance", instanceFlags);
            if (instanceAccessor == null)
                return;

            var registerMethod = modSupportType.GetMethod("RegisterConnection", methodFlags, null, new[] { typeof(Connection) }, null);
            if (registerMethod != null)
            {
                _legacyRegisterConnection = connection => InvokeLegacyBool(instanceAccessor, registerMethod, connection);
            }

            var unregisterMethod = modSupportType.GetMethod("UnregisterConnection", methodFlags, null, new[] { typeof(Connection) }, null);
            if (unregisterMethod != null)
            {
                _legacyUnregisterConnection = connection => InvokeLegacyBool(instanceAccessor, unregisterMethod, connection);
            }

            var getConnectionsMethod = modSupportType.GetMethod("GetRegisteredConnections", methodFlags, null, Type.EmptyTypes, null);
            if (getConnectionsMethod != null)
            {
                _legacyGetRegisteredConnections = () => InvokeLegacyConnections(instanceAccessor, getConnectionsMethod);
            }
        }

        private static void ResolveLegacyCommandInternalHooks(Assembly csmAssembly)
        {
            var commandInternalType = csmAssembly.GetType("CSM.Commands.CommandInternal");
            if (commandInternalType == null)
                return;

            var instanceField = commandInternalType.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                ?? commandInternalType.GetField("s_instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var sendToClientMethod = commandInternalType.GetMethod(
                "SendToClient",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(int), typeof(CommandBase) },
                null);

            if (sendToClientMethod != null)
            {
                _legacySendToClient = (clientId, command) =>
                {
                    object target = null;
                    if (!sendToClientMethod.IsStatic)
                    {
                        var field = instanceField;
                        if (field == null)
                            return;

                        target = field.GetValue(null);
                        if (target == null)
                            return;
                    }

                    try
                    {
                        sendToClientMethod.Invoke(target, new object[] { clientId, command });
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(LogCategory.Network, "Legacy SendToClient invocation failed | clientId={0} command={1} error={2}",
                            clientId,
                            command?.GetType().Name ?? "<null>",
                            ex);
                    }
                };
            }
        }

        private static object GetLegacyInstance(MemberInfo accessor)
        {
            switch (accessor)
            {
                case PropertyInfo property:
                    return property.GetValue(null, null);
                case FieldInfo field:
                    return field.GetValue(null);
                default:
                    return null;
            }
        }

        private static bool InvokeLegacyBool(MemberInfo instanceAccessor, MethodInfo method, Connection argument)
        {
            var instance = GetLegacyInstance(instanceAccessor);
            if (instance == null)
                return false;

            try
            {
                var result = method.Invoke(instance, new object[] { argument });
                return result is bool success && success;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "Legacy {0} invocation failed | connection={1} error={2}",
                    method.Name,
                    argument?.Name ?? argument?.GetType().Name ?? "<null>",
                    ex);
                return false;
            }
        }

        private static Connection[] InvokeLegacyConnections(MemberInfo instanceAccessor, MethodInfo method)
        {
            var instance = GetLegacyInstance(instanceAccessor);
            if (instance == null)
                return Array.Empty<Connection>();

            try
            {
                return method.Invoke(instance, Array.Empty<object>()) as Connection[] ?? Array.Empty<Connection>();
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "Legacy GetRegisteredConnections invocation failed | error={0}", ex);
                return Array.Empty<Connection>();
            }
        }

        private sealed class IgnoreScope : IDisposable
        {
            private readonly IgnoreHelper _helper;
            private bool _disposed;

            internal IgnoreScope(IgnoreHelper helper)
            {
                _helper = helper;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                try
                {
                    _helper.EndIgnore();
                }
                catch (Exception ex)
                {
                    Log.Warn(LogCategory.Network, "Failed to end ignore scope | error={0}", ex);
                }
            }
        }

        private sealed class DummyScope : IDisposable
        {
            internal static readonly DummyScope Instance = new DummyScope();
            public void Dispose()
            {
            }
        }
    }
}
