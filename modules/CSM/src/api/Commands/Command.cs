using System;
using System.Linq;
using System.Reflection;
using CSM.API;

namespace CSM.API.Commands
{
    public static class Command
    {
        public static Action<CommandBase> SendToAll, SendToServer, SendToClients;
        public static Action<int, CommandBase> SendToClient = LegacySendToClient;
        public static Func<Type, CommandHandler> GetCommandHandler;
        public static Func<Connection, bool> RegisterConnection = LegacyRegisterConnection;
        public static Func<Connection, bool> UnregisterConnection = LegacyUnregisterConnection;
        public static Func<Connection[]> GetRegisteredConnections = LegacyGetRegisteredConnections;

        public static MultiplayerRole CurrentRole { get; set; }

        public static void ConnectToCSM(Action<CommandBase> sendToAll, Action<CommandBase> sendToServer, Action<CommandBase> sendToClients, Func<Type, CommandHandler> getCommandHandler)
        {
            ConnectToCSM(sendToAll, sendToServer, sendToClients, null, getCommandHandler, null, null, null);
        }

        public static void ConnectToCSM(Action<CommandBase> sendToAll,
            Action<CommandBase> sendToServer,
            Action<CommandBase> sendToClients,
            Action<int, CommandBase> sendToClient,
            Func<Type, CommandHandler> getCommandHandler,
            Func<Connection, bool> registerConnection,
            Func<Connection, bool> unregisterConnection,
            Func<Connection[]> getRegisteredConnections)
        {
            SendToAll = sendToAll;
            SendToServer = sendToServer;
            SendToClients = sendToClients;
            SendToClient = sendToClient ?? LegacySendToClient;
            GetCommandHandler = getCommandHandler;

            _registerConnectionOverride = registerConnection;
            _unregisterConnectionOverride = unregisterConnection;
            _getRegisteredConnectionsOverride = getRegisteredConnections;
            _sendToClientOverride = sendToClient;

            RegisterConnection = registerConnection ?? LegacyRegisterConnection;
            UnregisterConnection = unregisterConnection ?? LegacyUnregisterConnection;
            GetRegisteredConnections = getRegisteredConnections ?? LegacyGetRegisteredConnections;
        }

        private static readonly object LegacySync = new object();
        private static Func<Connection, bool> _registerConnectionOverride;
        private static Func<Connection, bool> _unregisterConnectionOverride;
        private static Func<Connection[]> _getRegisteredConnectionsOverride;
        private static Action<int, CommandBase> _sendToClientOverride;

        private static Func<Connection, bool> _legacyRegisterConnection;
        private static Func<Connection, bool> _legacyUnregisterConnection;
        private static Func<Connection[]> _legacyGetRegisteredConnections;
        private static Action<int, CommandBase> _legacySendToClient;

        private static bool _legacyResolutionAttempted;
        private static bool _loggedMissingLegacyRegister;
        private static bool _loggedMissingLegacyUnregister;
        private static bool _loggedMissingLegacyGetConnections;
        private static bool _loggedMissingLegacySendToClient;

        private static bool LegacyRegisterConnection(Connection connection)
        {
            var handler = _registerConnectionOverride ?? _legacyRegisterConnection;
            if (handler == null)
            {
                TryResolveLegacyHooks();
                handler = _registerConnectionOverride ?? _legacyRegisterConnection;
            }

            if (handler != null)
                return SafeInvoke(handler, connection, LogLegacyRegisterFailure);

            LogLegacyRegisterMissing();
            return false;
        }

        private static bool LegacyUnregisterConnection(Connection connection)
        {
            var handler = _unregisterConnectionOverride ?? _legacyUnregisterConnection;
            if (handler == null)
            {
                TryResolveLegacyHooks();
                handler = _unregisterConnectionOverride ?? _legacyUnregisterConnection;
            }

            if (handler != null)
                return SafeInvoke(handler, connection, LogLegacyUnregisterFailure);

            LogLegacyUnregisterMissing();
            return false;
        }

        private static Connection[] LegacyGetRegisteredConnections()
        {
            var resolver = _getRegisteredConnectionsOverride ?? _legacyGetRegisteredConnections;
            if (resolver == null)
            {
                TryResolveLegacyHooks();
                resolver = _getRegisteredConnectionsOverride ?? _legacyGetRegisteredConnections;
            }

            if (resolver != null)
            {
                try
                {
                    return resolver() ?? Array.Empty<Connection>();
                }
                catch (Exception ex)
                {
                    LogLegacyGetConnectionsFailure(ex);
                    return Array.Empty<Connection>();
                }
            }

            LogLegacyGetConnectionsMissing();
            return Array.Empty<Connection>();
        }

        private static void LegacySendToClient(int clientId, CommandBase command)
        {
            var sender = _sendToClientOverride ?? _legacySendToClient;
            if (sender == null)
            {
                TryResolveLegacyHooks();
                sender = _sendToClientOverride ?? _legacySendToClient;
            }

            if (sender != null)
            {
                try
                {
                    sender(clientId, command);
                }
                catch (Exception ex)
                {
                    LogLegacySendToClientFailure(ex);
                }
                return;
            }

            LogLegacySendToClientMissing();
        }

        private static void TryResolveLegacyHooks()
        {
            if (_legacyResolutionAttempted &&
                _legacyRegisterConnection != null &&
                _legacyUnregisterConnection != null &&
                _legacyGetRegisteredConnections != null &&
                _legacySendToClient != null)
            {
                return;
            }

            lock (LegacySync)
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                var csmAssembly = assemblies.FirstOrDefault(a => string.Equals(a.GetName().Name, "CSM", StringComparison.OrdinalIgnoreCase));
                if (csmAssembly == null)
                {
                    _legacyResolutionAttempted = true;
                    return;
                }

                ResolveLegacyModSupportHooks(csmAssembly);
                ResolveLegacyCommandInternalHooks(csmAssembly);
                _legacyResolutionAttempted = true;
            }
        }

        private static void ResolveLegacyModSupportHooks(Assembly csmAssembly)
        {
            var modSupportType = csmAssembly.GetType("CSM.Mods.ModSupport");
            if (modSupportType == null)
                return;

            var instanceProperty = modSupportType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instanceProperty == null)
                return;

            var registerMethod = modSupportType.GetMethod("RegisterConnection", BindingFlags.Public | BindingFlags.Instance);
            if (registerMethod != null)
            {
                _legacyRegisterConnection = connection => InvokeBool(instanceProperty, registerMethod, connection);
            }

            var unregisterMethod = modSupportType.GetMethod("UnregisterConnection", BindingFlags.Public | BindingFlags.Instance);
            if (unregisterMethod != null)
            {
                _legacyUnregisterConnection = connection => InvokeBool(instanceProperty, unregisterMethod, connection);
            }

            var getConnectionsMethod = modSupportType.GetMethod("GetRegisteredConnections", BindingFlags.Public | BindingFlags.Instance);
            if (getConnectionsMethod != null)
            {
                _legacyGetRegisteredConnections = () =>
                {
                    var instance = instanceProperty.GetValue(null, null);
                    if (instance == null)
                        return Array.Empty<Connection>();

                    try
                    {
                        return getConnectionsMethod.Invoke(instance, Array.Empty<object>()) as Connection[] ?? Array.Empty<Connection>();
                    }
                    catch
                    {
                        return Array.Empty<Connection>();
                    }
                };
            }
        }

        private static void ResolveLegacyCommandInternalHooks(Assembly csmAssembly)
        {
            var commandInternalType = csmAssembly.GetType("CSM.Commands.CommandInternal");
            if (commandInternalType == null)
                return;

            var instanceField = commandInternalType.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
            var sendToClientMethod = commandInternalType.GetMethod("SendToClient", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int), typeof(CommandBase) }, null);

            if (instanceField != null && sendToClientMethod != null)
            {
                _legacySendToClient = (clientId, command) =>
                {
                    var instance = instanceField.GetValue(null);
                    if (instance == null)
                        return;

                    try
                    {
                        sendToClientMethod.Invoke(instance, new object[] { clientId, command });
                    }
                    catch
                    {
                        // ignored; caller will log
                    }
                };
            }
        }

        private static bool InvokeBool(PropertyInfo instanceProperty, MethodInfo method, Connection argument)
        {
            var instance = instanceProperty.GetValue(null, null);
            if (instance == null)
                return false;

            try
            {
                var result = method.Invoke(instance, new object[] { argument });
                return result is bool boolean && boolean;
            }
            catch
            {
                return false;
            }
        }

        private static bool SafeInvoke(Func<Connection, bool> handler, Connection connection, Action<Exception> onError)
        {
            try
            {
                return handler(connection);
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
                return false;
            }
        }

        private static void LogLegacyRegisterMissing()
        {
            if (_loggedMissingLegacyRegister)
                return;

            _loggedMissingLegacyRegister = true;
            TryLogWarn("CSM legacy register connection hook unavailable.");
        }

        private static void LogLegacyUnregisterMissing()
        {
            if (_loggedMissingLegacyUnregister)
                return;

            _loggedMissingLegacyUnregister = true;
            TryLogWarn("CSM legacy unregister connection hook unavailable.");
        }

        private static void LogLegacyGetConnectionsMissing()
        {
            if (_loggedMissingLegacyGetConnections)
                return;

            _loggedMissingLegacyGetConnections = true;
            TryLogWarn("CSM legacy registered connections enumeration unavailable.");
        }

        private static void LogLegacySendToClientMissing()
        {
            if (_loggedMissingLegacySendToClient)
                return;

            _loggedMissingLegacySendToClient = true;
            TryLogWarn("CSM legacy SendToClient hook unavailable.");
        }

        private static void LogLegacyRegisterFailure(Exception ex)
        {
            TryLogWarn($"CSM legacy register connection hook failed: {ex.Message}");
        }

        private static void LogLegacyUnregisterFailure(Exception ex)
        {
            TryLogWarn($"CSM legacy unregister connection hook failed: {ex.Message}");
        }

        private static void LogLegacyGetConnectionsFailure(Exception ex)
        {
            TryLogWarn($"CSM legacy registered connections enumeration failed: {ex.Message}");
        }

        private static void LogLegacySendToClientFailure(Exception ex)
        {
            TryLogWarn($"CSM legacy SendToClient hook failed: {ex.Message}");
        }

        private static void TryLogWarn(string message)
        {
            try
            {
                if (Log.Instance != null)
                {
                    Log.Warn(message);
                }
            }
            catch
            {
                // ignore logging issues
            }
        }
    }
    
    /// <summary>
    ///     What state our game is in.
    /// </summary>
    public enum MultiplayerRole
    {
        /// <summary>
        ///     The game is not connected to a server acting
        ///     as a server. In this state we leave all game mechanics
        ///     alone.
        /// </summary>
        None,

        /// <summary>
        ///     The game is connect to a server and must broadcast
        ///     it's update to the server and update internal values
        ///     from the server.
        /// </summary>
        Client,

        /// <summary>
        ///     The game is acting as a server, it will send out updates to all connected
        ///     clients and receive information about the game from the clients.
        /// </summary>
        Server
    }
}
