using System;
using CSM.API;

namespace CSM.API.Commands
{
    public static class Command
    {
        public static Action<CommandBase> SendToAll, SendToServer, SendToClients;
        public static Action<int, CommandBase> SendToClient;
        public static Func<Type, CommandHandler> GetCommandHandler;
        public static Func<Connection, bool> RegisterConnection, UnregisterConnection;
        public static Func<Connection[]> GetRegisteredConnections;

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
            SendToClient = sendToClient;
            GetCommandHandler = getCommandHandler;
            RegisterConnection = registerConnection;
            UnregisterConnection = unregisterConnection;
            GetRegisteredConnections = getRegisteredConnections;
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
