using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Client systems that decide what is drawn and how smoothly, none of which exist here.
/// </summary>
/// <remarks>
/// **Eighth batch, and it was found by asking a different question.** The first seven swept formats
/// and asked which declared fields nothing reads. That question is exhausted — every lump and every
/// structure in the readers has been walked. This batch asks the opposite one: **which systems does
/// the client run that leave no trace in any file at all?**
///
/// The answer is a different shape. A PVS is a lump this project reads past; Hermite interpolation
/// is a flag whose absence is the default; a soundscape is a system with no wire representation
/// beyond an index. None of these would be found by inventorying a structure, because their absence
/// is not a field going unread — it is a behaviour never performed.
///
/// **Two of them are deviations this project already documented and never made runnable**, which is
/// the specific value of writing them here. <c>ScenePropTrack</c> says in prose that Hermite is
/// deliberately not implemented, and <c>MapWorld</c> says in prose that the engine culls against the
/// PVS per frame and this does not. A comment stating a gap is a good comment and is not a record: it
/// cannot be counted, and it goes stale silently the day the gap is closed. A skipping test is the
/// same claim in a form that reports itself.
/// </remarks>
public sealed class UnimplementedClientSystemConformanceTests
{
    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void ClientSystems_ThePotentiallyVisibleSet_DecidesWhatIsDrawn()
    {
        // LUMP_VISIBILITY is lump 4 and dvis_t is at bspfile.h:904 — a cluster count followed by a
        // per-cluster pair of byte offsets, one for the PVS and one for the PAS (audible set). Each
        // is a run-length-encoded bit vector, one bit per cluster, and a zero byte in the stream means
        // "skip the next N clusters entirely".
        //
        // The engine finds the leaf the camera is in, takes that leaf's cluster, and draws only the
        // clusters its PVS names. Everything else is not culled by the frustum, not depth-rejected,
        // not drawn at all - it is never submitted.
        //
        // **This project draws the whole map every frame.** MapWorld says so in its own comments. On
        // an outdoor map that is close to what the PVS would give anyway; indoors on a map like
        // badlands it is the difference between a room and a building. The cost is entirely
        // performance, which is why it has survived unnoticed: the picture is correct.
        //
        // The other half is worth stating because it is NOT free. Anything relying on the PVS for
        // correctness rather than speed - a mapper's areaportal, a room that is meant to be hidden
        // until entered - draws here when the engine would have hidden it.
        // Read through Constants rather than Enumerators because bspfile.h's lump list is an
        // ANONYMOUS enum (bspfile.h:279) — there is no type name to ask for.
        IReadOnlyDictionary<string, int> lumps = SourceSdk.Constants("src/public/bspfile.h");

        lumps["LUMP_VISIBILITY"].ShouldBe(4);

        Assert.Ignore(
            "the PVS is not used. dvis_t (bspfile.h:904) gives a per-cluster visible set the engine " +
            "draws from; this project submits the whole map every frame. Costs performance, and " +
            "draws anything a mapper hid by visibility rather than by geometry.");
    }

    [Test]
    public void ClientSystems_ABumpedLightmap_CarriesThreeSamplesPerLuxel()
    {
        // NUM_BUMP_VECTS is 3 (bumpvects.h:25). A face whose material has a bump map is lit with FOUR
        // lightmap samples per luxel, not one: a flat sample followed by three taken along the basis
        // vectors, so the baked light can be recombined against the normal map at draw time.
        //
        // The consequence for anyone reading the lighting lump naively is the reason this belongs
        // here rather than in a performance note: **the samples are interleaved, so a reader that
        // assumes one sample per luxel does not fail — it reads every fourth luxel's flat sample and
        // treats three quarters of the buffer as further luxels.** The map lights up, dimly and
        // wrongly, with no error anywhere.
        //
        // This project has BumpedLight in Content and the world shader carries a "has a bump map"
        // flag, so the data is understood. What does not happen is the recombination: the flat sample
        // is used and the three directional ones are skipped, which loses exactly the detail bump
        // mapping exists to provide.
        IReadOnlyDictionary<string, int> bump = SourceSdk.Constants("src/public/mathlib/bumpvects.h");

        bump["NUM_BUMP_VECTS"].ShouldBe(3);

        Assert.Ignore(
            "bumped lightmaps are not recombined. A bumped face stores 1 + NUM_BUMP_VECTS samples " +
            "per luxel; this uses the flat one and skips the three directional samples, which is " +
            "the entire benefit of the bump basis.");
    }

    [Test]
    public void ClientSystems_Interpolation_DefaultsToHermiteWithLinearAsOptOut()
    {
        // **The polarity is the finding.** INTERPOLATE_LINEAR_ONLY is (1<<4) in interpolatedvar.h:36
        // and its comment reads "don't do hermite interpolation". Linear is the flag; Hermite is what
        // happens without one. A reader who assumes the engine lerps between two snapshots — which is
        // the obvious assumption, and the one this project made — has it backwards.
        //
        // Hermite needs three samples, not two: the value being interpolated toward, the one behind
        // it, and one further back to derive a slope from. That is why it is not a drop-in
        // replacement for a lerp and why the deviation was taken deliberately.
        //
        // Valve does not use it everywhere either, and the exception is instructive: angles fall back
        // to linear because a Hermite spline through three QAngles has no meaning without converting
        // to quaternions first. So the honest description of this gap is "positions should be
        // Hermite and are linear", not "interpolation is wrong".
        //
        // Visible as a slight corner-cutting on fast direction changes — a player who strafes hard
        // moves through a rounder path here than they did on the server.
        IReadOnlyDictionary<string, int> flags =
            SourceSdk.Constants("src/game/client/interpolatedvar.h");

        flags["INTERPOLATE_LINEAR_ONLY"].ShouldBe(1 << 4);

        Assert.Ignore(
            "interpolation is linear. INTERPOLATE_LINEAR_ONLY is a flag meaning 'don't do hermite' " +
            "(interpolatedvar.h:36), so Hermite is the engine default and this is the opt-out. " +
            "Needs three samples rather than two, which is why it was deferred.");
    }

    [Test]
    public void ClientSystems_AnimationAndSimulation_UseSeparateClocks()
    {
        // LATCH_ANIMATION_VAR and LATCH_SIMULATION_VAR (interpolatedvar.h:30-31) select which of two
        // timestamps a variable's history is sampled against: m_flAnimTime or m_flSimulationTime.
        // They are networked separately and they do not advance together.
        //
        // **Why the engine bothers**: a moving player's position updates every time the server
        // simulates them, while their animation cycle advances on its own schedule and can be
        // restarted by a sequence change. Interpolating both against one clock makes an animation
        // stutter whenever movement updates arrive irregularly, which on a demo is often.
        //
        // Not a performance gap and not invisible: this is why a replayed player's legs can appear to
        // slide relative to their movement. Written down because that symptom is the kind that gets
        // attributed to the animation system, which is the wrong place to look.
        IReadOnlyDictionary<string, int> flags =
            SourceSdk.Constants("src/game/client/interpolatedvar.h");

        flags["LATCH_ANIMATION_VAR"].ShouldBe(1 << 0);
        flags["LATCH_SIMULATION_VAR"].ShouldBe(1 << 1);

        Assert.Ignore(
            "one clock drives every interpolated value. The engine latches animation variables " +
            "against m_flAnimTime and simulation ones against m_flSimulationTime, which advance " +
            "independently — conflating them makes animation slide against movement.");
    }

    [Test]
    public void ClientSystems_TheHud_IsWhereADecodedEventBecomesVisible()
    {
        // **This said TF2's HUD is not in the public SDK and that a decompiler or the live client
        // would be needed. That was wrong.** src/game/client/tf carries 125 HUD sources, including
        // tf_hud_deathnotice.cpp — the kill feed — alongside the ammo, health and timer elements.
        //
        // The correction matters more than the entry, because the wrong version named a decompiler
        // as the next step for something Valve published. That is the expensive kind of mistake: it
        // does not block anything visibly, it just makes a cheap task look costly enough to defer.
        //
        // **The gap is not the drawing, it is the absence of any presentation layer at all.** Health,
        // ammo, the scoreboard, the kill feed and the round timer are all reconstructible from state
        // this project already decodes, and now the exact layout is readable too.
        //
        // Kept separate from "game events are decoded and never shown" in the entity batch, which is
        // about one specific stream. This is the surface all of them would arrive on, and building it
        // once is what makes each of those cheap.
        IEnumerable<string> hud = SourceSdk.Files("src/game/client/tf", "tf_hud_*.cpp");

        hud.Count().ShouldBeGreaterThan(50);

        Assert.Ignore(
            "there is no HUD or presentation layer. Health, ammo, scoreboard, kill feed and timer " +
            "are all derivable from state already decoded here, and TF2's own HUD sources ARE in " +
            "the SDK (tf_hud_deathnotice.cpp and 124 others) — neither the data nor the layout is " +
            "the blocker.");
    }
}
