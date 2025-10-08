using System;
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

        private static readonly MethodInfo SendToClientMethod;
        private static readonly MethodInfo SendToAllMethod;
        private static readonly MethodInfo SendToClientsMethod;
        private static readonly MethodInfo RegisterConnectionMethod;
        private static readonly MethodInfo UnregisterConnectionMethod;
        private static readonly object ConnectionRegistrarInstance;

        static CsmCompat()
        {
            SendToClientMethod = ResolveSendToClient();
            SendToAllMethod = ResolveSendToAll();
            SendToClientsMethod = ResolveSendToClients();

            var assembly = CommandType.Assembly;
            foreach (var type in assembly.GetTypes())
            {
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                foreach (var method in methods)
                {
                    if (RegisterConnectionMethod == null && method.Name == "RegisterConnection" && MatchesConnectionSignature(method))
                    {
                        RegisterConnectionMethod = method;
                        ConnectionRegistrarInstance = method.IsStatic ? null : GetSingletonInstance(type);
                    }

                    if (UnregisterConnectionMethod == null && method.Name == "UnregisterConnection" && MatchesConnectionSignature(method))
                    {
                        UnregisterConnectionMethod = method;
                        if (!method.IsStatic && ConnectionRegistrarInstance == null)
                            ConnectionRegistrarInstance = GetSingletonInstance(type);
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
                    var parameters = SendToClientMethod.GetParameters();
                    var args = BuildArguments(parameters, clientId, command);
                    SendToClientMethod.Invoke(null, args);
                    return;
                }

                if (SendToClientsMethod != null)
                {
                    var parameters = SendToClientsMethod.GetParameters();
                    var args = BuildArguments(parameters, new[] { clientId }, command);
                    SendToClientsMethod.Invoke(null, args);
                    return;
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
                    var parameters = SendToAllMethod.GetParameters();
                    var args = BuildArguments(parameters, null, command);
                    SendToAllMethod.Invoke(null, args);
                    return;
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
                var target = RegisterConnectionMethod.IsStatic ? null : ConnectionRegistrarInstance;
                RegisterConnectionMethod.Invoke(target, new object[] { connection });
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
                var target = UnregisterConnectionMethod.IsStatic ? null : ConnectionRegistrarInstance;
                UnregisterConnectionMethod.Invoke(target, new object[] { connection });
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
            return CommandType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "SendToClient")
                .OrderByDescending(m => m.GetParameters().Length)
                .FirstOrDefault(m => m.GetParameters().Any(p => typeof(CommandBase).IsAssignableFrom(p.ParameterType)));
        }

        private static MethodInfo ResolveSendToClients()
        {
            return CommandType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "SendToClients")
                .OrderByDescending(m => m.GetParameters().Length)
                .FirstOrDefault(m => m.GetParameters().Any(p => typeof(CommandBase).IsAssignableFrom(p.ParameterType)));
        }

        private static MethodInfo ResolveSendToAll()
        {
            return CommandType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "SendToAll")
                .OrderByDescending(m => m.GetParameters().Length)
                .FirstOrDefault(m => m.GetParameters().Any(p => typeof(CommandBase).IsAssignableFrom(p.ParameterType)));
        }

        private static bool MatchesConnectionSignature(MethodInfo method)
        {
            var parameters = method.GetParameters();
            return parameters.Length == 1 && typeof(Connection).IsAssignableFrom(parameters[0].ParameterType);
        }

        private static object GetSingletonInstance(Type type)
        {
            return type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null) ??
                   type.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null) ??
                   type.GetProperty("Singleton", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null) ??
                   type.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ??
                   type.GetField("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ??
                   type.GetField("Singleton", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
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
