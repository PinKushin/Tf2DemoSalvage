using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Working out every model a demo will show, before anything is drawn.
/// </summary>
/// <remarks>
/// **This was <c>MainForm.DemoModelPaths</c> and <c>WornModelPaths</c>** (B188, D90) — a question
/// about a demo and an install, asked from a window, and therefore untested.
///
/// **What is NOT covered here, said rather than left to look covered: the three timeline sources.**
/// Props, viewmodels and held weapons all need a `DemoTimeline`, whose only public factory reads a
/// demo file — so a device-free test cannot reach them, and they are covered end to end by the
/// corpus suites instead. What IS here is the source that needs no demo at all, and it is the one
/// that fails silently.
/// </remarks>
public sealed class DemoModelsTests
{
    // **A roster case was written here and removed, and the reason is worth keeping.** It asserted
    // that `Needed` returns the nine class models with no demo open, using the real locator — and it
    // FAILED, because `Tf2ConfigFiles.DefaultGameFolder` only looks under Program Files (x86) while
    // this machine keeps TF2 on another drive. The code was right; the test was measuring the
    // ENVIRONMENT.
    //
    // Pointing it at a better path would have hidden that rather than fixed it: a deterministic
    // suite that passes or fails on where somebody installed a game is not deterministic. The roster
    // needs an install, so it belongs with the suites that skip without one, alongside the three
    // timeline sources named above.

    [Test]
    public void Needed_WithNoInstall_IsEmptyRatherThanThrowing()
    {
        // **The control, and the ordinary case on a machine with no TF2.** An empty set means
        // nothing packs, which is correct: there is nothing to pack from.
        GameContent game = GameContent.Open(folder: null, new RecordingLoggerFactory());

        DemoModels.Needed(timeline: null, game).ShouldBeEmpty();
    }

    [Test]
    public void Needed_IsCaseInsensitive()
    {
        // A demo names models as the server sent them and the archives are case-insensitive, so a
        // set that treated `Models/Player/Scout.mdl` and `models/player/scout.mdl` as two would pack
        // the same geometry twice and grow the vertex buffer for nothing.
        HashSet<string> needed = DemoModels.Needed(timeline: null, Empty());

        needed.Comparer.ShouldBe(StringComparer.OrdinalIgnoreCase);
    }

    [Test]
    public void Worn_WithNoDemoOpen_IsEmpty()
    {
        // Nothing is worn by nobody. Empty rather than null, so the caller hands it straight on.
        DemoModels.Worn(timeline: null, Empty()).ShouldBeEmpty();
    }

    [Test]
    public void Needed_WithNoInstallSupplied_Refuses()
    {
        // A real object rather than null (D83): the install always answers, even when it is empty.
        Should.Throw<ArgumentNullException>(() => DemoModels.Needed(timeline: null, game: null!));
    }

    [Test]
    public void Worn_WithNoInstallSupplied_Refuses()
    {
        Should.Throw<ArgumentNullException>(() => DemoModels.Worn(timeline: null, game: null!));
    }

    /// <summary>An install that is not there, which is the only kind a unit test should assume.</summary>
    private static GameContent Empty() =>
        GameContent.Open(folder: null, new RecordingLoggerFactory());
}
