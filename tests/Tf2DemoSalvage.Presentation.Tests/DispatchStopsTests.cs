using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>When an entity's dispatched particle effect stops — Valve's two messages, not a window.</summary>
/// <remarks>
/// <code>
/// // particle_parse.cpp:500-519  StopParticleEffects( pEntity ) → DispatchEffect( "ParticleEffectStop", data )
/// // c_particle_system.cpp:269   ParticleEffectStopCallback: pEnt->ParticleProp()->StopEmission()   — every effect it owns
/// // c_particle_system.cpp:193   ParticleEffectCallback, PARTICLE_DISPATCH_RESET_PARTICLES: StopEmission() before Create
/// </code>
/// An effect dispatched from no entity is nobody's to stop: it ends by itself (`CNewParticleEffect::Create( NULL, … )`).
/// </remarks>
public sealed class DispatchStopsTests
{
    private const int Effect = 1;
    private const int Stop = 2;

    [Test]
    public void Compute_AStopOnTheSameEntity_StopsItThere()
    {
        int?[] stops = DispatchStops.Compute(
            [FromEntity(100, entity: 7), At(300, Stop, entity: 8), At(500, Stop, entity: 7)], NameOf);

        stops[0].ShouldBe(500, "the stop on another entity is not its own");
    }

    [Test]
    public void Compute_ALaterResetDispatchOnTheSameEntity_StopsTheEarlierOne()
    {
        int?[] stops = DispatchStops.Compute(
            [FromEntity(100, entity: 7), FromEntity(200, entity: 7, reset: true)], NameOf);

        stops[0].ShouldBe(200);
        stops[1].ShouldBeNull("the reset stops what came before it, not itself");
    }

    [Test]
    public void Compute_AnEffectFromNoEntity_IsNeverStopped()
    {
        int?[] stops = DispatchStops.Compute(
            [At(100, Effect, entity: 7, flags: 0), At(200, Stop, entity: 7)], NameOf);

        stops[0].ShouldBeNull();
    }

    [Test]
    public void Compute_ADispatchAfterAStop_IsNotStoppedByIt()
    {
        int?[] stops = DispatchStops.Compute([At(100, Stop, entity: 7), FromEntity(200, entity: 7)], NameOf);

        stops[1].ShouldBeNull();
    }

    [Test]
    public void Compute_TwoStops_TheFirstStopsIt()
    {
        int?[] stops = DispatchStops.Compute(
            [FromEntity(100, entity: 7), At(300, Stop, entity: 7), At(400, Stop, entity: 7)], NameOf);

        stops[0].ShouldBe(300);
    }

    /// <remarks>`PARTICLE_DISPATCH_RESET_PARTICLES` is read only inside the FROM_ENTITY branch (c_particle_system.cpp:189-196).</remarks>
    [Test]
    public void Compute_AResetWithoutFromEntity_StopsNothing()
    {
        int?[] stops = DispatchStops.Compute(
            [FromEntity(100, entity: 7), At(200, Effect, entity: 7, flags: 2)], NameOf);

        stops[0].ShouldBeNull();
    }

    /// <remarks>Effects fire in order within a tick: a stop sent after the effect, on its tick, stops it there.</remarks>
    [Test]
    public void Compute_AStopOnTheSameTickAfterIt_StopsItAtThatTick()
    {
        int?[] stops = DispatchStops.Compute([FromEntity(100, entity: 7), At(100, Stop, entity: 7)], NameOf);

        stops[0].ShouldBe(100);
    }

    /// <remarks>
    /// **The owner deleted ends it, and its index used again is another entity**: a stop sent to the index after the delete
    /// belongs to the new one. Deleted at 250, the stop at 400 is not this effect's.
    /// </remarks>
    [Test]
    public void Compute_TheOwnerDeletedBeforeAStopToItsIndex_EndsAtTheDelete()
    {
        int?[] stops = DispatchStops.Compute(
            [FromEntity(100, entity: 7), At(400, Stop, entity: 7)],
            NameOf,
            (entity, tick) => entity == 7 && tick < 250 ? 250 : null);

        stops[0].ShouldBe(250);
    }

    [Test]
    public void Compute_AStopBeforeTheOwnersDelete_StopsItAtTheStop()
    {
        int?[] stops = DispatchStops.Compute(
            [FromEntity(100, entity: 7), At(200, Stop, entity: 7)],
            NameOf,
            (entity, tick) => entity == 7 && tick < 250 ? 250 : null);

        stops[0].ShouldBe(200);
    }

    [Test]
    public void Compute_ADeleteWithNoStop_EndsItAtTheDelete()
    {
        int?[] stops = DispatchStops.Compute(
            [FromEntity(100, entity: 7)],
            NameOf,
            (entity, tick) => entity == 7 && tick < 250 ? 250 : null);

        stops[0].ShouldBe(250);
    }

    private static string? NameOf(int name) => name switch
    {
        Effect => "ParticleEffect",
        Stop => "ParticleEffectStop",
        _ => null,
    };

    private static SceneEffectDispatch FromEntity(int tick, int entity, bool reset = false) =>
        At(tick, Effect, entity, flags: reset ? 3 : 1);

    private static SceneEffectDispatch At(int tick, int name, int entity, int flags = 1) =>
        new(tick, name, default, default, default, default, flags, 1f, 0, 0, 0, 0, 0, entity, 0);
}
