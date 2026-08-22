using System.Collections.Generic;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// Ammo and team standing — the last of the plainly-citable gaps.
/// </summary>
/// <remarks>
/// **Eighteenth batch, and the pickings are thinner than the earlier ones**, which is itself worth
/// recording: the SDK-citable surface of what this project does not implement has been largely
/// swept. What remains here is small and concrete.
///
/// The ammo entry is the interesting one, and not because ammo is interesting. It is a case where
/// **the identifier lies and Valve's own comments say so** — two adjacent constants, both 32, one
/// annotated <c>// ???</c> and the other <c>// not really slots</c>.
/// </remarks>
public sealed class PlayerResourceConformanceTests
{
    /// <summary>Where the ammo dimensions are declared.</summary>
    private const string SharedDefs = "src/game/shared/shareddefs.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void PlayerResource_TheAmmoArray_IsIndexedByTypeThoughSizedBySlots()
    {
        // shareddefs.h:124-125, adjacent lines:
        //
        //   #define MAX_AMMO_TYPES  32    // ???
        //   #define MAX_AMMO_SLOTS  32    // not really slots
        //
        // **Valve disowns both names in the comments**, and the array on the player is
        // m_iAmmo[MAX_AMMO_SLOTS] while the accessor is GetAmmoCount( int iAmmoIndex ) — an ammo
        // TYPE, not a weapon slot. A reader that indexes by weapon slot gets a number, from the
        // wrong element, for every weapon.
        //
        // **And the two constants are both 32, so the size cannot tell them apart.** Same shape as
        // the control-point TEAM_ARRAY layout: where two candidate dimensions are equal, arithmetic
        // is blind and only the code that indexes the array settles it. That is now twice in this
        // project, which makes it a pattern rather than a coincidence — when two dimensions match,
        // stop trying to derive and go read the accessor.
        IReadOnlyDictionary<string, int> defs = SourceSdk.Constants(SharedDefs);

        defs["MAX_AMMO_TYPES"].ShouldBe(defs["MAX_AMMO_SLOTS"]);

        string text = SourceSdk.Text(SharedDefs).ShouldNotBeNull();

        // The comments are the evidence that the names are not to be trusted, so they are the
        // assertion. If Valve ever cleans them up, this fails and the reasoning above needs
        // rewriting rather than quietly continuing to be cited.
        text.ShouldContain("#define\tMAX_AMMO_TYPES\t32\t\t// ???");
        text.ShouldContain("#define MAX_AMMO_SLOTS  32\t\t// not really slots");

        string character = SourceSdk
            .Text("src/game/shared/basecombatcharacter_shared.cpp").ShouldNotBeNull();

        character.ShouldContain("int CBaseCombatCharacter::GetAmmoCount( int iAmmoIndex ) const");

        Assert.Ignore(
            "ammo is not decoded. m_iAmmo is sized by MAX_AMMO_SLOTS and indexed by ammo TYPE " +
            "(GetAmmoCount takes an iAmmoIndex), and both constants are 32 — so indexing by weapon " +
            "slot reads the wrong element and never goes out of range.");
    }

    [Test]
    public void PlayerResource_TheTeamEntity_CarriesScoreRoundsAndRoster()
    {
        // c_team.cpp:33-40 — m_iTeamNum, m_iScore, m_iRoundsWon, m_szTeamname, and a networked
        // player array.
        //
        // **Score and rounds won are separate**, which matters for reading a match: on a
        // round-based map the scoreboard number players remember is m_iRoundsWon, while m_iScore is
        // the generic team score the base class carries. Reporting one as the other is wrong in a
        // way that looks entirely plausible.
        //
        // The roster is networked too, so team membership does not have to be inferred by walking
        // player entities and reading their team number.
        string team = SourceSdk.Text("src/game/client/c_team.cpp").ShouldNotBeNull();

        foreach (string field in new[] { "m_iTeamNum", "m_iScore", "m_iRoundsWon", "m_szTeamname" })
        {
            team.ShouldContain($"RECVINFO({field})");
        }

        // The gap, with its control, so this marker fails when the team entity is read (D45).
        SchemaGap.AnyProductionAssemblyMentions(SchemaGap.KnownPresent).ShouldBeTrue(
            "the search cannot find a name that is demonstrably compiled in");

        SchemaGap.AnyProductionAssemblyMentions("m_iRoundsWon").ShouldBeFalse(
            "team standing is now decoded — replace this marker with a parity test against the " +
            "RECVINFO list above");

        Assert.Ignore(
            "team standing is not decoded. Score and rounds won are separate fields on the team " +
            "entity (c_team.cpp:33) — on a round-based map the number players remember is " +
            "m_iRoundsWon — and the roster is networked rather than needing to be inferred.");
    }
}
