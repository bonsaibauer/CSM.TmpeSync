using System;
using System.Collections;
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

        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
        private const BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
        private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags AnyStatic = PublicStatic | NonPublicStatic;
        private const BindingFlags AnyInstance = PublicInstance | NonPublicInstance;

        private static readonly FieldInfo RegisterConnectionField = typeof(Command).GetField("RegisterConnection", PublicStatic);
        private static readonly PropertyInfo RegisterConnectionProperty = typeof(Command).GetProperty("RegisterConnection", PublicStatic);
        private static readonly MethodInfo RegisterConnectionMethod = ResolveSingleParameterCommandMethod("RegisterConnection", typeof(Connection));
        private static readonly Func<Connection, bool> RegisterConnectionMethodDelegate = CreateRegisterMethodDelegate();

        private static readonly FieldInfo UnregisterConnectionField = typeof(Command).GetField("UnregisterConnection", PublicStatic);
        private static readonly PropertyInfo UnregisterConnectionProperty = typeof(Command).GetProperty("UnregisterConnection", PublicStatic);
        private static readonly MethodInfo UnregisterConnectionMethod = ResolveSingleParameterCommandMethod("UnregisterConnection", typeof(Connection));
        private static readonly Func<Connection, bool> UnregisterConnectionMethodDelegate = CreateUnregisterMethodDelegate();

        private static readonly FieldInfo GetRegisteredConnectionsField = typeof(Command).GetField("GetRegisteredConnections", PublicStatic);
        private static readonly PropertyInfo GetRegisteredConnectionsProperty = typeof(Command).GetProperty("GetRegisteredConnections", PublicStatic);
        private static readonly MethodInfo GetRegisteredConnectionsMethod = typeof(Command).GetMethods(PublicStatic)
            .FirstOrDefault(m => m.Name == "GetRegisteredConnections" && m.GetParameters().Length == 0);
        private static readonly Func<Connection[]> GetRegisteredConnectionsMethodDelegate = CreateGetRegisteredConnectionsMethodDelegate();

        private static readonly FieldInfo SendToClientField = typeof(Command).GetField("SendToClient", PublicStatic);
        private static readonly PropertyInfo SendToClientProperty = typeof(Command).GetProperty("SendToClient", PublicStatic);
        private static readonly MethodInfo SendToClientMethod = typeof(Command).GetMethods(PublicStatic)
            .FirstOrDefault(m =>
            {
                if (m.Name != "SendToClient")
                    return false;

                var parameters = m.GetParameters();
                if (parameters.Length != 2)
                    return false;

                return parameters[0].ParameterType == typeof(int) &&
                       parameters[1].ParameterType.IsAssignableFrom(typeof(CommandBase));
            });
        private static readonly Action<int, CommandBase> SendToClientMethodDelegate = CreateSendToClientMethodDelegate();

        private static readonly Type CommandInternalType = Type.GetType("CSM.Commands.CommandInternal, CSM");
        private static readonly FieldInfo CommandInternalInstanceField = CommandInternalType?.GetField("Instance", AnyStatic);
        private static readonly MethodInfo CommandInternalRefreshModelMethod = CommandInternalType?.GetMethod("RefreshModel", PublicInstance);
        private static readonly MethodInfo CommandInternalSendToClientMethod = CommandInternalType?
            .GetMethods(AnyInstance)
            .FirstOrDefault(m =>
            {
                if (m.Name != "SendToClient")
                    return false;

                var parameters = m.GetParameters();
                if (parameters.Length != 2)
                    return false;

                var parameterType = parameters[0].ParameterType;
                return string.Equals(parameterType.FullName, "CSM.Models.Player", StringComparison.Ordinal) &&
                       typeof(CommandBase).IsAssignableFrom(parameters[1].ParameterType);
            });

        private static readonly Type ModSupportType = Type.GetType("CSM.Mods.ModSupport, CSM");
        private static readonly PropertyInfo ModSupportInstanceProperty = ModSupportType?.GetProperty("Instance", AnyStatic);
        private static readonly PropertyInfo ModSupportConnectedModsProperty = ModSupportType?.GetProperty("ConnectedMods", AnyInstance);

        private static readonly Type MultiplayerManagerType = Type.GetType("CSM.Networking.MultiplayerManager, CSM");
        private static readonly PropertyInfo MultiplayerManagerInstanceProperty = MultiplayerManagerType?.GetProperty("Instance", AnyStatic);
        private static readonly PropertyInfo MultiplayerManagerCurrentServerProperty = MultiplayerManagerType?.GetProperty("CurrentServer", AnyInstance);

        private static readonly Type ServerType = Type.GetType("CSM.Networking.Server, CSM");
        private static readonly PropertyInfo ServerConnectedPlayersProperty = ServerType?.GetProperty("ConnectedPlayers", AnyInstance);

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

            var registerConnection = GetRegisterConnectionDelegate();
            if (registerConnection != null)
            {
                try
                {
                    if (IsConnectionPresent(connection))
                        return ConnectionRegistrationResult.AlreadyRegistered;

                    return registerConnection(connection)
                        ? ConnectionRegistrationResult.Registered
                        : ConnectionRegistrationResult.Failure;
                }
                catch (Exception ex)
                {
                    Log.Warn(LogCategory.Network, "Unable to register CSM connection | error={0}", ex);
                    return ConnectionRegistrationResult.Failure;
                }
            }

            LogMissingRegister();
            return LegacyRegisterConnection(connection);
        }

        internal static bool UnregisterConnection(Connection connection)
        {
            if (connection == null)
                return false;

            var unregisterConnection = GetUnregisterConnectionDelegate();
            if (unregisterConnection == null)
            {
                LogMissingUnregister();
                return LegacyUnregisterConnection(connection);
            }

            try
            {
                return unregisterConnection(connection);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "Unable to unregister CSM connection | error={0}", ex);
                return false;
            }
        }

        private static bool IsConnectionPresent(Connection connection)
        {
            var getConnections = GetRegisteredConnectionsDelegate();
            if (getConnections == null)
            {
                var legacy = LegacyGetRegisteredConnections();
                return legacy != null && legacy.Any(existing => ConnectionsMatch(existing, connection));
            }

            try
            {
                var connections = getConnections();
                return connections != null && connections.Any(existing => ConnectionsMatch(existing, connection));
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, "Failed to inspect registered CSM connections | error={0}", ex);
            }

            return false;
        }

        private static bool ConnectionsMatch(Connection existing, Connection candidate)
        {
            if (existing == null || candidate == null)
                return false;

            if (ReferenceEquals(existing, candidate))
                return true;

            if (existing.GetType() == candidate.GetType())
                return true;

            if (existing.ModClass != null && candidate.ModClass != null && existing.ModClass == candidate.ModClass)
                return true;

            if (!string.IsNullOrEmpty(existing.Name) && !string.IsNullOrEmpty(candidate.Name) &&
                string.Equals(existing.Name, candidate.Name, StringComparison.Ordinal))
                return true;

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

            if (LegacySendToClient(clientId, command))
            {
                Log.Debug(LogCategory.Network, "Dispatch complete via legacy path | direction=client type={0} clientId={1}", commandName, clientId);
                return;
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
                Log.Info(LogCategory.Diagnostics, "Command.SendToClient hook | value={0}", DescribeDelegate(GetSendToClientDelegate()));
                Log.Info(LogCategory.Diagnostics, "Command.RegisterConnection hook | value={0}", DescribeDelegate(GetRegisterConnectionDelegate()));
                Log.Info(LogCategory.Diagnostics, "Command.UnregisterConnection hook | value={0}", DescribeDelegate(GetUnregisterConnectionDelegate()));

                var getConnections = GetRegisteredConnectionsDelegate();
                var connections = getConnections != null ? getConnections() : LegacyGetRegisteredConnections();
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

        private static Func<Connection, bool> GetRegisterConnectionDelegate()
        {
            var del = ConvertDelegate<Func<Connection, bool>>(ExtractDelegate(RegisterConnectionField));
            if (del != null)
                return del;

            del = ConvertDelegate<Func<Connection, bool>>(ExtractDelegate(RegisterConnectionProperty));
            if (del != null)
                return del;

            return RegisterConnectionMethodDelegate;
        }

        private static Func<Connection, bool> GetUnregisterConnectionDelegate()
        {
            var del = ConvertDelegate<Func<Connection, bool>>(ExtractDelegate(UnregisterConnectionField));
            if (del != null)
                return del;

            del = ConvertDelegate<Func<Connection, bool>>(ExtractDelegate(UnregisterConnectionProperty));
            if (del != null)
                return del;

            return UnregisterConnectionMethodDelegate;
        }

        private static Func<Connection[]> GetRegisteredConnectionsDelegate()
        {
            var del = ExtractDelegate(GetRegisteredConnectionsField);
            var converted = ConvertConnectionsDelegate(del);
            if (converted != null)
                return converted;

            del = ExtractDelegate(GetRegisteredConnectionsProperty);
            converted = ConvertConnectionsDelegate(del);
            if (converted != null)
                return converted;

            return GetRegisteredConnectionsMethodDelegate ?? LegacyGetRegisteredConnections;
        }

        private static Action<int, CommandBase> GetSendToClientDelegate()
        {
            var del = ConvertDelegate<Action<int, CommandBase>>(ExtractDelegate(SendToClientField));
            if (del != null)
                return del;

            del = ConvertDelegate<Action<int, CommandBase>>(ExtractDelegate(SendToClientProperty));
            if (del != null)
                return del;

            return SendToClientMethodDelegate;
        }

        private static Delegate ExtractDelegate(FieldInfo field)
        {
            if (field == null)
                return null;

            try
            {
                return field.GetValue(null) as Delegate;
            }
            catch
            {
                return null;
            }
        }

        private static Delegate ExtractDelegate(PropertyInfo property)
        {
            if (property == null)
                return null;

            try
            {
#if NETSTANDARD || NET5_0_OR_GREATER
                return property.GetValue(null) as Delegate;
#else
                return property.GetValue(null, null) as Delegate;
#endif
            }
            catch
            {
                return null;
            }
        }

        private static TDelegate ConvertDelegate<TDelegate>(Delegate del)
            where TDelegate : class, Delegate
        {
            if (del == null)
                return null;

            if (del is TDelegate typed)
                return typed;

            try
            {
                return (TDelegate)Delegate.CreateDelegate(typeof(TDelegate), del.Target, del.Method);
            }
            catch
            {
                return null;
            }
        }

        private static Func<Connection[]> ConvertConnectionsDelegate(Delegate del)
        {
            if (del == null)
                return null;

            if (del is Func<Connection[]> arrayFunc)
                return arrayFunc;

            if (del is Func<IEnumerable<Connection>> enumerableFunc)
                return () => enumerableFunc()?.ToArray();

            if (del is Func<IEnumerable> nonGenericEnumerable)
                return () => ConvertConnections(nonGenericEnumerable());

            if (del.Method.GetParameters().Length == 0)
            {
                return () => ConvertConnections(del.DynamicInvoke());
            }

            return null;
        }

        private static Connection[] ConvertConnections(object value)
        {
            if (value == null)
                return null;

            if (value is Connection[] array)
                return array;

            if (value is IEnumerable<Connection> enumerable)
                return enumerable.ToArray();

            if (value is IEnumerable nonGeneric)
                return nonGeneric.Cast<object>().OfType<Connection>().ToArray();

            if (value is Connection single)
                return new[] { single };

            return null;
        }

        private static MethodInfo ResolveSingleParameterCommandMethod(string name, Type parameterType)
        {
            return typeof(Command).GetMethods(PublicStatic)
                .FirstOrDefault(m =>
                {
                    if (m.Name != name)
                        return false;

                    var parameters = m.GetParameters();
                    if (parameters.Length != 1)
                        return false;

                    return parameters[0].ParameterType.IsAssignableFrom(parameterType) && m.ReturnType == typeof(bool);
                });
        }

        private static Func<Connection, bool> CreateRegisterMethodDelegate()
        {
            if (RegisterConnectionMethod == null)
                return null;

            try
            {
                return (Func<Connection, bool>)Delegate.CreateDelegate(typeof(Func<Connection, bool>), RegisterConnectionMethod);
            }
            catch
            {
                return connection =>
                {
                    var result = RegisterConnectionMethod.Invoke(null, new object[] { connection });
                    return result is bool flag && flag;
                };
            }
        }

        private static Func<Connection, bool> CreateUnregisterMethodDelegate()
        {
            if (UnregisterConnectionMethod == null)
                return null;

            try
            {
                return (Func<Connection, bool>)Delegate.CreateDelegate(typeof(Func<Connection, bool>), UnregisterConnectionMethod);
            }
            catch
            {
                return connection =>
                {
                    var result = UnregisterConnectionMethod.Invoke(null, new object[] { connection });
                    return result is bool flag && flag;
                };
            }
        }

        private static Func<Connection[]> CreateGetRegisteredConnectionsMethodDelegate()
        {
            if (GetRegisteredConnectionsMethod == null)
                return null;

            return () => ConvertConnections(GetRegisteredConnectionsMethod.Invoke(null, null));
        }

        private static Action<int, CommandBase> CreateSendToClientMethodDelegate()
        {
            if (SendToClientMethod == null)
                return null;

            try
            {
                return (Action<int, CommandBase>)Delegate.CreateDelegate(typeof(Action<int, CommandBase>), SendToClientMethod);
            }
            catch
            {
                return (clientId, command) => SendToClientMethod.Invoke(null, new object[] { clientId, command });
            }
        }

        private static ConnectionRegistrationResult LegacyRegisterConnection(Connection connection)
        {
            var list = GetLegacyConnectionList();
            if (list == null)
                return ConnectionRegistrationResult.Failure;

            if (FindLegacyConnection(list, connection) != null)
                return ConnectionRegistrationResult.AlreadyRegistered;

            try
            {
                list.Add(connection);
                LegacyRefreshCommandModel();
                Log.Debug(LogCategory.Network, "Connection registered via legacy ModSupport | type={0}", connection.GetType().FullName);
                return ConnectionRegistrationResult.Registered;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "Unable to register connection via legacy ModSupport | error={0}", ex);
                return ConnectionRegistrationResult.Failure;
            }
        }

        private static bool LegacyUnregisterConnection(Connection connection)
        {
            var list = GetLegacyConnectionList();
            if (list == null)
                return false;

            var existing = FindLegacyConnection(list, connection);
            if (existing == null)
                return true;

            try
            {
                list.Remove(existing);
                LegacyRefreshCommandModel();
                Log.Debug(LogCategory.Network, "Connection unregistered via legacy ModSupport | type={0}", connection.GetType().FullName);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "Unable to unregister connection via legacy ModSupport | error={0}", ex);
                return false;
            }
        }

        private static Connection[] LegacyGetRegisteredConnections()
        {
            var list = GetLegacyConnectionList();
            if (list == null || list.Count == 0)
                return new Connection[0];

            try
            {
                return list.Cast<object>().OfType<Connection>().ToArray();
            }
            catch
            {
                return new Connection[0];
            }
        }

        private static IList GetLegacyConnectionList()
        {
            if (ModSupportType == null || ModSupportInstanceProperty == null || ModSupportConnectedModsProperty == null)
                return null;

            var instance = GetPropertyValue(ModSupportInstanceProperty, null);
            if (instance == null)
                return null;

            try
            {
                var value = GetPropertyValue(ModSupportConnectedModsProperty, instance);
                return value as IList;
            }
            catch
            {
                return null;
            }
        }

        private static object FindLegacyConnection(IList list, Connection target)
        {
            if (list == null || target == null)
                return null;

            foreach (var entry in list)
            {
                if (entry is Connection existing && ConnectionsMatch(existing, target))
                    return entry;
            }

            return null;
        }

        private static void LegacyRefreshCommandModel()
        {
            if (CommandInternalInstanceField == null || CommandInternalRefreshModelMethod == null)
                return;

            var instance = CommandInternalInstanceField.GetValue(null);
            if (instance == null)
                return;

            try
            {
                CommandInternalRefreshModelMethod.Invoke(instance, null);
            }
            catch (Exception ex)
            {
                Log.Debug(LogCategory.Diagnostics, "Legacy command model refresh failed | error={0}", ex);
            }
        }

        private static bool LegacySendToClient(int clientId, CommandBase command)
        {
            if (CommandInternalInstanceField == null || CommandInternalSendToClientMethod == null)
                return false;

            var instance = CommandInternalInstanceField.GetValue(null);
            if (instance == null)
                return false;

            var player = LegacyFindPlayer(clientId);
            if (player == null)
                return false;

            try
            {
                CommandInternalSendToClientMethod.Invoke(instance, new[] { player, (object)command });
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "Legacy SendToClient failed | id={0} command={1} error={2}", clientId, command.GetType().Name, ex);
                return false;
            }
        }

        private static object LegacyFindPlayer(int clientId)
        {
            if (MultiplayerManagerInstanceProperty == null || MultiplayerManagerCurrentServerProperty == null || ServerConnectedPlayersProperty == null)
                return null;

            var manager = GetPropertyValue(MultiplayerManagerInstanceProperty, null);
            if (manager == null)
                return null;

            var server = GetPropertyValue(MultiplayerManagerCurrentServerProperty, manager);
            if (server == null)
                return null;

            object dictionaryObj;
            try
            {
                dictionaryObj = GetPropertyValue(ServerConnectedPlayersProperty, server);
            }
            catch
            {
                return null;
            }

            if (dictionaryObj is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Key is int id && id == clientId)
                        return entry.Value;
                }
            }

            return null;
        }

        private static object GetPropertyValue(PropertyInfo property, object instance)
        {
            if (property == null)
                return null;

            try
            {
#if NETSTANDARD || NET5_0_OR_GREATER
                return property.GetValue(instance);
#else
                return property.GetValue(instance, null);
#endif
            }
            catch
            {
                return null;
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
