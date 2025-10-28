using CSM.API.Commands;
using CSM.TmpeSync.Messages.System;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.Handlers.System
{
    public class ModCompatibilityResultHandler : CommandHandler<ModCompatibilityResult>
    {
        protected override void Handle(ModCompatibilityResult cmd)
        {
            CompatibilityGuard.HandleResult(cmd);
        }
    }
}
