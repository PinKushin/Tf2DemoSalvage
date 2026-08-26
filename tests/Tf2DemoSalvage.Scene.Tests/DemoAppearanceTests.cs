using System;

using Microsoft.Extensions.Logging.Abstractions;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Building what the players in a demo look like, once the install is open.
/// </summary>
/// <remarks>
/// **This was <c>MainForm.EnsureWeaponRoles</c>** (B188, D90) — walking a timeline, reading an
/// archive and building an appearance, none of which is window work and none of which had a test.
///
/// **It is the member that already caused a shipped regression.** When `AddViewmodel` moved out, the
/// call to it was dropped: every weapon suffix answered null and every player animated with the
/// generic primary pose — the right weapon, the wrong hold, on everybody. An analyzer caught that
/// one only because the method became unreachable, which is luck rather than a check.
///
/// **So the wiring is now the RETURN VALUE.** `Ensure` takes the current appearance and gives back
/// the one to use, which a caller cannot use without assigning. The old shape wrote into
/// `_moment.Appearance` as a side effect, and a side effect is exactly what goes missing.
///
/// **Laziness is a real constraint, not an optimisation.** The archives open AFTER the demo is
/// applied, so building at load time silently produced nothing — the first attempt did precisely
/// that. These tests pin that a call with no install changes nothing and does not poison the cache.
/// </remarks>
public sealed class DemoAppearanceTests
{
    [Test]
    public void Ensure_WithNoInstall_KeepsTheEmptyAppearance()
    {
        // **The lazy case, and the reason this is not built at demo load.** The archives are opened
        // later, so a build attempted too early reads nothing and would cache that nothing for the
        // life of the demo.
        IPlayerAppearance kept = DemoAppearance.Ensure(
            DemoAppearance.None, timeline: null, game: null, NullLogger.Instance);

        kept.ShouldBeSameAs(DemoAppearance.None);
    }

    [Test]
    public void Ensure_WithAnAppearanceAlreadyBuilt_ReturnsThatSameOne()
    {
        // **Ensure, not Build.** It runs on every moment, and rebuilding would re-read the archive
        // sixty times a second. The identity check is the assertion: an equal-but-new instance
        // would pass a value comparison while still costing the read.
        GameAppearance already = new(Classes: null, Roles: null);

        DemoAppearance.Ensure(already, timeline: null, game: null, NullLogger.Instance)
            .ShouldBeSameAs(already);
    }

    [Test]
    public void None_AskedForAnAsset_AnswersNothing()
    {
        // The control for the case above: `None` must be recognisable AS empty, or `Ensure` cannot
        // tell "nothing built yet" from "built, and this demo genuinely has no models".
        DemoAppearance.None.ModelOf(3).ShouldBeNull();
        DemoAppearance.None.WeaponSuffix("CTFRocketLauncher", 3).ShouldBeNull();
        DemoAppearance.None.Hands(3).ShouldBeNull();
    }

    [Test]
    public void None_AskedWhetherAClassAirwalks_AnswersTrue()
    {
        // **Not a null answer, and this is the one place "knows nothing" is not "says no".** Every
        // class air-walks except the medic, so a `false` default would stop every class air-walking
        // on a machine with no TF2 installed — a silent BEHAVIOUR change wearing the appearance of
        // a missing asset. `GameAppearance` gives the same answer when the install cannot say.
        //
        // Asserted separately from the three above precisely because it breaks their pattern: a
        // reader who assumes the null object answers null to everything is wrong here, and a test
        // that lumped it in would let someone "tidy" it to false.
        DemoAppearance.None.Airwalks(3).ShouldBeTrue();
    }

    [Test]
    public void None_AskedTwice_IsTheSameInstance()
    {
        // It is used as a sentinel by `Ensure` and by `MomentScene`'s "no player appearance"
        // report, both of which compare by identity. A property returning a fresh object each time
        // would break both silently.
        DemoAppearance.None.ShouldBeSameAs(DemoAppearance.None);
    }

    [Test]
    public void Ensure_WithNoLogger_Refuses()
    {
        // What it builds is reported — the weapon-role table is the only record of which suffix
        // each held weapon resolved to, and a wrong one shows as the wrong hold rather than as an
        // error. A null sink is a caller mistake, not a quiet mode.
        Should.Throw<ArgumentNullException>(() =>
            DemoAppearance.Ensure(DemoAppearance.None, null, null, log: null!));
    }
}
