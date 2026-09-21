using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, where `Corpus` binds to the namespace.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>That a real demo's explosions reach the timeline, which only real bytes can answer (B415).</summary>
/// <remarks>
/// **The synthetic tests own the decode; this owns the SPELLING.** `ExplosionFeedConformanceTests` builds its own
/// `CTETFExplosion` and knows the right answer because it put the value there. What it cannot establish is that a
/// real TF2 demo names the class `CTETFExplosion` and its origin `m_vecOrigin[0]` — a feed matching a name nothing
/// sends passes every synthetic test and produces no explosion on any recording (D38, and
/// `docs/memory/output-level-assertion-or-it-is-not-done.md`).
///
/// **Why this cannot be synthetic at all.** The claim is about what Valve's server sends.
/// </remarks>
public sealed class CorpusExplosionTests
{
    /// <remarks>
    /// **A real match, because the era specimens cannot answer this**: they are the owner's own solo recordings on
    /// period clients, with nobody to shoot at and nothing to explode.
    ///
    /// **A floor, not the census figure.** B415 counted 2,786 `CTETFExplosion` in this recording; asserting that
    /// exactly would make the test a change detector on one file. What is being claimed is that explosions survive
    /// the trip from the temp entity stream to the timeline in the hundreds, which no partial wiring does.
    /// </remarks>
    [Test]
    public void Explosions_OnARealMatch_AreReadFromTheTempEntityStream()
    {
        if (Corpus.Demo("demostf-cp_process_f12-2026-08-07") is not { } path)
        {
            Assert.Ignore("demostf-cp_process_f12-2026-08-07.dem is not available");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        IReadOnlyList<SceneExplosion> blasts = timeline.Explosions.All;

        TestContext.Out.WriteLine(
            $"{blasts.Count} explosions; {blasts.Count(one => one.InAir)} in mid air, " +
            $"{blasts.Count(one => one.HasEntity)} against an entity, " +
            $"{blasts.Count(one => one.HasCustomParticle)} naming their own particle");

        blasts.Count.ShouldBeGreaterThan(
            500,
            "a 26-minute six-versus-six match is full of rockets and stickies, and B415 censused 2,786");
    }

    /// <remarks>
    /// **The control, and it is what stops the test above from passing on a fabrication.** Every demo carries
    /// thousands of `CTEFireBullets`, `CTETFBlood` and `CTEEffectDispatch` effects; a feed that matched the wrong
    /// class, or no class at all, would still report a large count. These assertions are about the CONTENT of what
    /// was read, and each fails on a different wrong reading:
    ///
    /// - every blast at the world origin is a reader that did not find `m_vecOrigin[0..2]`, which are three
    ///   separate floats and not the vector a careless reader looks for;
    /// - every blast with the same weapon id is a reader that took some other class's field;
    /// - every blast in mid air, or none, is a normal that was never read.
    /// </remarks>
    [Test]
    public void Explosions_OnARealMatch_CarryPositionsWeaponsAndSurfaces()
    {
        if (Corpus.Demo("demostf-cp_process_f12-2026-08-07") is not { } path)
        {
            Assert.Ignore("demostf-cp_process_f12-2026-08-07.dem is not available");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        IReadOnlyList<SceneExplosion> blasts = timeline.Explosions.All;

        blasts.Count(one => one.X != 0f || one.Y != 0f || one.Z != 0f)
            .ShouldBeGreaterThan(
                blasts.Count / 2, "a blast at the world origin means m_vecOrigin was not read");

        blasts.Select(one => one.WeaponId).Distinct().Count()
            .ShouldBeGreaterThan(1, "a match has rockets AND stickies at least");

        int air = blasts.Count(one => one.InAir);

        air.ShouldBeGreaterThan(0, "rockets go off in mid air constantly");
        air.ShouldBeLessThan(blasts.Count, "and plenty of them hit walls");
    }

    /// <remarks>
    /// **Fire order, because the renderer searches by window.** `ExplosionFeed.Between` is a binary search over
    /// this list, and a search over an unsorted list answers confidently and wrongly — it would find whichever
    /// blasts happened to sit either side of the probe.
    /// </remarks>
    [Test]
    public void Explosions_OnARealMatch_AreInTickOrder()
    {
        if (Corpus.Demo("demostf-cp_process_f12-2026-08-07") is not { } path)
        {
            Assert.Ignore("demostf-cp_process_f12-2026-08-07.dem is not available");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        IReadOnlyList<SceneExplosion> blasts = timeline.Explosions.All;

        int outOfOrder = 0;

        for (int at = 1; at < blasts.Count; at++)
        {
            if (blasts[at].Tick < blasts[at - 1].Tick)
            {
                outOfOrder++;
            }
        }

        outOfOrder.ShouldBe(0);
    }
}
