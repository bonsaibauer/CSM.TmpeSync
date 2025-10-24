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
            if (registerConnection == null)
            {
                LogMissingRegister();
                return ConnectionRegistrationResult.Failure;
            }

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

        internal static bool UnregisterConnection(Connection connection)
        {
            if (connection == null)
                return false;

            var unregisterConnection = GetUnregisterConnectionDelegate();
            if (unregisterConnection == null)
            {
                LogMissingUnregister();
                return false;
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
                Log.Info(LogCategory.Diagnostics, "Command.SendToClient hook | value={0}", DescribeDelegate(GetSendToClientDelegate()));
                Log.Info(LogCategory.Diagnostics, "Command.RegisterConnection hook | value={0}", DescribeDelegate(GetRegisterConnectionDelegate()));
                Log.Info(LogCategory.Diagnostics, "Command.UnregisterConnection hook | value={0}", DescribeDelegate(GetUnregisterConnectionDelegate()));

                var getConnections = GetRegisteredConnectionsDelegate();
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

            return GetRegisteredConnectionsMethodDelegate;
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
