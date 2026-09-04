using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Whether a corpse plays a death animation, and which one (B323, D136).
/// </summary>
/// <remarks>
/// **Three gates in series, and the famous one is the middle.**
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
/// **The draw is REPRODUCED, not put to anyone as a choice** (D136). The owner's words on being
/// asked which way to resolve it: *"you should of done it valves way"*. The engine draws a random
/// number; so does this. A 25/75 split is not an approximation of the engine, it is the engine.
///
/// **The seed is the one thing forced on us, and it is a consequence of a capability the client
/// lacks.** `RandomFloat` reads a running stream that a demo does not record. This project can seek,
/// so the draw is keyed to the corpse: scrubbing back over a death shows the same body it showed
/// going forward. A stream would show a different one each pass.
///
/// **Measured before being built** (B316): only about one corpse in a hundred reaches all three
/// gates, because eligibility alone excludes every death that is not a headshot, a decapitation or a
/// backstab — 0 of 159 in a competitive match with no sniper or spy. It is implemented because
/// parity is not a popularity contest, and because a frag video is made of exactly these deaths.
/// </remarks>
public static class RagdollDeath
{
    /// <summary>The animation a headshot, a decapitation or a barbarian swing plays.</summary>
    /// <remarks>
    /// `pRagdoll-&gt;LookupSequence( "primary_death_headshot" )`, `tf_player_shared.cpp:13448`. Four
    /// damage types share it — TF2 ships two death animations, not one per kind of death.
    /// </remarks>
    public const string HeadshotSequence = "primary_death_headshot";

    /// <summary>The animation a backstab plays.</summary>
    /// <remarks>`tf_player_shared.cpp:13451`.</remarks>
    public const string BackstabSequence = "primary_death_backstab";

    /// <summary>Which death animation this corpse plays, if any.</summary>
    /// <param name="corpse">The corpse.</param>
    /// <returns>A sequence label to look up on the model, or null for none.</returns>
    public static string? SequenceFor(SceneRagdoll corpse)
    {
        // Gate 1 — `GetSequenceForDeath`, a switch with two cases and no default.
        string? wanted = corpse.DamageCustom switch
        {
            Headshot or Decapitation or HeadshotDecapitation or BarbarianSwing => HeadshotSequence,
            Backstab => BackstabSequence,
            _ => null,
        };

        if (wanted is null)
        {
            return null;
        }

        // Gate 2 — the coin flip, which the three TAUNT kills are excluded from. A barbarian swing
        // is eligible AND excluded, so it animates every time; that pairing is easy to miss because
        // the two lists are in different functions.
        if (corpse.DamageCustom is not (BarbarianSwing or GuitarSmash or GuitarRiff) &&
            Draw(corpse) > 0.25f)
        {
            return null;
        }

        // Gate 3 — the ground. `bPlayDeathInAir` is the dissolve case, which is its own branch and
        // is not implemented here, so the veto is unconditional for an airborne corpse.
        return corpse.OnGround ? wanted : null;
    }

    /// <summary>The corpse's own draw, standing in for <c>RandomFloat( 0, 1 )</c>.</summary>
    /// <param name="corpse">The corpse.</param>
    /// <returns>A number in [0, 1).</returns>
    /// <remarks>
    /// **Keyed on the slot AND the serial, which is what makes it stable across a seek** and
    /// distinct between two corpses that reused one entity index. Hashing rather than a sequence
    /// generator because the value has to be recomputable from the corpse alone, at any tick, in any
    /// order — a viewer may draw tick 50,000 before tick 10.
    /// </remarks>
    private static float Draw(SceneRagdoll corpse)
    {
        // A small integer hash. The constant is the 32-bit FNV prime's near neighbour used widely
        // for this; nothing here depends on which mixer it is, only that it is stable and spreads.
        uint mixed = (uint)((corpse.EntityIndex * 73856093) ^ (corpse.Serial * 19349663));

        mixed ^= mixed >> 16;
        mixed *= 2246822519u;
        mixed ^= mixed >> 13;
        mixed *= 3266489917u;
        mixed ^= mixed >> 16;

        return mixed / (float)uint.MaxValue;
    }

    /// <summary><c>TF_DMG_CUSTOM_HEADSHOT</c>.</summary>
    /// <remarks>
    /// Counted off `ETFDmgCustom`'s enumerators (`tf_shareddefs.h:1181`), comments excluded — taking
    /// line offsets instead is wrong for every value after the first comment in the block.
    /// </remarks>
    private const int Headshot = 1;

    /// <summary><c>TF_DMG_CUSTOM_BACKSTAB</c>.</summary>
    private const int Backstab = 2;

    /// <summary><c>TF_DMG_CUSTOM_DECAPITATION</c>.</summary>
    private const int Decapitation = 20;

    /// <summary><c>TF_DMG_CUSTOM_TAUNTATK_BARBARIAN_SWING</c> — eligible AND excluded from the draw.</summary>
    private const int BarbarianSwing = 24;

    /// <summary><c>TF_DMG_CUSTOM_TAUNTATK_ENGINEER_GUITAR_SMASH</c>.</summary>
    private const int GuitarSmash = 33;

    /// <summary><c>TF_DMG_CUSTOM_HEADSHOT_DECAPITATION</c>.</summary>
    private const int HeadshotDecapitation = 51;

    /// <summary><c>TF_DMG_CUSTOM_TAUNTATK_ALLCLASS_GUITAR_RIFF</c>.</summary>
    private const int GuitarRiff = 62;
}
