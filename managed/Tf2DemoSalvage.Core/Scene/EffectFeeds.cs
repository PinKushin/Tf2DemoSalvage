using System;

using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>Every feed a demo's one-shot temp entities are offered to, in the order they are asked (B415).</summary>
/// <remarks>
/// **One place to add a feed.** Each says for itself whether an effect is its business; the first that takes it ends
/// the offer. A new temp entity class is a new feed here, not a new parameter threaded through the timeline's walk.
/// </remarks>
public sealed class EffectFeeds
{
    /// <summary>`CTETFExplosion`.</summary>
    public ExplosionFeed Explosions { get; } = new();

    /// <summary>`CTEFireBullets`.</summary>
    public ShotFeed Shots { get; } = new();

    /// <summary>`CTEWorldDecal`, `CTEDecal`, `CTEPlayerDecal` and the <c>decalprecache</c> table.</summary>
    public DecalFeed Decals { get; } = new();

    /// <summary>`CTETFBlood`.</summary>
    public BloodFeed Blood { get; } = new();

    /// <summary>`CTEEffectDispatch` and the <c>EffectDispatch</c> table.</summary>
    public EffectDispatchFeed Dispatches { get; } = new();

    /// <summary>`CTETFParticleEffect`, as the `"ParticleEffect"` dispatches the client turns them into.</summary>
    public TfParticleEffectFeed TfParticleEffects { get; } = new();

    /// <summary>`CTESparks`, `CTEMetalSparks` and `CTEArmorRicochet`.</summary>
    public SparkFeed Sparks { get; } = new();

    /// <summary>Offers one decoded temp entity to each feed until one takes it.</summary>
    /// <param name="className">The class the effect's id resolved to.</param>
    /// <param name="effect">The decoded effect.</param>
    /// <param name="tick">The tick its packet arrived on.</param>
    /// <param name="isPlayer">Whether an entity index names a player right now.</param>
    /// <param name="shooter">A shot's shooter as the client sees them right now.</param>
    /// <param name="reliable">Whether its message came reliably — a zero count (B504).</param>
    /// <returns>Whether a feed took it.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Each is numbered by its place among ALL the demo's temp entities**, taken or not: `CL_FireEvents` (engine.dll
    /// FUN_1800905d0) fires the queue in the order it was filled, so that place is the order the client makes their sounds
    /// in on one frame (B505).
    /// </remarks>
    public bool Record(
        string className,
        DecodedTempEntity effect,
        int tick,
        Func<int, bool> isPlayer,
        Func<int, ShotShooter?> shooter,
        bool reliable = false)
    {
        int place = ++_offered;

        return Explosions.Record(className, effect, tick, isPlayer, place, reliable) ||
            Shots.Record(className, effect, tick, shooter, place, reliable) ||
            Decals.Record(className, effect, tick) ||
            Blood.Record(className, effect, tick, isPlayer) ||
            Dispatches.Record(className, effect, tick, place, reliable) ||
            TfParticleEffects.Record(className, effect, tick) ||
            Sparks.Record(className, effect, tick);
    }

    /// <summary>How many temp entities have been offered.</summary>
    private int _offered;
}
