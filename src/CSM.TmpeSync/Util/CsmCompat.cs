using System;
using System.Collections;
using System.Reflection;
using CSM.API.Commands;
using CSM.API.Helpers;
using CSM.API.Networking;

namespace CSM.TmpeSync.Util
{
    internal static class CsmCompat
    {
        private static bool _stubSimulationLogged;
        private static bool _loggedMissingSendToAll;
        private static bool _loggedMissingSendToClients;
        private static bool _loggedSendToClientNotServer;
        private static bool _loggedSendToClientUnavailable;
        private static readonly object SendToClientReflectionLock = new object();
        private static bool _sendToClientReflectionInitialised;
        private static bool _sendToClientReflectionFailed;
        private static FieldInfo _commandInternalInstanceField;
        private static MethodInfo _commandInternalSendToClientMethod;
        private static PropertyInfo _multiplayerManagerInstanceProperty;
        private static PropertyInfo _multiplayerManagerCurrentServerProperty;
        private static PropertyInfo _serverConnectedPlayersProperty;

        private static bool Invoke(Action<CommandBase> sendDelegate, CommandBase command)
        {
            if (sendDelegate == null)
                return false;

            try
            {
                sendDelegate(command);
                return true;
            }
            catch (Exception ex)
            {
                var commandName = command != null ? command.GetType().Name : "<null>";
                Log.Warn(LogCategory.Network, "Dispatch via CSM delegate failed | delegate={0} type={1} error={2}", DescribeDelegate(sendDelegate), commandName, ex);
                return false;
            }
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

            if (!_loggedMissingSendToAll)
            {
                _loggedMissingSendToAll = true;
                Log.Warn(LogCategory.Network, "Unable to broadcast command | reason=no_send_delegate type={0}", command.GetType().FullName);
            }

            Log.Debug(LogCategory.Network, "Dispatch delegate missing | direction=all type={0}", commandName);
        }

        internal static void SendToClients(CommandBase command)
        {
            if (command == null)
                return;

            var commandName = command.GetType().Name;
            Log.Debug(LogCategory.Network, "Dispatch command | direction=clients type={0}", commandName);

            if (!IsServerInstance())
            {
                if (!_loggedSendToClientNotServer)
                {
                    _loggedSendToClientNotServer = true;
                    Log.Warn(LogCategory.Network, "Ignoring SendToClients call while not acting as server");
                }

                Log.Debug(LogCategory.Network, "SendToClients ignored on non-server instance | type={0}", commandName);
                return;
            }

            if (Invoke(Command.SendToClients, command))
            {
                Log.Debug(LogCategory.Network, "Dispatch complete | direction=clients type={0}", commandName);
                return;
            }

            if (!_loggedMissingSendToClients)
            {
                _loggedMissingSendToClients = true;
                Log.Warn(LogCategory.Network, "Unable to send command to connected clients | reason=no_send_delegate type={0}", command.GetType().FullName);
            }

            Log.Debug(LogCategory.Network, "Dispatch delegate missing | direction=clients type={0}", commandName);
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

            Log.Warn(LogCategory.Network, "Unable to send command to server | reason=no_send_delegate type={0}", command.GetType().FullName);
            Log.Debug(LogCategory.Network, "Dispatch delegate missing | direction=server type={0}", commandName);
        }

        internal static void SendToClient(int clientId, CommandBase command)
        {
            if (command == null)
                return;

            var commandName = command.GetType().Name;
            Log.Debug(LogCategory.Network, "Dispatch command | direction=client type={0} clientId={1}", commandName, clientId);

            if (!IsServerInstance())
            {
                if (!_loggedSendToClientNotServer)
                {
                    _loggedSendToClientNotServer = true;
                    Log.Warn(LogCategory.Network, "Ignoring SendToClient call while not acting as server");
                }

                Log.Debug(LogCategory.Network, "SendToClient ignored on non-server instance | type={0} clientId={1}", commandName, clientId);
                return;
            }

            if (TrySendToClientInternal(clientId, command, out var failureReason))
            {
                Log.Debug(LogCategory.Network, "Dispatch complete | direction=client type={0} clientId={1}", commandName, clientId);
                return;
            }

            if (!_loggedSendToClientUnavailable)
            {
                _loggedSendToClientUnavailable = true;
                Log.Warn(LogCategory.Network, "Unable to send command to client | id={0} command={1} reason={2}", clientId, command.GetType().Name, failureReason ?? "api_unavailable");
            }

            Log.Debug(LogCategory.Network, "Command not delivered to client | type={0} clientId={1} reason={2}", commandName, clientId, failureReason ?? "unknown");
        }

        private static bool EnsureSendToClientReflection()
        {
            if (_sendToClientReflectionInitialised)
                return true;

            if (_sendToClientReflectionFailed)
                return false;

            lock (SendToClientReflectionLock)
            {
                if (_sendToClientReflectionInitialised)
                    return true;

                if (_sendToClientReflectionFailed)
                    return false;

                try
                {
                    var commandInternalType = ResolveType("CSM.Commands.CommandInternal");
                    var multiplayerManagerType = ResolveType("CSM.Networking.MultiplayerManager");

                    if (commandInternalType == null || multiplayerManagerType == null)
                        throw new InvalidOperationException("CSM reflection types unavailable");

                    _commandInternalInstanceField = commandInternalType.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                    _commandInternalSendToClientMethod = FindSendToClientMethod(commandInternalType);
                    _multiplayerManagerInstanceProperty = multiplayerManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    _multiplayerManagerCurrentServerProperty = multiplayerManagerType.GetProperty("CurrentServer", BindingFlags.Public | BindingFlags.Instance);

                    var serverType = _multiplayerManagerCurrentServerProperty != null
                        ? _multiplayerManagerCurrentServerProperty.PropertyType
                        : null;

                    _serverConnectedPlayersProperty = serverType?.GetProperty("ConnectedPlayers", BindingFlags.Public | BindingFlags.Instance);

                    if (_commandInternalInstanceField == null ||
                        _commandInternalSendToClientMethod == null ||
                        _multiplayerManagerInstanceProperty == null ||
                        _multiplayerManagerCurrentServerProperty == null ||
                        _serverConnectedPlayersProperty == null)
                    {
                        throw new InvalidOperationException("CSM reflection members unavailable");
                    }

                    _sendToClientReflectionInitialised = true;
                    return true;
                }
                catch (Exception ex)
                {
                    _sendToClientReflectionFailed = true;
                    Log.Warn(LogCategory.Network, "Failed to initialise CSM reflection for SendToClient | error={0}", ex);
                    return false;
                }
            }
        }

        private static MethodInfo FindSendToClientMethod(Type commandInternalType)
        {
            if (commandInternalType == null)
                return null;

            var methods = commandInternalType.GetMethods(BindingFlags.Instance | BindingFlags.Public);
            foreach (var method in methods)
            {
                if (!string.Equals(method.Name, "SendToClient", StringComparison.Ordinal))
                    continue;

                var parameters = method.GetParameters();
                if (parameters.Length != 2)
                    continue;

                var commandParameter = parameters[1].ParameterType;
                if (typeof(CommandBase).IsAssignableFrom(commandParameter))
                    return method;
            }

            return null;
        }

        private static Type ResolveType(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return null;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, false);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static bool TrySendToClientInternal(int clientId, CommandBase command, out string failureReason)
        {
            failureReason = null;

            if (!EnsureSendToClientReflection())
            {
                failureReason = "reflection_unavailable";
                return false;
            }

            try
            {
                var commandInternal = _commandInternalInstanceField?.GetValue(null);
                if (commandInternal == null)
                {
                    failureReason = "command_internal_missing";
                    return false;
                }

                var multiplayerManager = _multiplayerManagerInstanceProperty?.GetValue(null, null);
                if (multiplayerManager == null)
                {
                    failureReason = "multiplayer_manager_missing";
                    return false;
                }

                var server = _multiplayerManagerCurrentServerProperty?.GetValue(multiplayerManager, null);
                if (server == null)
                {
                    failureReason = "server_missing";
                    return false;
                }

                var connectedPlayersObj = _serverConnectedPlayersProperty?.GetValue(server, null);
                if (!(connectedPlayersObj is IDictionary connectedPlayers))
                {
                    failureReason = "players_unavailable";
                    return false;
                }

                if (!connectedPlayers.Contains(clientId))
                {
                    failureReason = "client_not_found";
                    return false;
                }

                var player = connectedPlayers[clientId];
                if (player == null)
                {
                    failureReason = "player_null";
                    return false;
                }

                _commandInternalSendToClientMethod?.Invoke(commandInternal, new[] { player, command });
                return true;
            }
            catch (Exception ex)
            {
                failureReason = "send_failed";
                Log.Warn(LogCategory.Network, "Failed to send command to client via reflection | clientId={0} command={1} error={2}", clientId, command.GetType().Name, ex);
                return false;
            }
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
                Log.Info(LogCategory.Diagnostics, "Command.GetCommandHandler delegate | value={0}", DescribeDelegate(Command.GetCommandHandler));
                Log.Info(LogCategory.Diagnostics, "Command.SendToClient delegate | value=<unsupported>");
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
