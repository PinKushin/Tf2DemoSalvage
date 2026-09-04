using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Which death animation a corpse plays, and whether it plays one at all (B323, D136).
/// </summary>
/// <remarks>
/// **Three gates in series, and only the middle one is famous.**
///
/// <code>
/// iDeathSeq = pPlayer-&gt;m_Shared.GetSequenceForDeath( this, m_bBurning, m_iDamageCustom );
///
/// if ( iDeathSeq &gt; -1 &amp;&amp; (m_iDamageCustom != TF_DMG_CUSTOM_TAUNTATK_BARBARIAN_SWING) &amp;&amp;
///     (m_iDamageCustom != TF_DMG_CUSTOM_TAUNTATK_ENGINEER_GUITAR_SMASH) &amp;&amp;
///     (m_iDamageCustom != TF_DMG_CUSTOM_TAUNTATK_ALLCLASS_GUITAR_RIFF) )
/// {
///     if ( !m_bIceRagdoll &amp;&amp; !tf_always_deathanim.GetBool() &amp;&amp; (RandomFloat( 0, 1 ) &gt; 0.25f) )
///         iDeathSeq = -1;
/// }
///
/// bool bPlayDeathAnim = cl_ragdoll_physics_enable.GetBool() &amp;&amp; (iDeathSeq &gt; -1) &amp;&amp; pPlayer;
///
/// if ( !m_bOnGround &amp;&amp; bPlayDeathAnim &amp;&amp; !bPlayDeathInAir )
///     bPlayDeathAnim = false;
/// </code>
///
/// `c_tf_player.cpp:815-846`. Read-from-source.
///
/// 1. **Eligibility** — `GetSequenceForDeath` is a `switch` on `m_iDamageCustom` with two cases and
///    no default (`tf_player_shared.cpp:13441-13455`), so all but headshots, decapitations and
///    backstabs return -1 and never reach the rest.
/// 2. **The coin flip**, which discards three quarters — and which the three TAUNT kills are
///    excluded from, so those always animate.
/// 3. **The ground**, which vetoes it in mid-air.
///
/// **The draw is reproduced rather than asked about** (D136): the engine draws a random number, so
/// this draws one. The only forced adaptation is that it is SEEDED per corpse, because this project
/// can seek and the client could not — scrubbing back over a death has to show the same one twice.
/// </remarks>
public sealed class RagdollDeathConformanceTests
{
    [Test]
    public void SequenceFor_ForADeathThatIsNotAHeadshotOrBackstab_IsNone()
    {
        // A plain rocket. `GetSequenceForDeath`'s switch has no case for it.
        RagdollDeath.SequenceFor(Corpse(damage: 28)).ShouldBeNull();
    }

    /// <remarks>
    /// **Both names, because one cannot tell a lookup from a constant.** A backstab takes a
    /// different animation from a headshot, and an implementation returning whichever it wrote first
    /// passes a test of the other.
    /// </remarks>
    [Test]
    public void SequenceFor_ForAHeadshotAndABackstab_AreTheTwoDeathAnimations()
    {
        RagdollDeath.SequenceFor(Corpse(damage: 1, serial: Kept))
            .ShouldBe(RagdollDeath.HeadshotSequence);

        RagdollDeath.SequenceFor(Corpse(damage: 2, serial: Kept))
            .ShouldBe(RagdollDeath.BackstabSequence);
    }

    /// <remarks>
    /// **The decapitation variants take the HEADSHOT animation, not one of their own.** TF2 ships
    /// exactly two death animations; `GetSequenceForDeath` groups four damage types onto the first.
    /// </remarks>
    [Test]
    public void SequenceFor_ForADecapitation_TakesTheHeadshotAnimation()
    {
        RagdollDeath.SequenceFor(Corpse(damage: 20, serial: Kept))
            .ShouldBe(RagdollDeath.HeadshotSequence);

        RagdollDeath.SequenceFor(Corpse(damage: 51, serial: Kept))
            .ShouldBe(RagdollDeath.HeadshotSequence);
    }

    /// <remarks>
    /// **A barbarian swing animates every time**, because it is the one damage type that is both
    /// ELIGIBLE and excluded from the coin flip. Asserted across many seeds, since one seed cannot
    /// tell "always" from "lucky".
    /// </remarks>
    [Test]
    public void SequenceFor_ForABarbarianSwing_AlwaysAnimatesWhateverTheDraw()
    {
        for (int serial = 0; serial < 200; serial++)
        {
            RagdollDeath.SequenceFor(Corpse(damage: 24, serial: serial))
                .ShouldNotBeNull("a barbarian swing is excluded from the discard");
        }
    }

    /// <remarks>
    /// **Two of the three taunt exclusions are unreachable, and writing this test wrong is how that
    /// was found.** The coin flip is guarded by
    ///
    /// <code>
    /// if ( iDeathSeq &gt; -1 &amp;&amp; (m_iDamageCustom != TAUNTATK_BARBARIAN_SWING) &amp;&amp;
    ///     (m_iDamageCustom != TAUNTATK_ENGINEER_GUITAR_SMASH) &amp;&amp;
    ///     (m_iDamageCustom != TAUNTATK_ALLCLASS_GUITAR_RIFF) )
    /// </code>
    ///
    /// which reads as three damage types that always animate. But `GetSequenceForDeath` has no case
    /// for either guitar (`tf_player_shared.cpp:13441-13454`), so both fail the `iDeathSeq &gt; -1`
    /// test in the same condition and never reach the flip they are excused from. Only the barbarian
    /// swing appears in both lists.
    ///
    /// So an engineer's guitar smash and an all-class guitar riff play NO death animation at all —
    /// the exclusion protects them from a discard that could not have applied.
    /// </remarks>
    [Test]
    public void SequenceFor_ForAGuitarTauntKill_IsNoneDespiteTheExclusion()
    {
        for (int serial = 0; serial < 40; serial++)
        {
            RagdollDeath.SequenceFor(Corpse(damage: 33, serial: serial)).ShouldBeNull();
            RagdollDeath.SequenceFor(Corpse(damage: 62, serial: serial)).ShouldBeNull();
        }
    }

    /// <remarks>
    /// **A quarter, and measured across many corpses rather than asserted on one.** The engine keeps
    /// the animation when `RandomFloat( 0, 1 )` is NOT greater than 0.25, so about one in four. A
    /// single seed says nothing about a distribution; a wide margin here would pass against a
    /// constant, so the bound is tight enough to fail one.
    /// </remarks>
    [Test]
    public void SequenceFor_AcrossManyHeadshots_KeepsAboutAQuarter()
    {
        int kept = 0;

        for (int serial = 0; serial < 4000; serial++)
        {
            if (RagdollDeath.SequenceFor(Corpse(damage: 1, serial: serial)) is not null)
            {
                kept++;
            }
        }

        (kept / 4000d).ShouldBe(0.25d, 0.03d, "the engine keeps a quarter of eligible deaths");
    }

    /// <remarks>
    /// **The seed is the one forced adaptation** (D136). The client draws from a running stream and
    /// cannot seek; this project can, so scrubbing backwards over a death must show the same corpse
    /// it showed the first time. Asking twice for one corpse is the whole claim.
    /// </remarks>
    [Test]
    public void SequenceFor_AskedTwiceForOneCorpse_AnswersTheSame()
    {
        for (int serial = 0; serial < 50; serial++)
        {
            RagdollDeath.SequenceFor(Corpse(damage: 1, serial: serial))
                .ShouldBe(RagdollDeath.SequenceFor(Corpse(damage: 1, serial: serial)));
        }
    }

    /// <remarks>
    /// **Two corpses of one death type must not agree by construction**, or the seeding has
    /// collapsed to a constant and the test above passes trivially. The distribution test would
    /// catch an always-yes and an always-no; this catches a seed that ignores the corpse.
    /// </remarks>
    [Test]
    public void SequenceFor_AcrossCorpses_DoesNotAnswerTheSameForAll()
    {
        bool anyKept = false;
        bool anyDropped = false;

        for (int serial = 0; serial < 200; serial++)
        {
            if (RagdollDeath.SequenceFor(Corpse(damage: 1, serial: serial)) is null)
            {
                anyDropped = true;
            }
            else
            {
                anyKept = true;
            }
        }

        anyKept.ShouldBeTrue();
        anyDropped.ShouldBeTrue();
    }

    /// <remarks>
    /// **In the air there is no death animation at all** — `if ( !m_bOnGround &amp;&amp; bPlayDeathAnim
    /// &amp;&amp; !bPlayDeathInAir ) bPlayDeathAnim = false;`. Valve's own comment says why: *"Don't play
    /// most death anims in the air (headshot, etc)"*. A corpse that would otherwise have animated is
    /// the input, so this cannot pass by the draw having discarded it anyway.
    /// </remarks>
    [Test]
    public void SequenceFor_ForAnAirborneCorpse_IsNoneEvenWhenTheDrawKeptIt()
    {
        SceneRagdoll airborne = Corpse(damage: 1, serial: Kept) with { OnGround = false };

        RagdollDeath.SequenceFor(airborne).ShouldBeNull();

        // The control: the same corpse on the ground DOES animate, or the case above proves nothing.
        RagdollDeath.SequenceFor(airborne with { OnGround = true }).ShouldNotBeNull();
    }

    /// <summary>A serial whose draw keeps the animation, found once and reused.</summary>
    /// <remarks>
    /// **Chosen by asking the implementation, not by hoping.** Roughly one serial in four keeps it,
    /// so a hardcoded guess would make several tests here depend on an accident of the hash.
    /// </remarks>
    private static int Kept
    {
        get
        {
            for (int serial = 0; serial < 1000; serial++)
            {
                if (RagdollDeath.SequenceFor(Corpse(damage: 1, serial: serial)) is not null)
                {
                    return serial;
                }
            }

            return 0;
        }
    }

    private static SceneRagdoll Corpse(int damage, int serial = 1) =>
        new(EntityIndex: 40,
            Serial: serial,
            PlayerClass: 5,
            Team: SceneTeams.Red,
            X: 0f,
            Y: 0f,
            Z: 0f,
            Gib: false,
            Burning: false,
            FeignDeath: false,
            WasDisguised: false,
            FirstTick: 100,
            LastTick: 200,
            Yaw: 0f,
            DamageCustom: damage,
            OnGround: true);
}
