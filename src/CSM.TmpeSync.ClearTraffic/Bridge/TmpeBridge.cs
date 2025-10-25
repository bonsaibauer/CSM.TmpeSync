using System;
using CSM.API.Commands;
using CSM.TmpeSync.TmpeBridge;

namespace CSM.TmpeSync.ClearTraffic.Bridge
{
    public static class TmpeBridge
    {
        public static void SetClearTrafficFactories(Func<CommandBase> broadcastFactory, Func<CommandBase> requestFactory)
        {
            TmpeBridgeChangeDispatcher.ClearTrafficBroadcastFactory = broadcastFactory;
            TmpeBridgeChangeDispatcher.ClearTrafficRequestFactory = requestFactory;
        }

        public static bool ClearTraffic()
        {
            return TmpeBridgeAdapter.ClearTraffic();
        }
    }
}
