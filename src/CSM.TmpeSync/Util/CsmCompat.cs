using System;
using System.Collections.Generic;
using System.Linq;
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

            if (Command.RegisterConnection == null)
            {
                LogMissingRegister();
                return ConnectionRegistrationResult.Failure;
            }

            try
            {
                if (IsConnectionPresent(connection))
                    return ConnectionRegistrationResult.AlreadyRegistered;

                return Command.RegisterConnection(connection)
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

            if (Command.UnregisterConnection == null)
            {
                LogMissingUnregister();
                return false;
            }

            try
            {
                return Command.UnregisterConnection(connection);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "Unable to unregister CSM connection | error={0}", ex);
                return false;
            }
        }

        private static bool IsConnectionPresent(Connection connection)
        {
            var getConnections = Command.GetRegisteredConnections;
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

            if (Command.SendToClient != null)
            {
                try
                {
                    Command.SendToClient(clientId, command);
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
                Log.Info(LogCategory.Diagnostics, "Command.SendToClient delegate | value={0}", DescribeDelegate(Command.SendToClient));
                Log.Info(LogCategory.Diagnostics, "Command.RegisterConnection hook | value={0}", DescribeDelegate(Command.RegisterConnection));
                Log.Info(LogCategory.Diagnostics, "Command.UnregisterConnection hook | value={0}", DescribeDelegate(Command.UnregisterConnection));

                var connections = Command.GetRegisteredConnections != null ? Command.GetRegisteredConnections() : null;
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
