namespace Tf2DemoSalvage.GameSystems;

/// <summary>A subsystem the engine tells about levels, as Valve's <c>IGameSystem</c> is told.</summary>
/// <remarks>
/// **Valve's own contract, `game/shared/igamesystem.h`.** The engine keeps a list of registered
/// systems and calls each of them at the level boundaries —
/// <c>LevelInitPreEntityAllSystems( pMapName )</c> walks the list rather than one function reaching
/// into six. Adopting the shape means a new system is added by implementing this and registering it,
/// not by finding every place a level is loaded.
///
/// **No method takes a payload, and that is Valve's design rather than a simplification.**
/// <c>virtual void LevelInitPreEntity() = 0;</c> takes nothing; a system pulls what it needs from
/// globals. That single fact is why this project can exist at all: an interface carrying a
/// <c>LoadedMap</c> would have to be visible to Audio, which does not reference Scene.
///
/// **Every method has a default, which is `CBaseGameSystem`'s role.** Valve declares the interface
/// pure and supplies a base class whose overrides are all empty, so a system implements only the
/// hooks it cares about. C# default interface implementations do the same thing without a base
/// class, which suits a codebase that prefers composition — see `CLAUDE.md`.
///
/// **The pre/post split is not decoration.** Valve separates the phases because ordering matters:
/// entities are created between <c>LevelInitPreEntity</c> and <c>LevelInitPostEntity</c>, and
/// deleted between the two shutdown halves. A system that needs entities to exist says so by
/// choosing the later hook rather than by being called in the right order by luck.
///
/// **What is deliberately NOT here:** <c>OnSave</c>, <c>OnRestore</c> and
/// <c>LevelShutdownPreClearSteamAPIContext</c>. A demo viewer has no save games and no Steam API
/// context, so copying them would be cargo rather than parity. <c>Init</c>/<c>PostInit</c>/
/// <c>Shutdown</c> are omitted for the same reason: our systems are constructed and disposed by the
/// language, where Valve's are long-lived singletons the engine starts and stops.
/// </remarks>
public interface IGameSystem
{
    /// <summary>What this system is called, for the log.</summary>
    /// <remarks>
    /// Valve's <c>virtual char const *Name() = 0;</c>, and it earns its place the same way: a
    /// walk over a list of systems that reports a failure needs to say WHICH one, and a type name
    /// read by reflection is not what a person reading a log wants to see.
    /// </remarks>
    public string Name { get; }

    // **Valve has `virtual bool IsPerFrame() = 0;` here and this does not — a DEPARTURE, flagged
    // rather than assumed, and cheap to reverse if the owner would rather have it.**
    //
    // The split it guards is real and is kept: `CBaseGameSystem` makes the per-frame methods PRIVATE
    // specifically to stop a non-per-frame system implementing them, the header saying so in as many
    // words — "Prevent anyone derived from CBaseGameSystem from implementing these, they need to
    // derive from CBaseGameSystemPerFrame below!!!". Here that split is two interfaces.
    //
    // What is dropped is only the ANSWER being stored. In the SDK a system returns it, so a class
    // can derive from `CBaseGameSystemPerFrame` and still answer false: two facts that can disagree.
    // In C# the question is a type test at the call site — `system is IGameSystemPerFrame` — which
    // cannot disagree with itself. Valve needs the flag because that codebase avoids RTTI; keeping
    // it here would be a second spelling of something the type system already answers, which is
    // what both analyzers said (CA1033 on the explicit implementation, S3060 on the type test).

    /// <summary>Called when a level is loaded, before its entities exist.</summary>
    public void LevelInitPreEntity()
    {
    }

    /// <summary>Called when a level is loaded, once its entities have been created.</summary>
    public void LevelInitPostEntity()
    {
    }

    /// <summary>Called when a level is being torn down, while its entities still exist.</summary>
    public void LevelShutdownPreEntity()
    {
    }

    /// <summary>Called when a level is being torn down, after its entities have gone.</summary>
    public void LevelShutdownPostEntity()
    {
    }
}

/// <summary>A game system that also runs every frame.</summary>
/// <remarks>
/// **Valve's <c>IGameSystemPerFrame</c>, and specifically its CLIENT_DLL half.** The header declares
/// two different sets behind <c>#ifdef CLIENT_DLL</c>: the client gets
/// <c>PreRender()</c>, <c>Update( float frametime )</c> and <c>PostRender()</c>, while the server
/// gets <c>FrameUpdatePreEntityThink</c>, <c>FrameUpdatePostEntityThink</c> and
/// <c>PreClientUpdate</c>. This viewer is a client, so it takes the client set — copying the server
/// names would be a shape borrowed from the wrong side of the same header.
/// </remarks>
public interface IGameSystemPerFrame : IGameSystem
{
    /// <summary>Called before the frame is rendered.</summary>
    public void PreRender()
    {
    }

    /// <summary>Called each frame.</summary>
    /// <param name="frameSeconds">How long the last frame took, Valve's <c>frametime</c>.</param>
    public void Update(float frameSeconds)
    {
    }

    /// <summary>Called after the frame is rendered.</summary>
    public void PostRender()
    {
    }
}
