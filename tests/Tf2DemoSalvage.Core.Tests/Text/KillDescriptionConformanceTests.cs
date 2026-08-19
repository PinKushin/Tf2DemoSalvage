using System.Collections.Generic;

using Tf2DemoSalvage.Core.Text;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// That the names <see cref="KillDescription"/> prints match what Valve declares.
/// </summary>
/// <remarks>
/// **`KillDescription` holds transcribed values, which this project normally refuses.** The
/// alternative was worse: it needs a name for a handful of the 87 custom kill types and the eleven
/// death flags, and reading the SDK at runtime would make the CLI depend on a checkout being
/// present.
///
/// So the values are transcribed AND derived — the table lives in the product, and this test
/// computes the same thing from `tf_shareddefs.h` and compares. That is the pattern the rest of the
/// suite uses for engine constants, applied to a lookup table rather than a single number.
///
/// **The failure this catches is a rename or a renumber**, which for a kill feed means confidently
/// printing "backstab" over a headshot.
/// </remarks>
public sealed class KillDescriptionConformanceTests
{
    /// <summary>Where the death constants are declared.</summary>
    private const string SharedDefs = "src/game/shared/tf/tf_shareddefs.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void KillDescription_TheNamedCustomKills_SitAtValvesValues()
    {
        IReadOnlyDictionary<string, int> custom =
            SourceSdk.Enumerators(SharedDefs, "ETFDmgCustom");

        // Each pairing checked in the direction that matters: given Valve's value, does this project
        // print the right word? Asserting the reverse would pass if the table held extra entries.
        KillDescription.CustomKill(custom["TF_DMG_CUSTOM_HEADSHOT"]).ShouldBe("headshot");
        KillDescription.CustomKill(custom["TF_DMG_CUSTOM_BACKSTAB"]).ShouldBe("backstab");
        KillDescription.CustomKill(custom["TF_DMG_CUSTOM_BURNING"]).ShouldBe("burning");
        KillDescription.CustomKill(custom["TF_DMG_CUSTOM_BURNING_FLARE"]).ShouldBe("burning flare");
        KillDescription.CustomKill(custom["TF_DMG_CUSTOM_MINIGUN"]).ShouldBe("minigun");
        KillDescription.CustomKill(custom["TF_DMG_CUSTOM_SUICIDE"]).ShouldBe("suicide");

        // And the ordinary case, which is the one that must NOT acquire a name.
        KillDescription.CustomKill(custom["TF_DMG_CUSTOM_NONE"]).ShouldBeNull();
    }

    [Test]
    public void KillDescription_EveryDeathFlagValveDeclares_IsNamedHere()
    {
        // **Swept, so a flag added upstream shows up as a gap rather than as an unnamed bit in
        // someone's kill feed.** This is the assertion that would have to change if TF2 added a
        // twelfth flag, which is the point — the sweep is the notification.
        IReadOnlyDictionary<string, int> defs = SourceSdk.Constants(SharedDefs);

        foreach ((string name, int value) in defs)
        {
            // The duration that shares the prefix without being a flag. Excluded by name because it
            // is excluded by meaning, not because it is inconvenient.
            if (!name.StartsWith("TF_DEATH_", System.StringComparison.Ordinal) ||
                name == "TF_DEATH_ANIMATION_TIME")
            {
                continue;
            }

            // A named flag never renders as "flag 0x…", which is what an unnamed bit produces.
            // Compared with ShouldSatisfyAllConditions so the failure names the flag: Shouldly's
            // ShouldNotStartWith takes a Case as its third argument, not a message.
            string described = KillDescription.DeathFlags(value).ShouldNotBeNull();

            described.StartsWith("flag 0x", System.StringComparison.Ordinal)
                .ShouldBeFalse($"{name} (0x{value:X4}) has no name in KillDescription");
        }
    }
}
