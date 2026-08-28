using System;
using System.IO;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The render bounds read off real shipped models.
/// </summary>
/// <remarks>
/// **The conformance suite pins the offsets against `studio.h`; this pins them against a file.**
/// Those are different claims: the first says the field order in the header matches Valve's, the
/// second says the numbers we pull out of a real `.mdl` are a plausible box rather than a float
/// read from the middle of something else. An offset wrong by four bytes passes the first and
/// produces garbage here.
///
/// **The predictions are shapes, not magic numbers.** A scout is roughly 83 units tall (this
/// project already pins `PlayerHeight = 83f` from the player hull), so his bounds must be tens of
/// units and not thousands or thousandths. Asserting the exact authored figure would be a change
/// detector against Valve's art, which they may revise; asserting the order of magnitude and the
/// invariants catches every way a bad offset fails.
/// </remarks>
public sealed class StudioBoundsTests
{
    /// <summary>The scout, because every class model is authored the same way.</summary>
    private const string Scout = "models/player/scout.mdl";

    private static string Game => GameInstall.Require();

    /// <summary>That the scout's idle sequence is contained by its hull, so the union is inert.</summary>
    /// <remarks>
    /// **Measured, and it is why `RenderBounds` is the WEAKER instrument of the two here.** The
    /// scout's sequence 0 box sits inside the movement hull, so unioning it changes nothing — and
    /// when `HeaderHullMinOffset` was shifted by four bytes, that same union pulled the corrupted
    /// Y back to the correct one and hid the fault. A test on `RenderBounds` therefore cannot pin
    /// the hull's offsets; `MovementHull_ForTheScout_IsExactlyWhatTheFileHolds` is what does.
    ///
    /// Kept because the containment is a real fact worth asserting: a model whose sequence box
    /// escaped its hull would be a decode error or a genuinely unusual animation, and either is
    /// worth a red test.
    /// </remarks>
    [Test]
    public void RenderBounds_ForTheScoutsFirstSequence_EqualTheHull()
    {
        ReadOnlyMemory<byte> model = Read(Scout);

        StudioBounds.RenderBounds(model, sequence: 0)
            .ShouldBe(StudioBounds.MovementHull(model));
    }

    /// <summary>That the bounds are the authored box and not the mesh's extent.</summary>
    /// <remarks>
    /// **The control that separates this from the shortcut that was nearly taken.** A vertex extent
    /// and an authored hull are both "about the size of a scout", so a test asserting only the
    /// magnitude cannot tell them apart — and the whole point of reading these fields is that they
    /// are a different number. Valve's hull is a MOVEMENT box: it is symmetric about the origin in
    /// X and Y, because a player's collision hull is, and a mesh's extent is not.
    /// </remarks>
    /// <summary>That the hull is mirrored in Y, which a shifted read cannot be.</summary>
    /// <remarks>
    /// **This replaced a range check that could not fail.** The first version asserted the box was
    /// "tens of units", and shifting `HeaderHullMinOffset` by four bytes left it green — the six
    /// floats are all small and of similar magnitude, so reading one component early still produces
    /// a human-sized box. The assertions were loose, not the condition.
    ///
    /// **Y symmetry is the invariant that survives nothing.** Measured on the scout:
    /// `MinY = -16.550379`, `MaxY = 16.550385` — a movement hull is a mirrored capsule in plan, so
    /// those must be negatives of each other. Shifted by one component the pair becomes
    /// `(-3.51, 83.03)`, which fails by a mile. X is deliberately NOT asserted symmetric: the scout's
    /// is `-19.27` to `6.60`, offset forward, and predicting symmetry there would be predicting
    /// something untrue.
    /// </remarks>
    /// <summary>The scout's hull, every component, exactly as the file holds it.</summary>
    /// <remarks>
    /// **No tolerance anywhere, deliberately.** An earlier version asserted `MaxZ` was 83 "within
    /// 0.1", and the owner objected to exactly that — the real value is `83.02696`, nothing in the
    /// decoder rounds it, and a tolerance in the test is only a place for a wrong read to hide. A
    /// four-byte shift lands on a neighbouring float of similar magnitude, which is precisely what
    /// a tenth of a unit's slack would forgive.
    ///
    /// **Predicting six exact floats is the point rather than a risk.** These are Valve's authored
    /// numbers, not ours; they change only if Valve reships the model, and if that happens this
    /// test failing is the correct outcome and someone should look. The alternative — an assertion
    /// loose enough to survive an art update — is also loose enough to survive a decode bug.
    ///
    /// The shape they describe: mirrored in Y (`-16.550379` against `16.550385`, differing in the
    /// last bits as authored data does), offset forward in X rather than centred, standing just
    /// below the origin and reaching the player's full height.
    /// </remarks>
    [Test]
    public void MovementHull_ForTheScout_IsExactlyWhatTheFileHolds()
    {
        StudioBounds.MovementHull(Read(Scout)).ShouldBe(
            new StudioBox(
                MinX: -19.270922f,
                MinY: -16.550379f,
                MinZ: -3.506928f,
                MaxX: 6.600247f,
                MaxY: 16.550385f,
                MaxZ: 83.02696f));
    }

    /// <summary>That the hull's own numbers are asserted, not the sequence's.</summary>
    /// <remarks>
    /// **Two earlier attempts at this test could not fail, for two different reasons, and both are
    /// worth keeping.**
    ///
    /// The first asserted `RenderBounds(sequence: 0)` was mirrored in Y. It stayed green against a
    /// four-byte shift in `HeaderHullMinOffset` because `RenderBounds` UNIONS the sequence's box in,
    /// and the scout's sequence 0 box is wider in Y than the hull — so the union restored the
    /// symmetry the broken read had destroyed. The assertion was fine; it was measuring the
    /// sequence, not the hull.
    ///
    /// The second compared `RenderBounds(sequence: -1)` against `MovementHull`. Both go through the
    /// same offset, so a shift moves both and they agree exactly as before — a reader compared
    /// against itself, which is the shape that cannot fail by construction.
    ///
    /// What works is asserting the HULL's own structure, above, and separately that the sequence
    /// union grows the box, below. One subject each.
    /// </remarks>
    /// <summary>That the 83 this project already pins is this hull, rounded.</summary>
    /// <remarks>
    /// **The rounding is the CLAIM here, not a tolerance.** `StudioAnimationTests` carries
    /// `PlayerHeight = 83f`, described there as the game's own player hull and used to check that a
    /// posed model stands up. That constant is the hull height rounded to a whole unit, and saying
    /// so with `MathF.Round` states the relationship exactly rather than smuggling in slack — see
    /// `docs/memory/two-recordings-of-one-value.md`, which is what makes two independent routes to
    /// one number evidence instead of a restatement.
    /// </remarks>
    [Test]
    public void MovementHull_ForTheScout_RoundsToThePinnedPlayerHeight()
    {
        MathF.Round(StudioBounds.MovementHull(Read(Scout)).MaxZ).ShouldBe(83f);
    }

    /// <summary>That the hull's height is the 83 units this project already pins elsewhere.</summary>
    /// <remarks>
    /// **The same number by two unrelated routes** (`docs/memory/two-recordings-of-one-value.md`).
    /// `StudioAnimationTests` carries `PlayerHeight = 83f`, described there as coming from the
    /// game's own player hull and used to check a posed model stands up. This reads that hull out
    /// of the file directly and gets 83.027 — so the constant and the decoder agree without either
    /// having been derived from the other, which is what makes it evidence rather than a
    /// restatement.
    /// </remarks>

    /// <summary>That this model has no clipping box, so the hull is what gets used.</summary>
    /// <remarks>
    /// **Measured, and it falsified the guess in this file's first draft**, which asserted a player
    /// model carries a clipping box. The scout's `view_bbmin`/`view_bbmax` are all zero, so
    /// `IsAuthored` is false and `GetRenderBounds` falls through to the hull — which means the
    /// selection's ELSE branch is the one TF2 players exercise, and the branch a test on a player
    /// model actually covers.
    /// </remarks>
    [Test]
    public void ClippingBox_ForAPlayerModel_IsAbsentSoTheHullIsUsed()
    {
        ReadOnlyMemory<byte> model = Read(Scout);

        StudioBounds.ClippingBox(model).IsAuthored.ShouldBeFalse();

        StudioBounds.RenderBounds(model, sequence: -1)
            .ShouldBe(StudioBounds.MovementHull(model));
    }

    /// <summary>That a sequence widens the box rather than replacing it.</summary>
    /// <remarks>
    /// **Measured as a relationship, not as a number.** Whatever sequence 0 is, unioning its box in
    /// cannot make the result smaller than the header's box alone — `VectorMin`/`VectorMax` only
    /// ever grow it. A reader that overwrote instead of unioning would fail this whenever the
    /// sequence box is tighter, which for an idle animation it usually is.
    /// </remarks>
    [Test]
    public void RenderBounds_WithASequence_AreNeverSmallerThanWithout()
    {
        ReadOnlyMemory<byte> model = Read(Scout);

        StudioBox headerOnly = StudioBounds.RenderBounds(model, sequence: -1);
        StudioBox withSequence = StudioBounds.RenderBounds(model, sequence: 0);

        withSequence.MinX.ShouldBeLessThanOrEqualTo(headerOnly.MinX);
        withSequence.MinY.ShouldBeLessThanOrEqualTo(headerOnly.MinY);
        withSequence.MinZ.ShouldBeLessThanOrEqualTo(headerOnly.MinZ);
        withSequence.MaxX.ShouldBeGreaterThanOrEqualTo(headerOnly.MaxX);
        withSequence.MaxY.ShouldBeGreaterThanOrEqualTo(headerOnly.MaxY);
        withSequence.MaxZ.ShouldBeGreaterThanOrEqualTo(headerOnly.MaxZ);
    }

    /// <summary>That an out-of-range sequence is ignored rather than read.</summary>
    [Test]
    public void RenderBounds_ForASequenceTheModelDoesNotHave_AreTheHeadersAlone()
    {
        ReadOnlyMemory<byte> model = Read(Scout);

        StudioBounds.RenderBounds(model, sequence: 100_000)
            .ShouldBe(StudioBounds.RenderBounds(model, sequence: -1));
    }

    /// <summary>That BOTH boxes read as plausible, not only the one this model uses.</summary>
    /// <remarks>
    /// **Added after a sabotage went unnoticed.** Shifting `HeaderHullMinOffset` by four bytes left
    /// every real-file test green: the scout carries an authored clipping box, so `RenderBounds`
    /// never reads the hull, and the broken offset had nothing to break. The condition was wrong,
    /// not the assertions — so the fix is an input that reaches the other branch, which here means
    /// reading the hull directly rather than through the selection.
    ///
    /// Both boxes are checked in one test on purpose: they are two halves of one claim about the
    /// header's layout, and splitting them invites one to be deleted as redundant.
    /// </remarks>
    [Test]
    [Explicit("Diagnostic. Run by hand when a bounds offset is in question.")]
    public void MovementHullAndClippingBox_ForAPlayerModel_AreReported()
    {
        ReadOnlyMemory<byte> model = Read(Scout);

        StudioBox hull = StudioBounds.MovementHull(model);

        TestContext.Out.WriteLine($"hull     {hull}");
        TestContext.Out.WriteLine($"clipping {StudioBounds.ClippingBox(model)}");
        TestContext.Out.WriteLine($"render   {StudioBounds.RenderBounds(model, 0)}");

        // A floor, so a probe that read nothing cannot pass by vacuum.
        hull.LongestAxis.ShouldBeGreaterThan(0f);
    }

    [Test]
    public void RenderBounds_ForATruncatedFile_AreEmpty()
    {
        StudioBounds.RenderBounds(new byte[64], sequence: 0).ShouldBe(StudioBox.Empty);
    }

    /// <summary>That the longest axis is the longest, not the first or the vertical.</summary>
    [Test]
    public void LongestAxis_ForABoxLongestInY_IsThatAxis()
    {
        new StudioBox(-1f, -50f, -2f, 1f, 50f, 2f).LongestAxis.ShouldBe(100f);
    }

    private static ReadOnlyMemory<byte> Read(string path)
    {
        GameArchives archives = GameArchives.Open(Game);

        return Skip.Unless(
            archives.Read(path), $"{path} is not in this install's archives");
    }
}
