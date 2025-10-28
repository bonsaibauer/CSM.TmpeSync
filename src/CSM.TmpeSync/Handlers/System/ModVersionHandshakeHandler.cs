using CSM.API.Commands;
using CSM.TmpeSync.Messages.System;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.Handlers.System
{
    public class ModVersionHandshakeHandler : CommandHandler<ModVersionHandshake>
    {
        protected override void Handle(ModVersionHandshake cmd)
        {
            CompatibilityGuard.HandleHandshake(cmd);
        }
    }
}
