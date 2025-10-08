// Auto-generated stub implementations for building outside of the game environment.
#if !GAME
using System;
using System.Collections.Generic;
using System.Reflection;

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
        public static int SenderId { get; set; }

        public static void SendToClient(int clientId, Commands.CommandBase command) { }
        public static void SendToAll(Commands.CommandBase command) { }
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
        public static void RegisterConnection(Connection connection) { }
        public static void UnregisterConnection(Connection connection) { }
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
