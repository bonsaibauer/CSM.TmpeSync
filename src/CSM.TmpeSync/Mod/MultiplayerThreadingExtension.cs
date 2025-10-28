using ICities;

using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.Mod
{
    public class MultiplayerThreadingExtension : ThreadingExtensionBase
    {
        public override void OnUpdate(float realTimeDelta, float simulationTimeDelta)
        {
            CompatibilityGuard.Tick();
            LockRegistry.Tick();
        }

        public override void OnReleased()
        {
            LockRegistry.Reset();
            CompatibilityGuard.Shutdown();
        }
    }
}
