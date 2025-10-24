using ICities;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Mod
{
    public class MultiplayerThreadingExtension : ThreadingExtensionBase
    {
        public override void OnUpdate(float realTimeDelta, float simulationTimeDelta)
        {
            MultiplayerStateObserver.Update();
            LockRegistry.Tick();
            TimedTrafficLightOptionGuard.Update();
        }

        public override void OnReleased()
        {
            MultiplayerStateObserver.Reset();
            DeferredApply.Reset();
            LockRegistry.Reset();
        }
    }
}
