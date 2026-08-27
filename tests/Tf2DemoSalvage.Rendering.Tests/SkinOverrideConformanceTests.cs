using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Skins the client computes rather than receives, and the two overrides applied after.
/// </summary>
/// <remarks>
/// **Found by checking whether a hardcode had become redundant, and it had not.** After `m_nSkin`
/// was retained by the scene layer, the obvious next move was to delete the line in `MainForm` that
/// derives a player's skin from their team. That would have been wrong.
///
/// `c_tf_player.cpp:712-719` assigns `m_nSkin` from `m_iTeam` **on the client**, while setting the
/// model, and the prediction data marks the field `FTYPEDESC_PRIVATE`. For a player it is client
/// state derived from team, not a value the server sends — so deriving it here matches the client
/// exactly. Props are the opposite: a capture point's skin comes from ownership on the server
/// (`team_control_point.cpp:569`) and must be read off the entity.
///
/// **One entity property, two completely different provenances.** That is the kind of thing that
/// makes "just read the field everywhere" wrong, and it is only visible by reading the client.
///
/// What this class actually records is the part that is missing: the client applies two further
/// overrides immediately after the team assignment, and neither is implemented.
/// </remarks>
public sealed class SkinOverrideConformanceTests
{
    /// <summary>Where the client computes a player's skin.</summary>
    private const string TfPlayer = "src/game/client/tf/c_tf_player.cpp";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void SkinOverride_APlayersSkin_IsComputedFromTeam()
    {
        // The passing half, and the reason the MainForm hardcode stays. Pinned so that a future
        // reader who notices "we retain m_nSkin now, why is this derived?" finds the answer already
        // asserted rather than repeating the investigation.
        string player = SourceSdk.Text(TfPlayer).ShouldNotBeNull();

        player.ShouldContain("if ( m_iTeam == TF_TEAM_RED )");
        player.ShouldContain("DEFINE_PRED_FIELD( m_nSkin, FIELD_INTEGER, FTYPEDESC_OVERRIDE | FTYPEDESC_PRIVATE )");

        // **Ours, which this test had no way to reach until the rule was extracted.** Both teams,
        // because one of them alone is satisfied by a function that returns a constant — and
        // returning 0 for everything is exactly the defect the rule exists to prevent, since the
        // model's first family is red and both teams would draw in it.
        PlayerSkin.ForTeam(SceneTeams.Red).ShouldBe(0);
        PlayerSkin.ForTeam(SceneTeams.Blu).ShouldBe(1);

        // **And the case Valve's expression does not have.** A player entity can exist before the
        // demo says which side they are on, so the team is nullable here; Valve's form would send
        // that to BLU and make every joining player flash blue. Pinned as a deliberate divergence
        // rather than left to look like a transcription slip.
        PlayerSkin.ForTeam(null).ShouldBe(0);
    }

    [Test]
    public void SkinOverride_TheZombieOverride_RewritesTheSkinPerClass()
    {
        // c_tf_player.cpp:725 — AdjustSkinIndexForZombie( m_iClass, m_nSkin ), applied straight
        // after the team assignment and gated on BRenderAsZombie().
        //
        // **Per CLASS, not a single alternate skin.** The signature takes the class index and
        // rewrites the skin in place, so a Halloween zombie is a different family per class rather
        // than one shared zombie material — an implementation that maps "zombie" to one skin index
        // is wrong for eight of the nine classes.
        string player = SourceSdk.Text(TfPlayer).ShouldNotBeNull();

        player.ShouldContain("AdjustSkinIndexForZombie( m_iClass, m_nSkin )");

        // The gap, with its control, so this marker fails when the override lands (D45).
        SchemaGap.AnyProductionAssemblyMentions(SchemaGap.KnownPresent).ShouldBeTrue(
            "the search cannot find a name that is demonstrably compiled in");

        SchemaGap.AnyProductionAssemblyMentions("Zombie").ShouldBeFalse(
            "a zombie skin path now exists — replace this marker with a parity test, and check it "
            + "is per CLASS rather than one shared index");

        Assert.Ignore(
            "the zombie skin override is not implemented. AdjustSkinIndexForZombie rewrites the " +
            "skin per CLASS (c_tf_player.cpp:725), so Halloween players draw in their ordinary " +
            "team skin here.");
    }

    [Test]
    public void SkinOverride_TheGoldenWrenchRagdoll_IsRecognisedByTwoRoutes()
    {
        // The gold ragdoll check, and Valve's comment on it is the finding:
        //
        //   // We check against new-style (special flag to indicate goldification) and old style
        //   // (custom damage type) to maintain old demos involving the golden wrench
        //
        // **Valve kept a second, obsolete detection path specifically so that old DEMOS keep
        // rendering correctly.** That is this project's entire premise appearing inside the game's
        // own client code — the engine authors thought about demo compatibility across a format
        // change and left the old route in.
        //
        // Worth pinning for that reason more than for the effect itself.
        string player = SourceSdk.Text(TfPlayer).ShouldNotBeNull();

        player.ShouldContain("m_bGoldRagdoll || m_iDamageCustom == TF_DMG_CUSTOM_GOLD_WRENCH");
        player.ShouldContain("to maintain old demos involving the golden wrench");

        // The gap, with its control, so this marker fails when the override lands (D45).
        SchemaGap.AnyProductionAssemblyMentions(SchemaGap.KnownPresent).ShouldBeTrue(
            "the search cannot find a name that is demonstrably compiled in");

        SchemaGap.AnyProductionAssemblyMentions("m_bGoldRagdoll").ShouldBeFalse(
            "the gold ragdoll is now detected — replace this marker with a parity test, and cover "
            + "BOTH of Valve's routes, since the old one exists precisely for old demos");

        Assert.Ignore(
            "the gold ragdoll override is not implemented. Note that Valve keeps TWO detection " +
            "routes for it explicitly to keep old demos rendering — the game's own client carries " +
            "backward compatibility for the same reason this project exists.");
    }
}
