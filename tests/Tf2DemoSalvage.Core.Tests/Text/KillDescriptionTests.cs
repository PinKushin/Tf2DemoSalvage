using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// Turning a decoded death into a line a person can read.
/// </summary>
/// <remarks>
/// **Every field was already decoded and printed; none of it was interpreted.** A death currently
/// renders as <c>customkill=1 damagebits=34603010 death_flags=0</c>, which is the raw truth and
/// tells a reader nothing. `customkill=1` is a headshot.
///
/// **Only a named subset is translated, and that is deliberate.** TF2 declares **87** custom kill
/// values, most of them individual taunt kills that change with every update. Transcribing all of
/// them into this project would be a maintenance burden that buys almost nothing, and would go stale
/// silently.
///
/// So the rule here is: **name the ones that change how a kill reads, pass the rest through as a
/// number.** A number is honest — it says "this was a special kill of kind 47" — where a wrong name
/// would not. That is different from a fallback that papers over an unknown value, because nothing
/// downstream branches on the result; it is text for a human.
/// </remarks>
public sealed class KillDescriptionTests
{
    [Test]
    public void AHeadshotIsNamedRatherThanNumbered()
    {
        // TF_DMG_CUSTOM_HEADSHOT = 1, pinned against the SDK by
        // UnimplementedItemConformanceTests and GameEventTypeWidthConformanceTests.
        KillDescription.CustomKill(1).ShouldBe("headshot");
        KillDescription.CustomKill(2).ShouldBe("backstab");
        KillDescription.CustomKill(3).ShouldBe("burning");
    }

    [Test]
    public void AnOrdinaryKillHasNoCustomDescription()
    {
        // TF_DMG_CUSTOM_NONE. Null rather than "none" or an empty string: the caller decides how an
        // absent qualifier reads, and most kills are this.
        KillDescription.CustomKill(0).ShouldBeNull();
    }

    [Test]
    public void AnUnnamedCustomKillIsReportedAsItsNumber()
    {
        // The 87-value tail. Reported honestly rather than guessed at or silently dropped — a
        // reader seeing "custom 61" can look it up; a reader seeing nothing cannot know there was
        // anything to look up.
        KillDescription.CustomKill(61).ShouldBe("custom 61");
    }

    [Test]
    public void DeathFlagsAreNamedIndividuallyAndCombine()
    {
        // TF_DEATH_DOMINATION 0x0001, TF_DEATH_ASSISTER_DOMINATION 0x0002, TF_DEATH_FIRST_BLOOD
        // 0x0010, TF_DEATH_GIBBED 0x0080 — a BIT FIELD, so more than one can be set and the
        // description has to say so.
        //
        // The combining case is the one that matters: a kill can be a domination AND a first blood,
        // and anything treating this word as an enumeration reports one of them.
        KillDescription.DeathFlags(0x0001).ShouldBe("domination");
        KillDescription.DeathFlags(0x0080).ShouldBe("gibbed");
        KillDescription.DeathFlags(0x0011).ShouldBe("domination, first blood");
    }

    [Test]
    public void NoFlagsDescribesNothing()
    {
        // The common case, and zero is "no bits set" rather than a named state — the same shape as
        // TF_FLAGINFO_HOME.
        KillDescription.DeathFlags(0).ShouldBeNull();
    }

    [Test]
    public void AnUnknownFlagBitIsReportedRatherThanDropped()
    {
        // Bit 15 is not one of the eleven declared flags. Reporting it keeps the description
        // faithful to the data: silently dropping unknown bits would make a future TF2 update
        // invisible here rather than merely unnamed.
        KillDescription.DeathFlags(0x8000).ShouldBe("flag 0x8000");
    }
}
