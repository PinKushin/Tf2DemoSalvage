using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <see cref="ImpactRenderables"/>: a view's legacy impact particles as the engine files them (B734).
/// </summary>
/// <remarks>
/// An emitter is a <c>CParticleEffectBinding</c>, a translucent leaf-system renderable (`particlemgr.cpp:220`) collated by its
/// box; <c>AddSimpleParticle</c> feeds the simple singleton, drawn by <c>DrawParticleSingletons</c> before the leaf walk
/// (`viewrender.cpp:4570`); a <c>CFXQuad</c> is a client-side effect drawn after the view (`:1462-1463`).
/// </remarks>
public sealed class ImpactRenderablesConformanceTests
{
    private static readonly IReadOnlyDictionary<string, ParticleMaterial> Materials =
        new Dictionary<string, ParticleMaterial>(StringComparer.OrdinalIgnoreCase)
        {
            ["fleck"] = new ParticleMaterial(null, [], SpriteBlend.Translucent),
            ["dust"] = new ParticleMaterial(null, [], SpriteBlend.Translucent),
            ["flare"] = new ParticleMaterial(null, [], SpriteBlend.Additive),
        };

    /// <remarks>
    /// **Each pass in its place**: the fleck emitter is placed where the view puts its fixed box (±5 about x = 100,
    /// `fx_fleck.cpp:142-143`), centred there with its sort origin as its origin; the singleton dust is the singleton pass,
    /// unplaced; the quad is drawn after the view, unplaced.
    /// </remarks>
    [Test]
    public void Add_AFleckEmitterSingletonDustAndAQuad_GoToTheirOwnPasses()
    {
        IReadOnlyList<ParticleBatch> batches = Built(Effect(), sees: null, place: _ => 3, clientEffects: true, singletons: true);

        ParticleBatch fleck = batches.Single(batch => batch.Placement is not null);
        TranslucentPlacement placement = fleck.Placement.ShouldNotBeNull();
        placement.Place.ShouldBe(3);
        placement.Centre.ShouldBe((100f, 0f, 0f));
        placement.Origin.ShouldBe((100f, 0f, 0f));
        fleck.Pass.ShouldBe(ParticlePass.Translucent);

        batches.Single(batch => batch.Pass == ParticlePass.Singletons).Placement.ShouldBeNull();
        batches.Count(batch => batch.Pass == ParticlePass.Translucent && batch.Placement is null).ShouldBe(1, "the quad");
    }

    /// <remarks>
    /// **What each view draws**: a view that does not run <c>DrawEffects</c> (a water view, the sky) draws no quad; the 3D
    /// sky draws no simple singleton; a view whose collation refuses the box draws no emitter. Each against the test above.
    /// </remarks>
    [Test]
    public void Add_AViewWithoutClientEffectsSingletonsOrTheBox_DrawsNoneOfThem()
    {
        Built(Effect(), null, _ => 3, clientEffects: false, singletons: true)
            .ShouldNotContain(batch => batch.Pass == ParticlePass.Translucent && batch.Placement == null, "no quad");
        Built(Effect(), null, _ => 3, clientEffects: true, singletons: false)
            .ShouldNotContain(batch => batch.Pass == ParticlePass.Singletons);

        ImpactRenderables renderables = new();

        renderables.Begin();
        renderables.Add(Effect(), Vector3.Zero, Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ, _ => 1f, _ => false, _ => 3, true, true);

        renderables.Batches(Materials).ShouldNotContain(batch => batch.Placement != null);
        renderables.Culled.ShouldBe(1);
    }

    /// <remarks>
    /// **An auto-updating box is the particles'**, else the sort origin alone (`BBoxCalcEnd`, `particlemgr.cpp:264-293`); a
    /// fixed one is what <c>SetBBox</c> set, whatever the particles do.
    /// </remarks>
    [Test]
    public void RenderBox_AutoUpdatingOrFixed_IsTheParticlesOrTheSetBox()
    {
        ImpactEmitter auto = new(ImpactEmitterKind.Dust) { SortOrigin = new Vector3(7f) };

        auto.RenderBox().ShouldBe((new Vector3(7f), new Vector3(7f)), "no particles: the sort origin");

        auto.Particles.Add(new ImpactParticle("dust", new Vector3(1f, 2f, 3f), Vector3.Zero, 1f));
        auto.Particles.Add(new ImpactParticle("dust", new Vector3(-4f, 5f, 0f), Vector3.Zero, 1f));
        auto.RenderBox().ShouldBe((new Vector3(-4f, 2f, 0f), new Vector3(1f, 5f, 3f)));

        ImpactEmitter fixedBox = new(ImpactEmitterKind.Trail) { FixedBox = true, Mins = new Vector3(-32f), Maxs = new Vector3(32f) };

        fixedBox.Particles.Add(new ImpactParticle("spark", new Vector3(500f), Vector3.Zero, 1f));
        fixedBox.RenderBox().ShouldBe((new Vector3(-32f), new Vector3(32f)));
    }

    private static IReadOnlyList<ParticleBatch> Built(
        ImpactEffect effect,
        Func<(Vector3 Min, Vector3 Max), bool>? sees,
        Func<(Vector3 Min, Vector3 Max), int>? place,
        bool clientEffects,
        bool singletons)
    {
        ImpactRenderables renderables = new();

        renderables.Begin();
        renderables.Add(effect, Vector3.Zero, Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ, _ => 1f, sees, place, clientEffects, singletons);

        return [.. renderables.Batches(Materials)];
    }

    /// <summary>A fleck emitter at x = 100, two singleton dust puffs and a quad, all a camera at the origin can see.</summary>
    private static ImpactEffect Effect()
    {
        ImpactEffect effect = new();
        Vector3 at = new(100f, 0f, 0f);
        ImpactEmitter flecks = new(ImpactEmitterKind.Fleck) { FixedBox = true, Mins = at - new Vector3(5f), Maxs = at + new Vector3(5f), SortOrigin = at };

        flecks.Particles.Add(new ImpactParticle("fleck", at, Vector3.Zero, 3f) { Size = 2 });
        effect.Emitters.Add(flecks);

        ImpactEmitter dust = new(ImpactEmitterKind.Simple) { Singleton = true };

        dust.Particles.Add(new ImpactParticle("dust", at, Vector3.Zero, 1f) { StartSize = 4, EndSize = 8, StartAlpha = 200 });
        effect.Emitters.Add(dust);
        effect.Quads.Add(new ImpactQuad("flare", at, -Vector3.UnitX, (24f, 0f), (1f, 0f), 0.1f, 0f));

        return effect;
    }
}
