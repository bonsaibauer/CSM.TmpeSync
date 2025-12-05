using System;
using System.Collections;
using System.Reflection;
using CSM.API.Commands;
using CSM.API.Helpers;
using CSM.API.Networking;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.Services
{
    internal static class CsmBridge
    {
        internal static void SendToAll(CommandBase command)
        {
            if (command == null) return;
            try { Command.SendToAll?.Invoke(command); } catch { }
        }

        internal static void SendToServer(CommandBase command)
        {
            if (command == null) return;
            try { Command.SendToServer?.Invoke(command); } catch { }
        }

        internal static void SendToClient(int clientId, CommandBase command)
        {
            if (command == null) return;
            // CSM.API does not always expose a public SendToClient delegate; fall back to reflection.
            try
            {
                // Try delegate first if available in this runtime
                var del = typeof(Command).GetField("SendToClient", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as Action<int, CommandBase>;
                if (del != null)
                {
                    del(clientId, command);
                    return;
                }
            }
            catch { }

            TrySendToClientInternal(clientId, command);
        }

        private static bool TrySendToClientInternal(int clientId, CommandBase command)
        {
            try
            {
                var cmdInternalType = Type.GetType("CSM.Commands.CommandInternal");
                var mmType = Type.GetType("CSM.Networking.MultiplayerManager");
                if (cmdInternalType == null || mmType == null)
                    return false;

                var cmdInternal = cmdInternalType.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (cmdInternal == null)
                    return false;

                var mmInstance = mmType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
                if (mmInstance == null)
                    return false;

                var currentServer = mmType.GetProperty("CurrentServer", BindingFlags.Public | BindingFlags.Instance)?.GetValue(mmInstance, null);
                if (currentServer == null)
                    return false;

                var players = currentServer.GetType().GetProperty("ConnectedPlayers", BindingFlags.Public | BindingFlags.Instance)?.GetValue(currentServer, null) as IDictionary;
                if (players == null || !players.Contains(clientId))
                    return false;

                var player = players[clientId];
                if (player == null)
                    return false;

                // Find CommandInternal.SendToClient(player, command [, reliable])
                bool hasReliability = false;
                var sendMethod = FindSendToClientMethod(cmdInternalType, out hasReliability);
                if (sendMethod == null)
                    return false;

                object[] args = hasReliability ? new object[] { player, command, true } : new object[] { player, command };
                sendMethod.Invoke(cmdInternal, args);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static MethodInfo FindSendToClientMethod(Type cmdInternalType, out bool hasReliability)
        {
            hasReliability = false;
            if (cmdInternalType == null) return null;
            foreach (var m in cmdInternalType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != "SendToClient") continue;
                var p = m.GetParameters();
                if (p.Length == 2)
                {
                    hasReliability = false; return m;
                }
                if (p.Length == 3 && p[2].ParameterType == typeof(bool))
                {
                    hasReliability = true; return m;
                }
            }
            return null;
        }

        internal static int GetSenderId(CommandBase command) => command?.SenderId ?? -1;
        internal static bool IsServerInstance() => Command.CurrentRole == MultiplayerRole.Server;
        internal static string DescribeCurrentRole() => Command.CurrentRole.ToString();

        internal static int TryGetClientId(Player player)
        {
            try
            {
                var netPeer = player?.GetType().GetProperty("NetPeer")?.GetValue(player, null);
                var idObj = netPeer?.GetType().GetProperty("Id")?.GetValue(netPeer, null);
                if (idObj is int id)
                    return id;

                var username = player?.GetType().GetProperty("Username")?.GetValue(player, null) as string;
                if (!string.IsNullOrEmpty(username))
                {
                    var mmType = Type.GetType("CSM.Networking.MultiplayerManager");
                    var mmInstance = mmType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
                    var currentServer = mmType?.GetProperty("CurrentServer", BindingFlags.Public | BindingFlags.Instance)?.GetValue(mmInstance, null);
                    var players = currentServer?.GetType().GetProperty("ConnectedPlayers", BindingFlags.Public | BindingFlags.Instance)?.GetValue(currentServer, null) as IDictionary;
                    if (players != null)
                    {
                        foreach (DictionaryEntry entry in players)
                        {
                            var value = entry.Value;
                            var uname = value?.GetType().GetProperty("Username")?.GetValue(value, null) as string;
                            if (string.Equals(uname, username, StringComparison.Ordinal))
                            {
                                if (entry.Key is int keyId)
                                    return keyId;
                            }
                        }
                    }
                }
            }
            catch
            {
                // ignored
            }

            return -1;
        }

        internal static IDisposable StartIgnore()
        {
            try
            {
                var helper = IgnoreHelper.Instance;
                if (helper == null) return DummyScope.Instance;
                helper.StartIgnore();
                return new IgnoreScope(helper);
            }
            catch { return DummyScope.Instance; }
        }

        private sealed class IgnoreScope : IDisposable
        {
            private readonly IgnoreHelper _helper;
            internal IgnoreScope(IgnoreHelper helper) { _helper = helper; }
            public void Dispose() { try { _helper.EndIgnore(); } catch { } }
        }

        private sealed class DummyScope : IDisposable
        {
            internal static readonly DummyScope Instance = new DummyScope();
            public void Dispose() { }
        }
    }
}
