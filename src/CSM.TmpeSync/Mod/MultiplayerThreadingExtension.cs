using ICities;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;
using CSM.TmpeSync.Bridge;

namespace CSM.TmpeSync.Mod
{
    public class MultiplayerThreadingExtension : ThreadingExtensionBase
    {
        public override void OnUpdate(float realTimeDelta, float simulationTimeDelta)
        {
            CsmBridgeMultiplayerObserver.Update();
            LockRegistry.Tick();
        }

        public override void OnReleased()
        {
            CsmBridgeMultiplayerObserver.Reset();
            LockRegistry.Reset();
        }
    }
}
