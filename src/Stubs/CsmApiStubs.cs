// Auto-generated stub implementations for building outside of the game environment.
#if !GAME
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using CSM.TmpeSync.Util;

namespace CSM.API
{
    public enum MultiplayerRole
    {
        None,
        Client,
        Server
    }

    public static class Command
    {
        public static MultiplayerRole CurrentRole { get; set; }

        public static string Role => CurrentRole.ToString();

        public static string RoleName => CurrentRole.ToString();

        public static bool IsServer => CurrentRole == MultiplayerRole.Server;

        public static bool IsClient => CurrentRole == MultiplayerRole.Client;

        public static int SenderId { get; set; }

        public static int CurrentSenderId => SenderId;

        public static object Sender { get; set; }

        public static object CurrentSender => Sender;

        public static void RegisterConnection(Connection connection)
        {
            Helper.RegisterConnection(connection);
        }

        public static void UnregisterConnection(Connection connection)
        {
            Helper.UnregisterConnection(connection);
        }

        public static void SendToClient(int clientId, Commands.CommandBase command)
        {
            Helper.SendToClient(clientId, command);
        }

        public static void SendToClients(IEnumerable<int> clientIds, Commands.CommandBase command)
        {
            if (clientIds == null)
            {
                Helper.SendToClients(Array.Empty<int>(), command);
                return;
            }

            Helper.SendToClients(clientIds, command);
        }

        public static void SendToClients(int[] clientIds, Commands.CommandBase command)
        {
            Helper.SendToClients(clientIds ?? Array.Empty<int>(), command);
        }

        public static void SendToAll(Commands.CommandBase command)
        {
            Helper.SendToAll(command);
        }

        public static void SendToEveryone(Commands.CommandBase command)
        {
            Helper.SendToAll(command);
        }

        public static void Broadcast(Commands.CommandBase command)
        {
            Helper.SendToAll(command);
        }

        public static void BroadcastToAll(Commands.CommandBase command)
        {
            Helper.SendToAll(command);
        }

        public static void SimulateClientConnected(int clientId)
        {
            Helper.SimulateClientConnected(clientId);
        }

        public static void SimulateClientDisconnected(int clientId)
        {
            Helper.SimulateClientDisconnected(clientId);
        }

        public static IReadOnlyCollection<int> GetSimulatedClients()
        {
            return Helper.GetSimulatedClients();
        }

        public static IReadOnlyList<SimulatedCommandLogEntry> DumpSimulatedCommandLog()
        {
            return Helper.DumpSimulatedCommandLog();
        }
    }

    public sealed class IgnoreHelper
    {
        public static IgnoreHelper Instance { get; } = new IgnoreHelper();

        private IgnoreHelper() { }

        public void StartIgnore() { }
        public void EndIgnore() { }
    }

    public abstract class Connection
    {
        protected Connection()
        {
            CommandAssemblies = new List<Assembly>();
        }

        public string Name { get; set; }
        public bool Enabled { get; set; }
        public Type ModClass { get; set; }
        public List<Assembly> CommandAssemblies { get; }

        public abstract void RegisterHandlers();
        public abstract void UnregisterHandlers();
    }

    public static class Helper
    {
        private const int CommandLogLimit = 200;

        private static readonly object Sync = new object();
        private static readonly List<Connection> Connections = new List<Connection>();
        private static readonly HashSet<int> Clients = new HashSet<int>();
        private static readonly List<PendingCommand> PendingCommands = new List<PendingCommand>();
        private static readonly List<SimulatedCommandLogEntry> CommandLog = new List<SimulatedCommandLogEntry>();

        public static void RegisterConnection(Connection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            lock (Sync)
            {
                if (!Connections.Contains(connection))
                {
                    Connections.Add(connection);
                    connection.Enabled = true;
                    Log.Info("[CSM.API Stub] Registered connection '{0}'. Commands will be logged locally until a client connects.", DescribeConnection(connection));
                }
            }

            connection.RegisterHandlers();
        }

        public static void UnregisterConnection(Connection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            lock (Sync)
            {
                if (Connections.Remove(connection))
                    Log.Info("[CSM.API Stub] Unregistered connection '{0}'.", DescribeConnection(connection));
            }

            connection.UnregisterHandlers();
        }

        internal static void SendToClient(int clientId, Commands.CommandBase command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            lock (Sync)
            {
                if (Clients.Contains(clientId))
                {
                    LogCommand(false, clientId, command, SimulatedCommandStatus.Delivered, "Delivering command to simulated client {0}: {1}", clientId);
                }
                else
                {
                    PendingCommands.Add(new PendingCommand(clientId, command));
                    LogCommand(false, clientId, command, SimulatedCommandStatus.PendingClient, "Queued command for simulated client {0} (waiting for connection): {1}", clientId);
                }
            }
        }

        internal static void SendToClients(IEnumerable<int> clientIds, Commands.CommandBase command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            var targets = clientIds?.ToArray() ?? Array.Empty<int>();
            if (targets.Length == 0)
            {
                SendToAll(command);
                return;
            }

            foreach (var clientId in targets)
                SendToClient(clientId, command);
        }

        internal static void SendToAll(Commands.CommandBase command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            lock (Sync)
            {
                if (Clients.Count == 0)
                {
                    PendingCommands.Add(new PendingCommand(null, command));
                    LogCommand(true, null, command, SimulatedCommandStatus.PendingClient, "Queued broadcast (no simulated clients): {0}");
                    return;
                }

                foreach (var clientId in Clients)
                    LogCommand(true, clientId, command, SimulatedCommandStatus.Delivered, "Broadcast to simulated client {0}: {1}", clientId);
            }
        }

        public static void SimulateClientConnected(int clientId)
        {
            lock (Sync)
            {
                if (!Clients.Add(clientId))
                {
                    Log.Info("[CSM.API Stub] Simulated client {0} already connected.", clientId);
                    return;
                }

                Log.Info("[CSM.API Stub] Simulated client {0} connected. Replaying queued commands if available.", clientId);

                var replayed = 0;
                for (var i = PendingCommands.Count - 1; i >= 0; i--)
                {
                    var pending = PendingCommands[i];
                    if (pending.TargetClientId == null || pending.TargetClientId == clientId)
                    {
                        PendingCommands.RemoveAt(i);
                        replayed++;
                        LogCommand(pending.IsBroadcast, clientId, pending.Command, SimulatedCommandStatus.Delivered, "Replaying queued command -> simulated client {0}: {1}", clientId);
                    }
                }

                Log.Info("[CSM.API Stub] Replayed {0} queued command(s) for simulated client {1}.", replayed, clientId);
            }
        }

        public static void SimulateClientDisconnected(int clientId)
        {
            lock (Sync)
            {
                if (Clients.Remove(clientId))
                    Log.Info("[CSM.API Stub] Simulated client {0} disconnected.", clientId);
                else
                    Log.Info("[CSM.API Stub] Simulated client {0} was not connected.", clientId);
            }
        }

        public static IReadOnlyCollection<int> GetSimulatedClients()
        {
            lock (Sync)
            {
                return Clients.ToArray();
            }
        }

        public static IReadOnlyList<SimulatedCommandLogEntry> DumpSimulatedCommandLog()
        {
            lock (Sync)
            {
                return CommandLog.Select(entry => entry.Clone()).ToArray();
            }
        }

        private static void LogCommand(bool isBroadcast, int? targetClientId, Commands.CommandBase command, SimulatedCommandStatus status, string messageFormat, params object[] extraArgs)
        {
            var description = DescribeCommand(command);
            var args = extraArgs?.ToList() ?? new List<object>();
            args.Add(description);

            Log.Info("[CSM.API Stub] " + messageFormat, args.ToArray());

            CommandLog.Add(new SimulatedCommandLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                IsBroadcast = isBroadcast,
                TargetClientId = targetClientId,
                CommandName = command?.GetType().FullName ?? "<null>",
                PayloadDescription = description,
                Status = status
            });

            if (CommandLog.Count > CommandLogLimit)
                CommandLog.RemoveRange(0, CommandLog.Count - CommandLogLimit);
        }

        private static string DescribeConnection(Connection connection)
        {
            if (connection == null)
                return "<null>";

            if (!string.IsNullOrWhiteSpace(connection.Name))
                return connection.Name;

            return connection.GetType().FullName ?? connection.ToString();
        }

        private static string DescribeCommand(Commands.CommandBase command)
        {
            if (command == null)
                return "<null>";

            var type = command.GetType();
            var builder = new StringBuilder();
            builder.Append(type.Name);
            var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.CanRead)
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
                builder.Append("}");

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
                case IEnumerable enumerable when !(value is string):
                    return "[" + string.Join(", ", enumerable.Cast<object>().Select(FormatValue)) + "]";
                case IFormattable formattable:
                    return formattable.ToString(null, CultureInfo.InvariantCulture);
                default:
                    return value.ToString();
            }
        }

        private sealed class PendingCommand
        {
            internal PendingCommand(int? targetClientId, Commands.CommandBase command)
            {
                TargetClientId = targetClientId;
                Command = command;
                IsBroadcast = !targetClientId.HasValue;
            }

            internal int? TargetClientId { get; }
            internal Commands.CommandBase Command { get; }
            internal bool IsBroadcast { get; }
        }
    }

    public sealed class SimulatedCommandLogEntry
    {
        public DateTime TimestampUtc { get; set; }
        public bool IsBroadcast { get; set; }
        public int? TargetClientId { get; set; }
        public string CommandName { get; set; }
        public string PayloadDescription { get; set; }
        public SimulatedCommandStatus Status { get; set; }

        internal SimulatedCommandLogEntry Clone()
        {
            return new SimulatedCommandLogEntry
            {
                TimestampUtc = TimestampUtc,
                IsBroadcast = IsBroadcast,
                TargetClientId = TargetClientId,
                CommandName = CommandName,
                PayloadDescription = PayloadDescription,
                Status = Status
            };
        }
    }

    public enum SimulatedCommandStatus
    {
        PendingClient,
        Delivered
    }
}

namespace CSM.API.Helpers
{
    public sealed class IgnoreHelper
    {
        public static IgnoreHelper Instance { get; } = new IgnoreHelper();

        private IgnoreHelper() { }

        public void StartIgnore() { }
        public void EndIgnore() { }
    }
}

namespace CSM.API.Commands
{
    public abstract class CommandBase
    {
    }

    public abstract class CommandHandler<TCommand> where TCommand : CommandBase
    {
        protected abstract void Handle(TCommand command);

        public void Execute(TCommand command)
        {
            Handle(command);
        }
    }
}
#endif
