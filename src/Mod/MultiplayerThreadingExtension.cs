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
            TmpeToolAvailability.Tick(MultiplayerStateObserver.ShouldRestrictTools);
        }

        public override void OnReleased()
        {
            TmpeToolAvailability.Reset();
            MultiplayerStateObserver.Reset();
        }
    }
}
