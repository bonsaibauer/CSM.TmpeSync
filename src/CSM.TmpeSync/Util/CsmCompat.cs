using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using CSM.API;
using CSM.API.Commands;

namespace CSM.TmpeSync.Util
{
    internal static class CsmCompat
    {
        private static readonly Type CommandType = typeof(Command);
        private static readonly Assembly[] CandidateAssemblies;
        private static readonly PropertyInfo CurrentRoleProperty = CommandType.GetProperty("CurrentRole", BindingFlags.Public | BindingFlags.Static);
        private static readonly PropertyInfo RoleNameProperty = CommandType.GetProperty("Role", BindingFlags.Public | BindingFlags.Static);
        private static readonly PropertyInfo IsServerProperty = CommandType.GetProperty("IsServer", BindingFlags.Public | BindingFlags.Static);
        private static readonly FieldInfo IsServerField = CommandType.GetField("IsServer", BindingFlags.Public | BindingFlags.Static);
        private static readonly MethodInfo RoleCheckMethod = ResolveRoleCheckMethod(CommandType);
        private static readonly PropertyInfo SenderIdProperty = CommandType.GetProperty("SenderId", BindingFlags.Public | BindingFlags.Static) ??
                                                                 CommandType.GetProperty("CurrentSenderId", BindingFlags.Public | BindingFlags.Static);
        private static readonly FieldInfo SenderIdField = CommandType.GetField("SenderId", BindingFlags.Public | BindingFlags.Static);
        private static readonly PropertyInfo SenderProperty = CommandType.GetProperty("Sender", BindingFlags.Public | BindingFlags.Static) ??
                                                               CommandType.GetProperty("CurrentSender", BindingFlags.Public | BindingFlags.Static);

        private static readonly Type IgnoreHelperType = Type.GetType("CSM.API.IgnoreHelper, CSM.API") ??
                                                         Type.GetType("CSM.API.Helpers.IgnoreHelper, CSM.API");
        private static readonly object IgnoreHelperInstance = IgnoreHelperType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
        private static readonly MethodInfo IgnoreStartMethod = ResolveIgnoreMethod("StartIgnore");
        private static readonly MethodInfo IgnoreEndMethod = ResolveIgnoreMethod("EndIgnore");

        private static readonly MethodInfo SimulateClientConnectedMethod = CommandType.GetMethod("SimulateClientConnected", BindingFlags.Public | BindingFlags.Static);
        private static readonly MethodInfo GetSimulatedClientsMethod = CommandType.GetMethod("GetSimulatedClients", BindingFlags.Public | BindingFlags.Static);

        private static readonly HashSet<string> RegisterConnectionNames = new HashSet<string>(new[]
        {
            "RegisterConnection",
            "Register",
            "AddConnection",
            "Add",
            "AttachConnection",
            "Attach",
            "SubscribeConnection",
            "Subscribe",
            "ConnectToCSM",
            "Connect"
        }, StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> UnregisterConnectionNames = new HashSet<string>(new[]
        {
            "UnregisterConnection",
            "Unregister",
            "RemoveConnection",
            "Remove",
            "DetachConnection",
            "Detach",
            "DeregisterConnection",
            "Deregister",
            "UnsubscribeConnection",
            "Unsubscribe",
            "DisconnectFromCSM",
            "Disconnect"
        }, StringComparer.OrdinalIgnoreCase);

        private static readonly string[] SendToClientMethodNames =
        {
            "SendToClient",
            "SendToPeer",
            "SendToPlayer",
            "SendToTarget",
            "SendTo",
            "SendCommandToClient",
            "SendCommandToPeer"
        };

        private static readonly string[] SendToClientsMethodNames =
        {
            "SendToClients",
            "SendToPeers",
            "SendToPlayers",
            "SendToTargets",
            "SendToMany",
            "SendCommandToClients",
            "SendCommandToPeers"
        };

        private static readonly string[] SendToAllMethodNames =
        {
            "SendToAll",
            "SendToEveryone",
            "SendToAllClients",
            "Broadcast",
            "BroadcastToAll",
            "BroadcastCommand"
        };

        private static readonly string[] SendToServerMethodNames =
        {
            "SendToServer",
            "SendCommandToServer",
            "ForwardToServer",
            "RelayToServer"
        };

        private static readonly MethodInfo SendToClientMethod;
        private static readonly object SendToClientTarget;
        private static readonly MethodInfo SendToAllMethod;
        private static readonly object SendToAllTarget;
        private static readonly MethodInfo SendToClientsMethod;
        private static readonly object SendToClientsTarget;
        private static readonly MethodInfo SendToServerMethod;
        private static readonly object SendToServerTarget;
        private static readonly MethodInfo RegisterConnectionMethod;
        private static readonly object RegisterConnectionTarget;
        private static readonly MethodInfo UnregisterConnectionMethod;
        private static readonly object UnregisterConnectionTarget;
        private static readonly Type ModSupportType;
        private static readonly PropertyInfo ModSupportInstanceProperty;
        private static readonly PropertyInfo ConnectedModsProperty;
        private static readonly Type CommandInternalType;
        private static readonly FieldInfo CommandInternalInstanceField;
        private static readonly MethodInfo CommandInternalRefreshMethod;

        private static readonly HashSet<string> LoggedDiagnosticContexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _loggedMissingSendToClient;
        private static bool _loggedMissingBroadcast;
        private static bool _loggedMissingSendToServer;
        private static bool _loggedMissingRegister;
        private static bool _loggedMissingUnregister;
        private static bool _stubSimulationAutostartAttempted;

        private const int DefaultStubClientId = 1;

        internal enum ConnectionRegistrationResult
        {
            Failure,
            Registered,
            AlreadyRegistered
        }

        static CsmCompat()
        {
            CandidateAssemblies = EnumerateCandidateAssemblies()
                .Where(a => a != null)
                .Distinct()
                .ToArray();

            SendToClientMethod = ResolveSendToClient();
            SendToClientTarget = ResolveTarget(SendToClientMethod);
            SendToAllMethod = ResolveSendToAll();
            SendToAllTarget = ResolveTarget(SendToAllMethod);
            SendToClientsMethod = ResolveSendToClients();
            SendToClientsTarget = ResolveTarget(SendToClientsMethod);
            SendToServerMethod = ResolveSendToServer();
            SendToServerTarget = ResolveTarget(SendToServerMethod);

            foreach (var assembly in CandidateAssemblies)
            {
                foreach (var type in SafeGetTypes(assembly))
                {
                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                    foreach (var method in methods)
                    {
                        if (RegisterConnectionMethod == null && MatchesCandidateName(method, RegisterConnectionNames) && MatchesConnectionSignature(method))
                        {
                            RegisterConnectionMethod = method;
                            RegisterConnectionTarget = ResolveTarget(method);
                        }

                        if (UnregisterConnectionMethod == null && MatchesCandidateName(method, UnregisterConnectionNames) && MatchesConnectionSignature(method))
                        {
                            UnregisterConnectionMethod = method;
                            UnregisterConnectionTarget = ResolveTarget(method) ?? RegisterConnectionTarget;
                        }
                    }

                    if (RegisterConnectionMethod != null && UnregisterConnectionMethod != null)
                        break;
                }

                if (RegisterConnectionMethod != null && UnregisterConnectionMethod != null)
                    break;
            }

            Log.Debug("CSM compat initialised. SendToClient={0}; SendToAll={1}; Register={2}; Unregister={3}",
                DescribeMethod(SendToClientMethod),
                DescribeMethod(SendToAllMethod),
                DescribeMethod(RegisterConnectionMethod),
                DescribeMethod(UnregisterConnectionMethod));

            if (SendToServerMethod != null)
                Log.Debug("CSM compat detected SendToServer hook: {0}", DescribeMethod(SendToServerMethod));

            foreach (var assembly in CandidateAssemblies)
            {
                if (assembly == null)
                    continue;

                try
                {
                    foreach (var type in SafeGetTypes(assembly))
                    {
                        if (ModSupportType == null && string.Equals(type.FullName, "CSM.Mods.ModSupport", StringComparison.Ordinal))
                        {
                            ModSupportType = type;
                            ModSupportInstanceProperty = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                            ConnectedModsProperty = type.GetProperty("ConnectedMods", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        }

                        if (CommandInternalType == null && string.Equals(type.FullName, "CSM.Commands.CommandInternal", StringComparison.Ordinal))
                        {
                            CommandInternalType = type;
                            CommandInternalInstanceField = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                            CommandInternalRefreshMethod = type.GetMethod("RefreshModel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                        }

                        if (ModSupportType != null && CommandInternalType != null)
                            break;
                    }
                }
                catch
                {
                    // ignored – handled in diagnostics later
                }

                if (ModSupportType != null && CommandInternalType != null)
                    break;
            }
        }

        private static IEnumerable<Assembly> EnumerateCandidateAssemblies()
        {
            yield return CommandType.Assembly;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly == null || assembly == CommandType.Assembly)
                    continue;

                AssemblyName name;
                try
                {
                    name = assembly.GetName();
                }
                catch
                {
                    continue;
                }

                var simpleName = name?.Name;
                if (string.IsNullOrEmpty(simpleName))
                    continue;

                if (string.Equals(simpleName, "CSM.API", StringComparison.OrdinalIgnoreCase) ||
                    simpleName.StartsWith("CSM.API.", StringComparison.OrdinalIgnoreCase))
                {
                    yield return assembly;
                }
            }
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null);
            }
            catch
            {
                return new Type[0];
            }
        }

        internal static bool IsServerInstance()
        {
            try
            {
                if (IsServerProperty != null && IsServerProperty.PropertyType == typeof(bool))
                {
                    var value = IsServerProperty.GetValue(null, null);
                    if (value is bool boolValue)
                        return boolValue;
                }

                if (IsServerField != null && IsServerField.FieldType == typeof(bool))
                {
                    var value = IsServerField.GetValue(null);
                    if (value is bool boolValue)
                        return boolValue;
                }

                if (RoleCheckMethod != null && RoleCheckMethod.ReturnType == typeof(bool) && RoleCheckMethod.GetParameters().Length == 0)
                {
                    var target = RoleCheckMethod.IsStatic ? null : GetSingletonInstance(RoleCheckMethod.DeclaringType);
                    var result = RoleCheckMethod.Invoke(target, null);
                    if (result is bool boolResult)
                        return boolResult;
                }

                if (CurrentRoleProperty != null)
                {
                    var value = CurrentRoleProperty.GetValue(null, null);
                    if (IsServerRoleValue(value))
                        return true;
                }

                if (RoleNameProperty != null)
                {
                    var value = RoleNameProperty.GetValue(null, null);
                    if (IsServerRoleValue(value))
                        return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to resolve CSM server role: {0}", ex);
            }

            return false;
        }

        internal static string DescribeCurrentRole()
        {
            try
            {
                if (CurrentRoleProperty != null)
                {
                    var value = CurrentRoleProperty.GetValue(null, null);
                    if (value != null)
                        return value.ToString();
                }

                if (RoleNameProperty != null)
                {
                    var value = RoleNameProperty.GetValue(null, null);
                    if (value != null)
                        return value.ToString();
                }

                if (IsServerProperty != null || IsServerField != null || RoleCheckMethod != null)
                    return IsServerInstance() ? "Server" : "Client";
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to describe current CSM role: {0}", ex);
            }

            return "unknown";
        }

        internal static int GetSenderId(CommandBase command)
        {
            try
            {
                if (SenderIdProperty != null)
                {
                    var value = SenderIdProperty.GetValue(null, null);
                    if (value != null)
                        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
                }

                if (SenderIdField != null)
                {
                    var value = SenderIdField.GetValue(null);
                    if (value != null)
                        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
                }

                if (SenderProperty != null)
                {
                    var sender = SenderProperty.GetValue(null, null);
                    if (sender != null)
                    {
                        var idProperty = sender.GetType().GetProperty("Id") ??
                                         sender.GetType().GetProperty("ClientId") ??
                                         sender.GetType().GetProperty("SenderId");
                        if (idProperty != null)
                        {
                            var value = idProperty.GetValue(sender, null);
                            if (value != null)
                                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
                        }
                    }
                }

                if (command != null)
                {
                    var cmdProperty = command.GetType().GetProperty("SenderId");
                    if (cmdProperty != null)
                    {
                        var value = cmdProperty.GetValue(command, null);
                        if (value != null)
                            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to resolve sender id: {0}", ex);
            }

            return -1;
        }

        internal static void SendToClient(int clientId, CommandBase command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            Log.Info("CSM send to client {0}: {1}", clientId, DescribeCommand(command));
            try
            {
                if (SendToClientMethod != null)
                {
                    var method = PrepareMethodForInvoke(SendToClientMethod, clientId, command);
                    if (method != null)
                    {
                        var parameters = method.GetParameters();
                        var args = BuildArguments(parameters, clientId, command);
                        var target = method.IsStatic ? SendToClientTarget : (SendToClientTarget ?? ResolveTarget(SendToClientMethod));
                        method.Invoke(target, args);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to invoke direct SendToClient for client {0}: {1}", clientId, ex);
            }

            try
            {
                if (SendToClientsMethod != null)
                {
                    var single = new[] { clientId };
                    var method = PrepareMethodForInvoke(SendToClientsMethod, single, command);
                    if (method != null)
                    {
                        var parameters = method.GetParameters();
                        var args = BuildArguments(parameters, single, command);
                        var target = method.IsStatic ? SendToClientsTarget : (SendToClientsTarget ?? ResolveTarget(SendToClientsMethod));
                        method.Invoke(target, args);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to invoke SendToClients fallback for client {0}: {1}", clientId, ex);
            }

            Log.Warn("No compatible send-to-client method available in CSM.API");
            if (!_loggedMissingSendToClient)
            {
                _loggedMissingSendToClient = true;
                LogDiagnostics("SendToClient unavailable", true);
            }
        }

        internal static void SendToAll(CommandBase command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            Log.Info("CSM broadcast: {0}", DescribeCommand(command));
            try
            {
                if (SendToAllMethod != null)
                {
                    var method = PrepareMethodForInvoke(SendToAllMethod, null, command);
                    if (method != null)
                    {
                        var parameters = method.GetParameters();
                        var args = BuildArguments(parameters, null, command);
                        var target = method.IsStatic ? SendToAllTarget : (SendToAllTarget ?? ResolveTarget(SendToAllMethod));
                        method.Invoke(target, args);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to broadcast command: {0}", ex);
                return;
            }

            Log.Warn("No compatible broadcast method available in CSM.API");
            if (!_loggedMissingBroadcast)
            {
                _loggedMissingBroadcast = true;
                LogDiagnostics("Broadcast unavailable", true);
            }
        }

        internal static void SendToServer(CommandBase command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            Log.Info("CSM send to server: {0}", DescribeCommand(command));

            if (SendToServerMethod != null)
            {
                try
                {
                    var method = PrepareMethodForInvoke(SendToServerMethod, null, command);
                    if (method != null)
                    {
                        var parameters = method.GetParameters();
                        var args = BuildArguments(parameters, null, command);
                        var target = method.IsStatic ? SendToServerTarget : (SendToServerTarget ?? ResolveTarget(SendToServerMethod));
                        method.Invoke(target, args);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("Failed to invoke SendToServer: {0}", ex);
                }
            }

            if (!_loggedMissingSendToServer)
            {
                _loggedMissingSendToServer = true;
                Log.Warn("Unable to send TM:PE request to server – CSM.API server hook missing");
                LogDiagnostics("SendToServer missing", true);
            }

            SendToAll(command);
        }

        private static string DescribeCommand(CommandBase command)
        {
            if (command == null)
                return "<null>";

            var type = command.GetType();
            var builder = new StringBuilder();
            builder.Append(type.Name);

            var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .OrderBy(p => p.Name, StringComparer.Ordinal);

            var first = true;
            foreach (var property in properties)
            {
                if (first)
                {
                    builder.Append(" {");
                    first = false;
                }
                else
                {
                    builder.Append(", ");
                }

                object value;
                try
                {
                    value = property.GetValue(command, null);
                }
                catch
                {
                    value = "<error>";
                }

                builder.Append(property.Name);
                builder.Append("=");
                builder.Append(FormatValue(value));
            }

            if (!first)
                builder.Append('}');

            return builder.ToString();
        }

        private static string FormatValue(object value)
        {
            switch (value)
            {
                case null:
                    return "<null>";
                case string s:
                    return '"' + s + '"';
                default:
                    if (value is IEnumerable enumerable && !(value is string))
                        return FormatEnumerable(enumerable);

                    if (value is IFormattable formattable)
                        return formattable.ToString(null, CultureInfo.InvariantCulture);

                    return value?.ToString() ?? string.Empty;
            }
        }

        private static string FormatEnumerable(IEnumerable enumerable)
        {
            var builder = new StringBuilder();
            builder.Append('[');

            var first = true;
            foreach (var item in enumerable)
            {
                if (!first)
                    builder.Append(", ");

                builder.Append(FormatValue(item));
                first = false;
            }

            builder.Append(']');
            return builder.ToString();
        }

        internal static ConnectionRegistrationResult RegisterConnection(Connection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            if (RegisterConnectionMethod == null)
            {
                var fallbackResult = RegisterViaModSupport(connection);
                if (fallbackResult != ConnectionRegistrationResult.Failure)
                    return fallbackResult;

                Log.Warn("Unable to register connection – CSM.API register hook missing");
                if (!_loggedMissingRegister)
                {
                    _loggedMissingRegister = true;
                    LogDiagnostics("Register hook missing", true);
                }
                return ConnectionRegistrationResult.Failure;
            }

            Log.Debug("Registering connection '{0}' via {1}", SafeName(connection), DescribeMethod(RegisterConnectionMethod));
            try
            {
                var method = PrepareMethodForInvoke(RegisterConnectionMethod, connection, null);
                if (method == null)
                {
                    Log.Warn("Failed to prepare register connection method for '{0}'", SafeName(connection));
                    return ConnectionRegistrationResult.Failure;
                }

                var parameters = method.GetParameters();
                var args = BuildArguments(parameters, connection, null);
                var target = method.IsStatic ? RegisterConnectionTarget : (RegisterConnectionTarget ?? ResolveTarget(RegisterConnectionMethod));
                method.Invoke(target, args);
                Log.Info("Registered connection '{0}' with CSM", SafeName(connection));
                return ConnectionRegistrationResult.Registered;
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to register connection: {0}", ex);
                return ConnectionRegistrationResult.Failure;
            }
        }

        internal static bool UnregisterConnection(Connection connection)
        {
            if (connection == null)
                return false;

            if (UnregisterConnectionMethod == null)
            {
                var fallbackResult = UnregisterViaModSupport(connection);
                if (fallbackResult)
                    return true;

                Log.Warn("Unable to unregister connection – CSM.API unregister hook missing");
                if (!_loggedMissingUnregister)
                {
                    _loggedMissingUnregister = true;
                    LogDiagnostics("Unregister hook missing", true);
                }
                return false;
            }

            try
            {
                var method = PrepareMethodForInvoke(UnregisterConnectionMethod, connection, null);
                if (method == null)
                {
                    Log.Warn("Failed to prepare unregister connection method for '{0}'", SafeName(connection));
                    return false;
                }

                var parameters = method.GetParameters();
                var args = BuildArguments(parameters, connection, null);
                var target = method.IsStatic ? UnregisterConnectionTarget : (UnregisterConnectionTarget ?? ResolveTarget(UnregisterConnectionMethod));
                method.Invoke(target, args);
                Log.Info("Unregistered connection '{0}' from CSM", SafeName(connection));
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to unregister connection: {0}", ex);
                return false;
            }
        }

        private static ConnectionRegistrationResult RegisterViaModSupport(Connection connection)
        {
            if (connection == null)
                return ConnectionRegistrationResult.Failure;

            if (ModSupportType == null || ModSupportInstanceProperty == null || ConnectedModsProperty == null)
                return ConnectionRegistrationResult.Failure;

            object modSupport;
            try
            {
                modSupport = ModSupportInstanceProperty.GetValue(null, null);
            }
            catch (Exception ex)
            {
                Log.Warn("Unable to resolve CSM.Mods.ModSupport instance: {0}", ex);
                return ConnectionRegistrationResult.Failure;
            }

            if (modSupport == null)
                return ConnectionRegistrationResult.Failure;

            object listObj;
            try
            {
                listObj = ConnectedModsProperty.GetValue(modSupport, null);
            }
            catch (Exception ex)
            {
                Log.Warn("Unable to access CSM.Mods.ModSupport.ConnectedMods: {0}", ex);
                return ConnectionRegistrationResult.Failure;
            }

            if (!(listObj is IEnumerable enumerable))
                return ConnectionRegistrationResult.Failure;

            foreach (var item in enumerable)
            {
                if (item == null)
                    continue;

                if (ReferenceEquals(item, connection) || item.GetType() == connection.GetType())
                {
                    Log.Info("Connection '{0}' already registered with CSM (detected via ModSupport).", SafeName(connection));
                    return ConnectionRegistrationResult.AlreadyRegistered;
                }
            }

            if (listObj is IList list)
            {
                list.Add(connection);
            }
            else
            {
                var addMethod = listObj.GetType().GetMethod("Add", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(object) }, null)
                                ?? listObj.GetType().GetMethod("Add", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (addMethod == null)
                    return ConnectionRegistrationResult.Failure;

                var parameters = addMethod.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(connection.GetType()))
                {
                    addMethod.Invoke(listObj, new object[] { connection });
                }
                else if (parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(typeof(Connection)))
                {
                    addMethod.Invoke(listObj, new object[] { connection });
                }
                else
                {
                    return ConnectionRegistrationResult.Failure;
                }
            }

            RefreshCommandModel();
            Log.Info("Registered connection '{0}' with CSM via ModSupport fallback.", SafeName(connection));
            return ConnectionRegistrationResult.Registered;
        }

        private static bool UnregisterViaModSupport(Connection connection)
        {
            if (connection == null)
                return false;

            if (ModSupportType == null || ModSupportInstanceProperty == null || ConnectedModsProperty == null)
                return false;

            object modSupport;
            try
            {
                modSupport = ModSupportInstanceProperty.GetValue(null, null);
            }
            catch (Exception ex)
            {
                Log.Warn("Unable to resolve CSM.Mods.ModSupport instance for unregister: {0}", ex);
                return false;
            }

            if (modSupport == null)
                return false;

            object listObj;
            try
            {
                listObj = ConnectedModsProperty.GetValue(modSupport, null);
            }
            catch (Exception ex)
            {
                Log.Warn("Unable to access CSM.Mods.ModSupport.ConnectedMods for unregister: {0}", ex);
                return false;
            }

            if (!(listObj is IList list))
                return false;

            if (!list.Contains(connection))
            {
                Log.Debug("Connection '{0}' not present in ModSupport list during unregister – skipping.", SafeName(connection));
                return true;
            }

            list.Remove(connection);
            RefreshCommandModel();
            Log.Info("Unregistered connection '{0}' from CSM via ModSupport fallback.", SafeName(connection));
            return true;
        }

        private static void RefreshCommandModel()
        {
            if (CommandInternalInstanceField == null || CommandInternalRefreshMethod == null)
                return;

            try
            {
                var instance = CommandInternalInstanceField.GetValue(null);
                if (instance != null)
                {
                    CommandInternalRefreshMethod.Invoke(instance, null);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to refresh CSM command model: {0}", ex);
            }
        }

        private static string SafeName(Connection connection)
        {
            if (connection == null)
                return "<null>";

            if (!string.IsNullOrEmpty(connection.Name))
                return connection.Name;

            return connection.GetType().FullName ?? connection.GetType().Name;
        }

        private static string DescribeMethod(MethodInfo method)
        {
            if (method == null)
                return "<missing method>";

            var declaring = method.DeclaringType?.FullName ?? method.DeclaringType?.Name ?? "<unknown type>";
            var parameters = method.GetParameters();
            var parameterTypes = string.Join(", ", parameters.Select(p => p.ParameterType.Name).ToArray());
            return declaring + "." + method.Name + "(" + parameterTypes + ")";
        }

        private static string DescribeMember(MemberInfo member)
        {
            if (member == null)
                return "<missing member>";

            var declaring = member.DeclaringType?.FullName ?? member.DeclaringType?.Name ?? "<unknown type>";
            return declaring + "." + member.Name;
        }

        internal static void LogDiagnostics(string context = null, bool force = false)
        {
            var key = context ?? string.Empty;
            if (!force && LoggedDiagnosticContexts.Contains(key))
                return;

            LoggedDiagnosticContexts.Add(key);

            var header = string.IsNullOrEmpty(context)
                ? "CSM compat diagnostics:"
                : $"CSM compat diagnostics ({context}):";
            Log.Info(header);
            Log.Info("  Command type: {0}", CommandType?.FullName ?? "<missing>");
            Log.Info("  RegisterConnection method: {0}", DescribeMethod(RegisterConnectionMethod));
            Log.Info("  UnregisterConnection method: {0}", DescribeMethod(UnregisterConnectionMethod));
            Log.Info("  SendToClient method: {0}", DescribeMethod(SendToClientMethod));
            Log.Info("  SendToClients method: {0}", DescribeMethod(SendToClientsMethod));
            Log.Info("  SendToAll method: {0}", DescribeMethod(SendToAllMethod));
            Log.Info("  SendToServer method: {0}", DescribeMethod(SendToServerMethod));
            Log.Info("  SimulateClientConnected method: {0}", DescribeMethod(SimulateClientConnectedMethod));
            Log.Info("  GetSimulatedClients method: {0}", DescribeMethod(GetSimulatedClientsMethod));
            Log.Info("  ModSupport type: {0}", ModSupportType?.FullName ?? "<missing>");
            Log.Info("  ModSupport.Instance property: {0}", DescribeMember(ModSupportInstanceProperty));
            Log.Info("  ModSupport.ConnectedMods property: {0}", DescribeMember(ConnectedModsProperty));
            Log.Info("  CommandInternal type: {0}", CommandInternalType?.FullName ?? "<missing>");
            Log.Info("  CommandInternal.Instance field: {0}", DescribeMember(CommandInternalInstanceField));
            Log.Info("  CommandInternal.RefreshModel method: {0}", DescribeMethod(CommandInternalRefreshMethod));

            var ignoreInstance = IgnoreHelperInstance == null
                ? "<missing>"
                : IgnoreHelperInstance.GetType().FullName ?? IgnoreHelperInstance.GetType().Name;
            Log.Info("  Ignore helper instance: {0}", ignoreInstance);
            Log.Info("  Ignore start method: {0}", DescribeMethod(IgnoreStartMethod));
            Log.Info("  Ignore end method: {0}", DescribeMethod(IgnoreEndMethod));

            Log.Info("  CurrentRole property: {0}", DescribeMember(CurrentRoleProperty));
            Log.Info("  RoleName property: {0}", DescribeMember(RoleNameProperty));
            Log.Info("  IsServer property: {0}", DescribeMember(IsServerProperty));
            Log.Info("  IsServer field: {0}", DescribeMember(IsServerField));
            Log.Info("  Role check method: {0}", DescribeMethod(RoleCheckMethod));
            Log.Info("  SenderId property: {0}", DescribeMember(SenderIdProperty));
            Log.Info("  SenderId field: {0}", DescribeMember(SenderIdField));
            Log.Info("  Sender property: {0}", DescribeMember(SenderProperty));
        }

        internal static void EnsureStubSimulationActive()
        {
            if (_stubSimulationAutostartAttempted)
                return;

            _stubSimulationAutostartAttempted = true;

            if (SimulateClientConnectedMethod == null || GetSimulatedClientsMethod == null)
            {
                Log.Debug("Stub simulation hooks unavailable – skipping autostart.");
                return;
            }

            try
            {
                if (HasSimulatedClients())
                {
                    Log.Debug("Stub simulation already has connected clients – skipping autostart.");
                    return;
                }

                var parameters = SimulateClientConnectedMethod.GetParameters();
                if (parameters.Length != 1)
                {
                    Log.Warn("SimulateClientConnected signature unexpected – skipping stub autostart: {0}", DescribeMethod(SimulateClientConnectedMethod));
                    return;
                }

                object clientArgument;
                try
                {
                    clientArgument = Convert.ChangeType(DefaultStubClientId, parameters[0].ParameterType, CultureInfo.InvariantCulture);
                }
                catch (Exception ex)
                {
                    Log.Warn("Unable to convert stub client id to '{0}': {1}", parameters[0].ParameterType, ex.Message);
                    return;
                }

                SimulateClientConnectedMethod.Invoke(null, new[] { clientArgument });
                Log.Info("Stub CSM simulation started automatically (clientId={0}).", clientArgument);
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to autostart stub simulation: {0}", ex);
            }
        }

        internal static void ResetStubSimulationState()
        {
            _stubSimulationAutostartAttempted = false;
        }

        private static bool HasSimulatedClients()
        {
            try
            {
                var result = GetSimulatedClientsMethod.Invoke(null, null);
                if (result is IEnumerable enumerable)
                {
                    foreach (var _ in enumerable)
                        return true;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Unable to inspect simulated clients: {0}", ex.Message);
            }

            return false;
        }

        private static bool IsServerRoleValue(object value)
        {
            if (value == null)
                return false;

            try
            {
                if (value is bool boolValue)
                    return boolValue;

                var type = value.GetType();

                if (type.IsEnum)
                {
                    var name = Enum.GetName(type, value);
                    if (!string.IsNullOrEmpty(name))
                    {
                        name = name.ToLowerInvariant();
                        if (name == "server" || name == "host")
                            return true;
                    }

                    try
                    {
                        var numeric = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                        if (numeric == 2)
                            return true;
                    }
                    catch
                    {
                        // ignore conversion errors
                    }
                }

                if (value is string str)
                {
                    str = str.Trim().ToLowerInvariant();
                    return str == "server" || str == "host";
                }

                var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(text))
                {
                    text = text.Trim().ToLowerInvariant();
                    if (text == "server" || text == "host")
                        return true;
                }
            }
            catch
            {
                // ignore conversion issues – fall back to false
            }

            return false;
        }

        private static MethodInfo ResolveRoleCheckMethod(Type type)
        {
            if (type == null)
                return null;

            var candidateNames = new[]
            {
                "IsServer",
                "IsCurrentRoleServer",
                "IsHost",
                "IsCurrentRoleHost",
                "IsServerInstance",
                "IsHostInstance"
            };

            foreach (var name in candidateNames)
            {
                var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (method != null && method.ReturnType == typeof(bool) && method.GetParameters().Length == 0)
                    return method;
            }

            return null;
        }

        internal static IDisposable StartIgnore()
        {
            if (IgnoreHelperInstance == null || IgnoreStartMethod == null || IgnoreEndMethod == null)
                return new DummyScope();

            try
            {
                var instance = IgnoreStartMethod.IsStatic ? null : IgnoreHelperInstance;
                IgnoreStartMethod.Invoke(instance, null);
                return new IgnoreScope(IgnoreHelperInstance, IgnoreEndMethod);
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to start ignore scope: {0}", ex);
                return new DummyScope();
            }
        }

        private static object[] BuildArguments(ParameterInfo[] parameters, object recipient, CommandBase command)
        {
            var args = new object[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                if (parameter.ParameterType.IsInstanceOfType(command))
                {
                    args[i] = command;
                }
                else if (recipient != null && parameter.ParameterType.IsInstanceOfType(recipient))
                {
                    args[i] = recipient;
                }
                else if (recipient != null && TryConvertRecipient(recipient, parameter.ParameterType, out var converted))
                {
                    args[i] = converted;
                }
                else if (TryInferArgument(parameter, recipient, out var inferred))
                {
                    args[i] = inferred;
                }
                else if (TryGetDefaultValue(parameter, out var defaultValue))
                {
                    args[i] = defaultValue;
                }
                else if (parameter.ParameterType.IsValueType)
                {
                    args[i] = Activator.CreateInstance(parameter.ParameterType);
                }
                else
                {
                    args[i] = null;
                }
            }

            return args;
        }

        private static bool TryConvertRecipient(object value, Type targetType, out object converted)
        {
            try
            {
                if (targetType.IsInstanceOfType(value))
                {
                    converted = value;
                    return true;
                }

                if (targetType == typeof(Type))
                {
                    if (value is Type typeValue)
                    {
                        converted = typeValue;
                        return true;
                    }

                    converted = value?.GetType();
                    return converted != null;
                }

                if (targetType == typeof(Assembly))
                {
                    if (value is Assembly assembly)
                    {
                        converted = assembly;
                        return true;
                    }

                    converted = value?.GetType()?.Assembly;
                    return converted != null;
                }

                if (targetType == typeof(int) || targetType == typeof(long) || targetType == typeof(uint) || targetType == typeof(ushort) || targetType == typeof(byte))
                {
                    converted = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
                    return true;
                }

                if (targetType.IsArray)
                {
                    var elementType = targetType.GetElementType();
                    if (value is System.Collections.IEnumerable enumerable && elementType != null)
                    {
                        var list = new List<object>();
                        foreach (var item in enumerable)
                        {
                            if (item == null)
                            {
                                list.Add(null);
                                continue;
                            }

                            if (elementType.IsInstanceOfType(item))
                            {
                                list.Add(item);
                                continue;
                            }

                            list.Add(Convert.ChangeType(item, elementType, CultureInfo.InvariantCulture));
                        }

                        var array = Array.CreateInstance(elementType, list.Count);
                        for (var i = 0; i < list.Count; i++)
                            array.SetValue(list[i], i);

                        converted = array;
                        return true;
                    }
                }

                if (value is Connection connection)
                {
                    if (targetType.IsInstanceOfType(connection.CommandAssemblies))
                    {
                        converted = connection.CommandAssemblies;
                        return true;
                    }

                    if (typeof(IEnumerable<Assembly>).IsAssignableFrom(targetType))
                    {
                        converted = connection.CommandAssemblies;
                        return true;
                    }
                }
            }
            catch
            {
                // ignore conversion failures
            }

            converted = null;
            return false;
        }

        private static bool TryGetDefaultValue(ParameterInfo parameter, out object value)
        {
            if ((parameter.Attributes & ParameterAttributes.HasDefault) == ParameterAttributes.HasDefault)
            {
                value = parameter.DefaultValue;
                return true;
            }

            var defaultValueAttribute = parameter
                .GetCustomAttributes(typeof(DefaultValueAttribute), false)
                .OfType<DefaultValueAttribute>()
                .FirstOrDefault();

            if (defaultValueAttribute != null)
            {
                value = defaultValueAttribute.Value;
                return true;
            }

            value = null;
            return false;
        }

        private static MethodInfo ResolveIgnoreMethod(string name)
        {
            if (IgnoreHelperType == null)
                return null;

            return IgnoreHelperType
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.Name == name)
                .Where(m => m.GetParameters().Length == 0)
                .OrderByDescending(m => m.IsStatic)
                .FirstOrDefault();
        }

        private static MethodInfo ResolveSendToClient()
        {
            return ResolveSendMethod(
                SendToClientMethodNames,
                new[] { "send" },
                new[] { "client", "peer", "target" });
        }

        private static MethodInfo ResolveSendToClients()
        {
            return ResolveSendMethod(
                SendToClientsMethodNames,
                new[] { "send" },
                new[] { "clients", "peers", "targets" });
        }

        private static MethodInfo ResolveSendToAll()
        {
            return ResolveSendMethod(
                SendToAllMethodNames,
                new[] { "send", "broadcast" },
                new string[0]);
        }

        private static MethodInfo ResolveSendToServer()
        {
            return ResolveSendMethod(
                SendToServerMethodNames,
                new[] { "send" },
                new[] { "server", "host" });
        }

        private static bool AcceptsCommandParameter(MethodInfo method)
        {
            var parameters = method.GetParameters();
            foreach (var parameter in parameters)
            {
                if (IsCommandType(parameter.ParameterType))
                    return true;

                if (parameter.ParameterType.IsGenericParameter && SatisfiesCommandConstraints(parameter.ParameterType))
                    return true;
            }

            foreach (var genericParameter in method.GetGenericArguments())
            {
                if (SatisfiesCommandConstraints(genericParameter))
                    return true;
            }

            return false;
        }

        private static bool SatisfiesCommandConstraints(Type genericParameter)
        {
            if (!genericParameter.IsGenericParameter)
                return IsCommandType(genericParameter);

            var constraints = genericParameter.GetGenericParameterConstraints();
            if (constraints.Length == 0)
                return false;

            return constraints.Any(IsCommandType);
        }

        private static bool IsConnectionType(Type type)
        {
            return IsConnectionType(type, new HashSet<Type>());
        }

        private static bool IsConnectionType(Type type, HashSet<Type> visited)
        {
            if (type == null)
                return false;

            if (!visited.Add(type))
                return false;

            if (typeof(Connection).IsAssignableFrom(type))
                return true;

            if (IsConnectionTypeByName(type))
                return true;

            if (type.IsGenericParameter)
            {
                var constraints = type.GetGenericParameterConstraints();
                if (constraints.Length == 0)
                    return false;

                return constraints.Any(c => IsConnectionType(c, visited));
            }

            var baseType = type.BaseType;
            if (baseType != null && IsConnectionType(baseType, visited))
                return true;

            foreach (var iface in type.GetInterfaces())
            {
                if (IsConnectionType(iface, visited))
                    return true;
            }

            return false;
        }

        private static bool IsConnectionTypeByName(Type type)
        {
            if (type == null)
                return false;

            var name = type.Name;
            if (string.IsNullOrEmpty(name))
                return false;

            if (type.Namespace == null || type.Namespace.IndexOf("CSM.API", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            if (string.Equals(name, "Connection", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "ConnectionBase", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "IConnection", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return name.EndsWith("Connection", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCommandType(Type type)
        {
            return IsCommandType(type, new HashSet<Type>());
        }

        private static bool IsCommandType(Type type, HashSet<Type> visited)
        {
            if (type == null)
                return false;

            if (!visited.Add(type))
                return false;

            if (typeof(CommandBase).IsAssignableFrom(type))
                return true;

            if (IsCommandTypeByName(type))
                return true;

            if (type.IsGenericParameter)
            {
                var constraints = type.GetGenericParameterConstraints();
                if (constraints.Length == 0)
                    return false;

                return constraints.Any(c => IsCommandType(c, visited));
            }

            var baseType = type.BaseType;
            if (baseType != null && IsCommandType(baseType, visited))
                return true;

            foreach (var iface in type.GetInterfaces())
            {
                if (IsCommandType(iface, visited))
                    return true;
            }

            return false;
        }

        private static bool IsCommandTypeByName(Type type)
        {
            if (type == null)
                return false;

            var name = type.Name;
            if (string.IsNullOrEmpty(name))
                return false;

            var namespaceName = type.Namespace ?? string.Empty;

            if (namespaceName.IndexOf("CSM.API", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (string.Equals(name, "Command", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "CommandBase", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "ICommand", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith("Command", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (namespaceName.IndexOf("ProtoBuf", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (string.Equals(name, "IExtensible", StringComparison.OrdinalIgnoreCase) ||
                    name.IndexOf("Command", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static MethodInfo PrepareMethodForInvoke(MethodInfo method, object recipient, CommandBase command)
        {
            if (method == null)
                return null;

            if (!method.IsGenericMethodDefinition)
                return method;

            var prepared = TryMakeGenericMethod(method, recipient, command);
            if (prepared == null)
                Log.Warn("Unable to resolve generic method for invocation: {0}", DescribeMethod(method));

            return prepared;
        }

        private static MethodInfo TryMakeGenericMethod(MethodInfo method, object recipient, CommandBase command)
        {
            var genericArguments = method.GetGenericArguments();
            if (genericArguments.Length == 0)
                return method;

            var candidates = new List<Type>();
            if (recipient != null)
            {
                if (recipient is Type typeRecipient)
                    candidates.Add(typeRecipient);
                else
                    candidates.Add(recipient.GetType());
            }

            if (command != null)
                candidates.Add(command.GetType());

            var resolved = new Type[genericArguments.Length];
            for (var i = 0; i < genericArguments.Length; i++)
            {
                var parameter = genericArguments[i];
                Type match = null;

                foreach (var candidate in candidates)
                {
                    if (candidate != null && SatisfiesGenericConstraints(parameter, candidate))
                    {
                        match = candidate;
                        break;
                    }
                }

                if (match == null)
                {
                    var constraints = parameter.GetGenericParameterConstraints();
                    match = constraints.FirstOrDefault(c => !c.IsGenericParameter && SatisfiesGenericConstraints(parameter, c));
                }

                if (match == null)
                    return null;

                resolved[i] = match;
            }

            return method.MakeGenericMethod(resolved);
        }

        private static bool SatisfiesGenericConstraints(Type genericParameter, Type candidate)
        {
            if (candidate == null)
                return false;

            if (!genericParameter.IsGenericParameter)
                return genericParameter.IsAssignableFrom(candidate);

            var attributes = genericParameter.GenericParameterAttributes;
            if ((attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0 && candidate.IsValueType)
                return false;

            if ((attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
            {
                if (!candidate.IsValueType || Nullable.GetUnderlyingType(candidate) != null)
                    return false;
            }

            if ((attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0)
            {
                if (candidate.IsAbstract || candidate.GetConstructor(Type.EmptyTypes) == null)
                    return false;
            }

            foreach (var constraint in genericParameter.GetGenericParameterConstraints())
            {
                if (!constraint.IsAssignableFrom(candidate))
                    return false;
            }

            return true;
        }

        private static MethodInfo ResolveSendMethod(
            IEnumerable<string> methodNames,
            IEnumerable<string> mandatoryKeywords,
            IEnumerable<string> optionalKeywords)
        {
            var candidates = new HashSet<string>(methodNames ?? new string[0], StringComparer.OrdinalIgnoreCase);
            var methods = CandidateAssemblies
                .SelectMany(SafeGetTypes)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                .Where(AcceptsCommandParameter)
                .ToList();

            var match = methods
                .Where(m => MatchesCandidateName(m, candidates))
                .OrderByDescending(m => m.IsStatic)
                .ThenByDescending(m => m.GetParameters().Length)
                .FirstOrDefault();

            if (match != null)
                return match;

            var mandatory = (mandatoryKeywords ?? new string[0])
                .Select(k => k?.Trim())
                .Where(k => !string.IsNullOrEmpty(k))
                .ToArray();
            var optional = (optionalKeywords ?? new string[0])
                .Select(k => k?.Trim())
                .Where(k => !string.IsNullOrEmpty(k))
                .ToArray();

            IEnumerable<MethodInfo> FilterByKeywords(IEnumerable<MethodInfo> source)
            {
                foreach (var method in source)
                {
                    if (!ContainsAllKeywords(method, mandatory))
                        continue;

                    if (optional.Length == 0 || ContainsAnyKeyword(method, optional))
                        yield return method;
                }
            }

            return FilterByKeywords(methods)
                .OrderByDescending(m => m.IsStatic)
                .ThenByDescending(m => m.GetParameters().Length)
                .FirstOrDefault();
        }

        private static bool ContainsAllKeywords(MethodInfo method, string[] keywords)
        {
            if (keywords == null || keywords.Length == 0)
                return true;

            return keywords.All(keyword => method.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool ContainsAnyKeyword(MethodInfo method, string[] keywords)
        {
            if (keywords == null || keywords.Length == 0)
                return true;

            return keywords.Any(keyword => method.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool TryInferArgument(ParameterInfo parameter, object recipient, out object value)
        {
            if (recipient is Connection connection)
            {
                if (parameter.ParameterType == typeof(string))
                {
                    var name = connection.Name;
                    if (string.IsNullOrEmpty(name) || name.Trim().Length == 0)
                        name = connection.GetType().FullName ?? connection.GetType().Name;
                    value = name;
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static bool MatchesCandidateName(MethodInfo method, IEnumerable<string> candidateNames)
        {
            if (method == null)
                return false;

            if (candidateNames == null)
                return false;

            foreach (var name in candidateNames)
            {
                if (string.IsNullOrEmpty(name))
                    continue;

                if (string.Equals(method.Name, name, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (method.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static bool MatchesCandidateName(MethodInfo method, HashSet<string> candidateNames)
        {
            if (method == null)
                return false;

            if (candidateNames == null || candidateNames.Count == 0)
                return false;

            if (candidateNames.Contains(method.Name))
                return true;

            foreach (var name in candidateNames)
            {
                if (method.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static bool MatchesConnectionSignature(MethodInfo method)
        {
            var parameters = method.GetParameters();
            if (parameters.Length == 0)
                return false;

            foreach (var parameter in parameters)
            {
                if (IsConnectionType(parameter.ParameterType))
                    return true;

                if (parameter.ParameterType.IsGenericParameter && SatisfiesConnectionConstraints(parameter.ParameterType))
                    return true;

                if (AcceptsConnectionMetadata(parameter.ParameterType))
                    return true;
            }

            if (method.IsGenericMethodDefinition)
            {
                foreach (var genericParameter in method.GetGenericArguments())
                {
                    if (SatisfiesConnectionConstraints(genericParameter))
                        return true;
                }
            }

            return false;
        }

        private static bool SatisfiesConnectionConstraints(Type type)
        {
            if (!type.IsGenericParameter)
                return IsConnectionType(type) || AcceptsConnectionMetadata(type);

            var constraints = type.GetGenericParameterConstraints();
            if (constraints.Length == 0)
                return false;

            return constraints.Any(t => IsConnectionType(t) || AcceptsConnectionMetadata(t));
        }

        private static bool AcceptsConnectionMetadata(Type parameterType)
        {
            if (parameterType == null)
                return false;

            if (parameterType == typeof(Type) || parameterType == typeof(string) || parameterType == typeof(Assembly))
                return true;

            if (parameterType.IsGenericParameter)
                return false;

            if (typeof(IEnumerable<Assembly>).IsAssignableFrom(parameterType))
                return true;

            if (parameterType.IsArray)
            {
                var elementType = parameterType.GetElementType();
                if (elementType != null && AcceptsConnectionMetadata(elementType))
                    return true;
            }

            if (parameterType.IsGenericType)
            {
                var definition = parameterType.GetGenericTypeDefinition();
                var fullName = definition.FullName;
                if (string.Equals(fullName, "System.Collections.Generic.IEnumerable`1", StringComparison.Ordinal) ||
                    string.Equals(fullName, "System.Collections.Generic.ICollection`1", StringComparison.Ordinal) ||
                    string.Equals(fullName, "System.Collections.Generic.IList`1", StringComparison.Ordinal) ||
                    string.Equals(fullName, "System.Collections.Generic.List`1", StringComparison.Ordinal) ||
                    string.Equals(fullName, "System.Collections.Generic.IReadOnlyCollection`1", StringComparison.Ordinal) ||
                    string.Equals(fullName, "System.Collections.Generic.IReadOnlyList`1", StringComparison.Ordinal))
                {
                    var elementType = parameterType.GetGenericArguments()[0];
                    if (AcceptsConnectionMetadata(elementType))
                        return true;
                }
            }

            return false;
        }

        private static object ResolveTarget(MethodInfo method)
        {
            if (method == null || method.IsStatic)
                return null;

            return GetSingletonInstance(method.DeclaringType);
        }

        private static object GetSingletonInstance(Type type)
        {
            if (type == null)
                return null;

            var bindingFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase;

            return type.GetProperty("Instance", bindingFlags)?.GetValue(null, null) ??
                   type.GetProperty("Current", bindingFlags)?.GetValue(null, null) ??
                   type.GetProperty("Singleton", bindingFlags)?.GetValue(null, null) ??
                   type.GetProperty("Api", bindingFlags)?.GetValue(null, null) ??
                   type.GetProperty("API", bindingFlags)?.GetValue(null, null) ??
                   type.GetField("Instance", bindingFlags)?.GetValue(null) ??
                   type.GetField("Current", bindingFlags)?.GetValue(null) ??
                   type.GetField("Singleton", bindingFlags)?.GetValue(null) ??
                   type.GetField("Api", bindingFlags)?.GetValue(null) ??
                   type.GetField("API", bindingFlags)?.GetValue(null);
        }

        private readonly struct DummyScope : IDisposable
        {
            public void Dispose() { }
        }

        private sealed class IgnoreScope : IDisposable
        {
            private readonly object _instance;
            private readonly MethodInfo _endMethod;
            private bool _disposed;

            internal IgnoreScope(object instance, MethodInfo endMethod)
            {
                _instance = instance;
                _endMethod = endMethod;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                try
                {
                    var target = _endMethod.IsStatic ? null : _instance;
                    _endMethod.Invoke(target, null);
                }
                catch (Exception ex)
                {
                    Log.Warn("Failed to end ignore scope: {0}", ex);
                }

                _disposed = true;
            }
        }
    }
}
