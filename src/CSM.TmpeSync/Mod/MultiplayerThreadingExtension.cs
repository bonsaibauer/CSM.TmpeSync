using ICities;

using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Mod
{
    public class MultiplayerThreadingExtension : ThreadingExtensionBase
    {
        public override void OnUpdate(float realTimeDelta, float simulationTimeDelta)
        {
            LockRegistry.Tick();
        }

        public override void OnReleased()
        {
            LockRegistry.Reset();
        }
    }
}
