#if !GAME
namespace ICities
{
    public interface IUserMod
    {
        string Name { get; }
        string Description { get; }
        void OnEnabled();
        void OnDisabled();
    }
}
#endif
