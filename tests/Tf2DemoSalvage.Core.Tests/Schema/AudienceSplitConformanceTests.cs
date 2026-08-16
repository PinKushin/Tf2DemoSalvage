using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// The mechanism behind the local/shared split, read rather than inferred from examples.
/// </summary>
/// <remarks>
/// **Sixteenth batch, and it replaces four anecdotes with the rule that produced them.** The
/// previous batch found übercharge, disguise and cloak timing each split by audience and treated
/// them as three findings. They are three uses of one mechanism, and the mechanism is eighteen lines
/// of published code in <c>basecombatweapon_shared.cpp</c>.
///
/// **Reading it settles a question the examples could not: what happens when nobody owns the
/// entity.** Both proxies <c>return NULL</c> in that case, so an unowned weapon sends *neither*
/// table. That is a third state the examples never showed, and it is the one that matters most for a
/// parser — "absent" is not "the other table has it".
///
/// This is the difference the project's own guidance keeps pointing at: measuring the corpus can
/// only find data that is wrong, and cannot find a case the corpus never contains. A dropped
/// medigun is not in any committed demo, and the code says exactly what it does.
/// </remarks>
public sealed class AudienceSplitConformanceTests
{
    /// <summary>Where both audience proxies are defined.</summary>
    private const string WeaponShared = "src/game/shared/basecombatweapon_shared.cpp";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void TheTwoAudienceProxiesAreExactlyComplementary()
    {
        // basecombatweapon_shared.cpp:2739 and :2761.
        //
        //   local:     pRecipients->SetOnly( pPlayer->GetClientIndex() );
        //   non-local: pRecipients->SetAllRecipients();
        //              pRecipients->ClearRecipient( pPlayer->GetClientIndex() );
        //
        // **Disjoint and covering: no client ever receives both, and every client receives one.**
        // That is what makes the two sub-tables a genuine either/or rather than an optimisation with
        // overlap, and it is why a POV demo and an STV demo of the same weapon carry different
        // fields rather than the same fields at different times.
        //
        // Pinned as the two recipient calls rather than as prose, because the prose comment above
        // each ("Only send this chunk of data to the player carrying this weapon") states intent and
        // these state behaviour.
        string source = SourceSdk.Text(WeaponShared).ShouldNotBeNull();

        source.ShouldContain("pRecipients->SetOnly( pPlayer->GetClientIndex() );");
        source.ShouldContain("pRecipients->SetAllRecipients();");
        source.ShouldContain("pRecipients->ClearRecipient( pPlayer->GetClientIndex() );");
    }

    [Test]
    public void AnUnownedWeaponSendsNeitherHalfOfTheSplit()
    {
        // **The case the examples could not show, and the one a parser has to handle.**
        //
        // Both proxies end the same way:
        //
        //   CBasePlayer *pPlayer = ToBasePlayer( pWeapon->GetOwner() );
        //   if ( pPlayer ) { ...; return (void*)pVarData; }
        //   return NULL;
        //
        // No owner means no recipients for EITHER sub-table. A weapon lying on the ground therefore
        // carries neither the full-precision field nor the quantised one — so "the local table is
        // absent, read the shared one" is wrong as a fallback rule, and there is a third outcome:
        // the value is simply not on the wire.
        //
        // Which, per this project's own sentinel rule, means the DEFAULT — not zero chosen as a
        // stand-in, and not "unknown" conflated with an answer.
        string source = SourceSdk.Text(WeaponShared).ShouldNotBeNull();

        int local = source.IndexOf(
            "void* SendProxy_SendLocalWeaponDataTable", StringComparison.Ordinal);
        int nonLocal = source.IndexOf(
            "void* SendProxy_SendNonLocalWeaponDataTable", StringComparison.Ordinal);

        local.ShouldBeGreaterThan(0);
        nonLocal.ShouldBeGreaterThan(local);

        // Each proxy body ends in `return NULL;` before its REGISTER_ line. Measured within each
        // body rather than over the file, so a `return NULL` belonging to some other function
        // cannot satisfy this.
        string localBody = source[local..nonLocal];
        int registerNonLocal = source.IndexOf(
            "REGISTER_SEND_PROXY_NON_MODIFIED_POINTER( SendProxy_SendNonLocalWeaponDataTable )",
            StringComparison.Ordinal);
        string nonLocalBody = source[nonLocal..registerNonLocal];

        localBody.ShouldContain("return NULL;");
        nonLocalBody.ShouldContain("return NULL;");

        Assert.Ignore(
            "the no-owner case is not handled, because the split is not decoded at all. Both " +
            "proxies return NULL without an owner, so a dropped weapon sends NEITHER sub-table — " +
            "'fall back to the shared one' is wrong, and absent means the default.");
    }

    [Test]
    public void TheSplitIsAGeneralMechanismRatherThanAMedigunQuirk()
    {
        // Counted from the SDK rather than listed by hand, so the number cannot go stale the way a
        // transcribed list does. This is the same instrument idea as generating a conformance
        // denominator: what is being measured is COVERAGE of a mechanism, and a hand-written list
        // measures only what its author happened to know.
        //
        // Every hit is an entity whose state a SourceTV recording sees differently from the player's
        // own recording.
        IEnumerable<string> sources = SourceSdk.Files("src/game/shared", "*.cpp")
            .Concat(SourceSdk.Files("src/game/shared/tf", "*.cpp"));

        int uses = sources
            .Select(SourceSdk.Text)
            .Where(text => text is not null)
            .Sum(text => CountOccurrences(text!, "SendProxy_SendLocal"));

        // Well above the three examples the previous batch found by hand. Asserted as a floor rather
        // than an exact count: Valve adding another split is not a failure, and the point is that
        // this is systemic.
        uses.ShouldBeGreaterThan(5);

        Assert.Ignore(
            $"the audience split is not modelled. {uses} uses of the local-send proxy across " +
            "shared game code — this is a general mechanism, so every one of them is a field an " +
            "STV recording sees differently from the player's own.");
    }

    /// <summary>Counts non-overlapping occurrences of a literal.</summary>
    private static int CountOccurrences(string text, string needle)
    {
        int count = 0;
        int at = text.IndexOf(needle, StringComparison.Ordinal);

        while (at >= 0)
        {
            count++;
            at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
