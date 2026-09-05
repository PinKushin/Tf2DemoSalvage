using System.Diagnostics.CodeAnalysis;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// The five condition bitfields a TF player networks, read the way the engine reads them.
/// </summary>
/// <param name="Cond"><c>m_nPlayerCond</c>, conditions 0..31.</param>
/// <param name="Ex"><c>m_nPlayerCondEx</c>, conditions 32..63.</param>
/// <param name="Ex2"><c>m_nPlayerCondEx2</c>, conditions 64..95.</param>
/// <param name="Ex3"><c>m_nPlayerCondEx3</c>, conditions 96..127.</param>
/// <param name="Ex4"><c>m_nPlayerCondEx4</c>, conditions 128..159.</param>
/// <remarks>
/// **<c>CTFPlayerShared::InCond</c>, <c>tf_player_shared.cpp:1209</c>**, and its
/// <c>CConditionVars</c> constructor at <c>:1041</c>, which picks the variable by range and
/// subtracts that range's base to get the bit. Five variables rather than one because TF has long
/// since passed 32 conditions — 31 of `DT_TFPlayerShared`'s 66 fields live past the first.
///
/// **This project read none of them.** `docs/WIRE-COVERAGE.md` reports `DT_TFPlayerShared` at 0 of
/// 66, and the owner's recording carries `m_nPlayerCond` through `m_nPlayerCondEx3` — measured on
/// the demo, not assumed from the SDK.
///
/// **Deliberately NOT implemented, and named rather than omitted:** the other half of the engine's
/// first line, <c>if ( eCond &lt; 32 &amp;&amp; m_ConditionList.InCond( eCond ) ) return true;</c>.
/// `m_ConditionList` is a networked `CUtlVector` of per-condition records rather than a bitfield;
/// the bit is the path the engine falls through to and the one this recording populates. A
/// condition set ONLY in the list reads as absent here.
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification =
        "Ex, Ex2, Ex3 and Ex4 are Valve's own names for these fields — m_nPlayerCondEx and "
        + "the rest, tf_player_shared.cpp:1099. Renaming them to the analyzer's numeric "
        + "convention would break the one property that matters here: a grep for the wire "
        + "name finds the code that reads it. docs/memory/wire-names-are-strings.md.")]
public readonly record struct PlayerConditions(int Cond, int Ex, int Ex2, int Ex3, int Ex4)
{
    /// <summary><c>TF_COND_ZOOMED</c>, <c>tf_shareddefs.h:691</c> — a scoped sniper rifle.</summary>
    /// <remarks>
    /// **It selects a different ATTACK gesture, not just a viewmodel.**
    /// `CTFPlayerAnimState::DoAnimationEvent` fires `ACT_MP_ATTACK_STAND_PRIMARYFIRE_DEPLOYED` for
    /// a zoomed sniper and the ordinary stand activity otherwise
    /// (`tf_playeranimstate.cpp:1013`), so a reader that ignores the zoom plays the wrong
    /// animation for every scoped shot.
    /// </remarks>
    public const int Zoomed = 1;

    /// <summary><c>TF_COND_DISGUISED</c>, <c>tf_shareddefs.h:693</c>.</summary>
    public const int Disguised = 3;

    /// <summary><c>TF_COND_DISGUISING</c>, <c>tf_shareddefs.h:692</c> — mid-disguise, not yet one.</summary>
    public const int Disguising = 2;

    /// <summary><c>TF_COND_DISGUISED_AS_DISPENSER</c>, <c>tf_shareddefs.h:739</c>.</summary>
    public const int DisguisedAsDispenser = 49;

    /// <summary><c>TF_COND_BURNING</c>, <c>tf_shareddefs.h:712</c> — alight (B336).</summary>
    /// <remarks>
    /// **What the client does with it is derive a TIME.** `CTFPlayerShared::OnAddBurning` sets
    /// `m_flBurnEffectStartTime = gpGlobals->curtime` when the bit is ADDED
    /// (`tf_player_shared.cpp:7306`) and `OnRemoveBurning` clears it (`:6884`); nothing networks
    /// that time. `CProxyBurnLevel` then ramps `$detailblendfactor` from it, so the fire overlay's
    /// whole shape hangs off the tick this bit turns on.
    /// </remarks>
    public const int Burning = 22;

    /// <summary><c>TF_COND_URINE</c>, <c>tf_shareddefs.h:714</c> — jarate'd (B336).</summary>
    /// <remarks>
    /// `CProxyUrineLevel` multiplies the player by `(6,9,2)` on RED and `(7,5,1)` on BLU while this
    /// holds, and by white otherwise — so the proxy runs on 7,570 shipped materials and shows
    /// nothing at all until somebody is hit.
    /// </remarks>
    public const int Urine = 24;

    /// <summary>How many conditions one variable carries.</summary>
    private const int PerVariable = 32;

    /// <summary>Whether a condition is set.</summary>
    /// <param name="condition">An <c>ETFCond</c> value.</param>
    /// <returns>Whether its bit is set in the variable that carries it.</returns>
    /// <remarks>
    /// **Unsigned shift, because these are 32-bit fields and bit 31 is a real condition.** Testing
    /// with a signed 1 works for every bit but the top one, where the shift produces
    /// <c>int.MinValue</c> and a naive comparison misreads it — the family
    /// <c>docs/memory/numeric-decoding-traps.md</c> records, where a wrong answer arrives as a
    /// plausible number rather than an error.
    /// </remarks>
    public bool Has(int condition)
    {
        if (condition < 0)
        {
            return false;
        }

        int variable = condition / PerVariable;
        int bit = condition % PerVariable;

        int bits = variable switch
        {
            0 => Cond,
            1 => Ex,
            2 => Ex2,
            3 => Ex3,
            4 => Ex4,

            // Past TF_COND_LAST's range. Answering false rather than throwing: a demo from a
            // future build may carry a condition this reader has never heard of, and refusing to
            // draw the frame over it would be worse than not knowing about it.
            _ => 0,
        };

        return (unchecked((uint)bits) & (1u << bit)) != 0u;
    }
}
