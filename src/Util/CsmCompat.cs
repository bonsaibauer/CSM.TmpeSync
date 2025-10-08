using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CSM.API;
using CSM.API.Commands;

namespace CSM.TmpeSync.Util
{
    internal static class CsmCompat
    {
        private static readonly Type CommandType = typeof(Command);
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

        private static readonly HashSet<string> RegisterConnectionNames = new HashSet<string>(new[]
        {
            "RegisterConnection",
            "Register",
            "AddConnection",
            "Add",
            "AttachConnection",
            "Attach",
            "SubscribeConnection",
            "Subscribe"
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
            "Unsubscribe"
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

        private static readonly MethodInfo SendToClientMethod;
        private static readonly object SendToClientTarget;
        private static readonly MethodInfo SendToAllMethod;
        private static readonly object SendToAllTarget;
        private static readonly MethodInfo SendToClientsMethod;
        private static readonly object SendToClientsTarget;
        private static readonly MethodInfo RegisterConnectionMethod;
        private static readonly object RegisterConnectionTarget;
        private static readonly MethodInfo UnregisterConnectionMethod;
        private static readonly object UnregisterConnectionTarget;

        static CsmCompat()
        {
            SendToClientMethod = ResolveSendToClient();
            SendToClientTarget = ResolveTarget(SendToClientMethod);
            SendToAllMethod = ResolveSendToAll();
            SendToAllTarget = ResolveTarget(SendToAllMethod);
            SendToClientsMethod = ResolveSendToClients();
            SendToClientsTarget = ResolveTarget(SendToClientsMethod);

            var assembly = CommandType.Assembly;
            foreach (var type in assembly.GetTypes())
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

            Log.Debug("CSM compat initialised. SendToClient={0}; SendToAll={1}; Register={2}; Unregister={3}",
                DescribeMethod(SendToClientMethod),
                DescribeMethod(SendToAllMethod),
                DescribeMethod(RegisterConnectionMethod),
                DescribeMethod(UnregisterConnectionMethod));
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

            Log.Debug("CSM send to client {0}: {1}", clientId, command.GetType().FullName);
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

                if (SendToClientsMethod != null)
                {
                    var method = PrepareMethodForInvoke(SendToClientsMethod, new[] { clientId }, command);
                    if (method != null)
                    {
                        var parameters = method.GetParameters();
                        var args = BuildArguments(parameters, new[] { clientId }, command);
                        var target = method.IsStatic ? SendToClientsTarget : (SendToClientsTarget ?? ResolveTarget(SendToClientsMethod));
                        method.Invoke(target, args);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to send command to client {0}: {1}", clientId, ex);
                return;
            }

            Log.Warn("No compatible send-to-client method available in CSM.API");
        }

        internal static void SendToAll(CommandBase command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            Log.Debug("CSM broadcast: {0}", command.GetType().FullName);
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
        }

        internal static bool RegisterConnection(Connection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            if (RegisterConnectionMethod == null)
            {
                Log.Warn("Unable to register connection – CSM.API register hook missing");
                return false;
            }

            Log.Debug("Registering connection '{0}' via {1}", SafeName(connection), DescribeMethod(RegisterConnectionMethod));
            try
            {
                var method = PrepareMethodForInvoke(RegisterConnectionMethod, connection, null);
                if (method == null)
                {
                    Log.Warn("Failed to prepare register connection method for '{0}'", SafeName(connection));
                    return false;
                }

                var parameters = method.GetParameters();
                var args = BuildArguments(parameters, connection, null);
                var target = method.IsStatic ? RegisterConnectionTarget : (RegisterConnectionTarget ?? ResolveTarget(RegisterConnectionMethod));
                method.Invoke(target, args);
                Log.Info("Registered connection '{0}' with CSM", SafeName(connection));
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to register connection: {0}", ex);
                return false;
            }
        }

        internal static bool UnregisterConnection(Connection connection)
        {
            if (connection == null)
                return false;

            if (UnregisterConnectionMethod == null)
            {
                Log.Warn("Unable to unregister connection – CSM.API unregister hook missing");
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
                        var list = enumerable.Cast<object>().Select(item => Convert.ChangeType(item, elementType, CultureInfo.InvariantCulture)).ToArray();
                        var array = Array.CreateInstance(elementType, list.Length);
                        for (var i = 0; i < list.Length; i++)
                        {
                            array.SetValue(list[i], i);
                        }

                        converted = array;
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
            return ResolveSendMethod(SendToClientMethodNames);
        }

        private static MethodInfo ResolveSendToClients()
        {
            return ResolveSendMethod(SendToClientsMethodNames);
        }

        private static MethodInfo ResolveSendToAll()
        {
            return ResolveSendMethod(SendToAllMethodNames);
        }

        private static bool AcceptsCommandParameter(MethodInfo method)
        {
            var parameters = method.GetParameters();
            foreach (var parameter in parameters)
            {
                if (typeof(CommandBase).IsAssignableFrom(parameter.ParameterType))
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
                return typeof(CommandBase).IsAssignableFrom(genericParameter);

            var constraints = genericParameter.GetGenericParameterConstraints();
            if (constraints.Length == 0)
                return false;

            return constraints.Any(c => typeof(CommandBase).IsAssignableFrom(c));
        }

        private static bool IsConnectionType(Type type)
        {
            if (type == null)
                return false;

            if (typeof(Connection).IsAssignableFrom(type))
                return true;

            if (type.IsGenericParameter)
            {
                var constraints = type.GetGenericParameterConstraints();
                if (constraints.Length == 0)
                    return false;

                return constraints.Any(c => typeof(Connection).IsAssignableFrom(c));
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

        private static MethodInfo ResolveSendMethod(IEnumerable<string> methodNames)
        {
            var candidates = new HashSet<string>(methodNames ?? new string[0], StringComparer.OrdinalIgnoreCase);
            return CommandType.Assembly
                .GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                .Where(m => MatchesCandidateName(m, candidates))
                .OrderByDescending(m => m.IsStatic)
                .ThenByDescending(m => m.GetParameters().Length)
                .FirstOrDefault(AcceptsCommandParameter);
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
            if (parameters.Length > 0 && IsConnectionType(parameters[0].ParameterType))
                return true;

            if (method.IsGenericMethodDefinition)
            {
                foreach (var genericParameter in method.GetGenericArguments())
                {
                    if (IsConnectionType(genericParameter))
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
