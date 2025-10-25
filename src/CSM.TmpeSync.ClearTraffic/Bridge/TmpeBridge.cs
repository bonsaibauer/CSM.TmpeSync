using System;
using CSM.API.Commands;

namespace CSM.TmpeSync.ClearTraffic.Bridge
{
    public static class TmpeBridge
    {
        private static Func<CommandBase> _broadcastFactory;
        private static Func<CommandBase> _requestFactory;
        public static void SetClearTrafficFactories(Func<CommandBase> broadcastFactory, Func<CommandBase> requestFactory)
        {
            _broadcastFactory = broadcastFactory;
            _requestFactory = requestFactory;
        }

        public static bool ClearTraffic()
        {
            return UtilityAdapter.ClearTraffic();
        }

        internal static void HandleClearTrafficTriggered()
        {
            // If running as server, broadcast the applied action; otherwise request the server to clear.
            if (CsmBridge.IsServerInstance())
            {
                var cmd = _broadcastFactory?.Invoke();
                if (cmd != null)
                    CsmBridge.SendToAll(cmd);
                return;
            }

            var request = _requestFactory?.Invoke();
            if (request != null)
                CsmBridge.SendToServer(request);
        }
    }
}
