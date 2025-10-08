#if !GAME
namespace ICities
{
    public interface IThreading
    {
    }

    public abstract class ThreadingExtensionBase
    {
        public virtual void OnCreated(IThreading threading) { }
        public virtual void OnReleased() { }
        public virtual void OnUpdate(float realTimeDelta, float simulationTimeDelta) { }
    }
}
#endif
