using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// **The client precaches every surface's sounds at level init** — `PrecachePhysicsSounds` (`game/shared/physics_shared.cpp:1034-1053`):
/// <code>
/// for ( int i = 0; i &lt; physprops-&gt;SurfacePropCount(); i++ )
///     pprop-&gt;soundhandles.stepleft = PrecachePhysicsSoundByStringIndex( pprop-&gt;sounds.stepleft );   // ... and stepright,
///     // impactSoft, impactHard, scrapeSmooth, scrapeRough, bulletImpact, rolling, breakSound, strainSound
/// </code>
/// each through `CBaseEntity::PrecacheScriptSound` (`:1024`), which precaches EVERY wave of the script (B503).
/// </summary>
/// <remarks>
/// Measured (finding 84): with these left out, a bullet striking tile and a step on glass each decoded on the render thread the first
/// time they played, 170 ms and 204 ms frames. `rolling`, `breaksound` and `strainsound` are not read by this viewer's surface parser,
/// and nothing here plays them, so they have nothing to precache yet.
/// </remarks>
[TestFixture]
public sealed class PhysicsSoundPrecacheConformanceTests
{
    [Test]
    public void SurfaceWaves_SevenOfTheTenSurfaceSoundKeys_NameEveryWaveOnce()
    {
        VphysicsSurface tile = new("tile", default, hasSecondFriction: false)
        {
            Sounds = new SurfaceSoundNames("Tile.StepLeft", "Tile.StepRight", "Tile.BulletImpact")
            {
                ImpactHard = "Tile.ImpactHard", ImpactSoft = "Tile.ImpactSoft", ScrapeRough = "Tile.ScrapeRough", ScrapeSmooth = "Shared.Scrape",
            },
        };

        // The control: a surface that names nothing adds nothing. And a script two surfaces share is one set of waves.
        VphysicsSurface glass = new("glass", default, hasSecondFriction: false)
        {
            Sounds = new SurfaceSoundNames("Glass.Step", null, null) { ScrapeSmooth = "Shared.Scrape" },
        };
        VphysicsSurface silent = new("silent", default, hasSecondFriction: false);

        Dictionary<string, SoundScriptEntry> scripts = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string name, string[] waves) in new[]
        {
            ("Tile.StepLeft", new[] { "step/tile1.wav", "step/tile2.wav" }), ("Tile.StepRight", new[] { "step/tile3.wav" }),
            ("Tile.BulletImpact", new[] { "impact/tile_bullet1.wav", "impact/tile_bullet2.wav" }), ("Tile.ImpactHard", new[] { "impact/tile_hard.wav" }),
            ("Tile.ImpactSoft", new[] { "impact/tile_soft.wav" }), ("Tile.ScrapeRough", new[] { "scrape/tile_rough.wav" }),
            ("Shared.Scrape", new[] { "scrape/smooth.wav" }), ("Glass.Step", new[] { "step/glass1.wav", "step/glass3.wav" }),
        })
        {
            scripts[name] = new SoundScriptEntry(name, 4, new SoundRange(1f, 1f), new SoundRange(100f, 100f), new SoundRange(75f, 75f), waves);
        }

        DemoSounds.SurfaceWaves(new VphysicsSurfaceProps([tile, glass, silent]), scripts).ShouldBe(
        [
            "step/tile1.wav", "step/tile2.wav", "step/tile3.wav", "impact/tile_bullet1.wav", "impact/tile_bullet2.wav", "impact/tile_hard.wav",
            "impact/tile_soft.wav", "scrape/tile_rough.wav", "scrape/smooth.wav", "step/glass1.wav", "step/glass3.wav",
        ]);
    }

    [Test]
    public void SurfaceWaves_NoSurfacesOrNoScripts_NamesNothing()
    {
        DemoSounds.SurfaceWaves(null, new Dictionary<string, SoundScriptEntry>()).ShouldBeEmpty();
        DemoSounds.SurfaceWaves(new VphysicsSurfaceProps([]), null).ShouldBeEmpty();
    }
}
