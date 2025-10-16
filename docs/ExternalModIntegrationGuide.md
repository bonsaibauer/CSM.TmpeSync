# Guide: building an external add-on for Cities: Skylines Multiplayer

This guide summarises how to build a standalone add-on similar to
`SampleExternalMod` from the official
[CSM repository](https://github.com/CitiesSkylinesMultiplayer/CSM). It uses
**CSM.TmpeSync** as the reference implementation: the add-on lives outside the
main CSM project and integrates solely through the public `CSM.API`. The steps
below help you avoid common pitfalls (missing hooks, wrong dependencies) and make
sure your module is ready for multiplayer use.

## 1. Adopt the project structure

* Create a classic .NET Framework 3.5 class library (for example with
  `dotnet new classlib -f net35`).
* Reuse the build approach from `CSM.TmpeSync.csproj`: always reference the real
  game assemblies, Harmony and the CSM API so builds match the runtime
  environment.【F:src/CSM.TmpeSync/CSM.TmpeSync.csproj†L1-L198】
* List your source files explicitly via `<Compile>` elements to decouple the
  build from the default folder layout – CSM.TmpeSync does this for all
  modules (Mod, Util, Net, Snapshot, …).【F:src/CSM.TmpeSync/CSM.TmpeSync.csproj†L118-L190】

## 2. Implement the `IUserMod` entry point

Create a `MyUserMod` (or your own name) implementing `ICities.IUserMod`. The
reference in CSM.TmpeSync provides the basics:

* During activation check whether CSM and Harmony are enabled
  (`Deps.GetMissingDependencies()`). If they are missing, disable the mod
  immediately to avoid invalid connections.【F:src/Mod/MyUserMod.cs†L16-L36】【F:src/Util/Deps.cs†L1-L142】
* Instantiate your CSM connection (`new TmpeSyncConnection()`) and register it via
  the compatibility layer (`CsmCompat.RegisterConnection`). If registration fails,
  keep the mod passive.【F:src/Mod/MyUserMod.cs†L24-L44】【F:src/Util/CsmCompat.cs†L136-L210】
* Unregister the connection on deactivation (`CsmCompat.UnregisterConnection`) so
  CSM does not retain dead handlers.【F:src/Mod/MyUserMod.cs†L39-L58】

This flow matches the expectations set by `SampleExternalMod`: CSM loads your
add-on like any other mod and you enable the multiplayer link only after all
requirements are met.

## 3. Derive the connection from `CSM.API.Connection`

Create a class deriving from `CSM.API.Connection` that describes your
communication channel. In CSM.TmpeSync this is `TmpeSyncConnection`:

* Choose a descriptive `Name`, set `Enabled = true` and wire up `ModClass` so CSM
  can log the connection owner.【F:src/Mod/TmpeSyncConnection.cs†L4-L13】
* Register all assemblies containing network commands (`CommandAssemblies.Add(...)`).
  CSM reflects these assemblies to discover your `[ProtoContract]` commands and
  handlers.【F:src/Mod/TmpeSyncConnection.cs†L11-L12】【F:src/Net/Contracts/Requests/SetSpeedLimitRequest.cs†L1-L10】
* Override `RegisterHandlers`/`UnregisterHandlers` if you need additional
  initialisation. Pure attribute-based handlers do not require extra code.【F:src/Mod/TmpeSyncConnection.cs†L12-L14】

Once `CsmCompat.RegisterConnection` succeeds, CSM sets up your handlers and routes
messages through this channel automatically.

## 4. Define network commands and handlers

* Model requests/responses as classes inheriting from `CSM.API.Commands.CommandBase`
  and decorate them with `[ProtoContract]`/`[ProtoMember]` so CSM can serialise
  them.【F:src/Net/Contracts/Requests/SetSpeedLimitRequest.cs†L1-L10】
* Implement handlers by deriving from `CommandHandler<T>`. For example
  `SetSpeedLimitRequestHandler` checks the sender role (`CsmCompat.IsServerInstance()`),
  validates the data and calls your gameplay logic (TM:PE in this case). Afterwards
  it broadcasts the confirmed result to all clients (`CsmCompat.SendToAll(...)`).【F:src/Net/Handlers/SetSpeedLimitRequestHandler.cs†L1-L49】
* Use helpers such as `NetUtil.RunOnSimulation` or `EntityLocks.AcquireLane` to
  execute actions on the simulation thread. Reuse the pattern if you need to touch
  in-game managers.【F:src/Net/Handlers/SetSpeedLimitRequestHandler.cs†L20-L45】

This satisfies the core requirement from `SampleExternalMod`: every change is
serialised, validated on the host and then broadcast to all participants.

## 5. Validate dependencies and diagnostics

* `CsmCompat.LogDiagnostics` lists every hook (SendToClient, SendToAll,
  Register/Unregister) during activation. Call it in your `OnEnabled` to verify that
the current CSM build exports the expected API surface.【F:src/Mod/MyUserMod.cs†L34-L37】【F:src/Util/CsmCompat.cs†L71-L134】
* Consult the log reference to understand messages for missing hooks or role
  information and how to resolve them. Frequent issues are missing
  `SendToClient`/`RegisterConnection` methods in the loaded `CSM.API` or outdated
  builds.【F:docs/LogReference.md†L1-L65】

## 6. Test workflow

1. **Development build** (`dotnet build -c Debug`): compiles against the real
   assemblies but keeps debugging symbols for inspection.
2. **Release build** (`dotnet build -c Release`): produces the artefacts you ship
   and matches the runtime environment. The build script copies the output into
   the configured mods folder.【F:README.md†L24-L98】
3. **Multiplayer test**: connect at least one client to the CSM server and verify
   that your commands (for example speed limit changes) replicate between all
   participants.【F:README.md†L102-L164】

Following this workflow covers every step required by `SampleExternalMod`: an
independent mod that registers with CSM at runtime, exposes its own network
commands and processes them in a host-authoritative way. Adhering to these
structures keeps your add-on maintainable outside of the main CSM repository.
