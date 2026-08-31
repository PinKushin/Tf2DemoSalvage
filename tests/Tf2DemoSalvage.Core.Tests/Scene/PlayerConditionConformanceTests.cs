using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A player condition is one bit of five networked variables, chosen by the condition's number.
/// </summary>
/// <remarks>
/// **`CTFPlayerShared::InCond`, `tf_player_shared.cpp:1209`:**
///
/// <code>
///   bool CTFPlayerShared::InCond( ETFCond eCond ) const
///   {
///       // Old condition system, only used for the first 32 conditions
///       if ( eCond &lt; 32 &amp;&amp; m_ConditionList.InCond( eCond ) )
///           return true;
///
///       CONDITION_VARS( cPlayerCond, eCond );
///       return (cPlayerCond.CondVar() &amp; cPlayerCond.CondBit()) != 0;
///   }
/// </code>
///
/// and `CConditionVars`' constructor (`:1041`) picks the variable by range, subtracting the range's
/// base to get the bit:
///
/// <code>
///   eCond &gt;= 128 -&gt; m_nPlayerCondEx4, bit eCond - 128
///   eCond &gt;=  96 -&gt; m_nPlayerCondEx3, bit eCond -  96
///   eCond &gt;=  64 -&gt; m_nPlayerCondEx2, bit eCond -  64
///   eCond &gt;=  32 -&gt; m_nPlayerCondEx,  bit eCond -  32
///   else            m_nPlayerCond,    bit eCond
/// </code>
///
/// **All five are on the wire and this project read none of them.** `docs/WIRE-COVERAGE.md` reports
/// `DT_TFPlayerShared` at 0 of 66, and the owner's recording carries `m_nPlayerCond` through
/// `m_nPlayerCondEx3` — measured, not assumed.
///
/// **What is deliberately NOT implemented, named rather than omitted:** the `m_ConditionList` half
/// of the line above. It is a second source for conditions below 32 and it is a networked
/// `CUtlVector` of per-condition records rather than a bitfield. The bit is the path the engine
/// falls through to and the one the recording populates; the list would be a separate piece of
/// work. A condition set ONLY in the list would read as absent here.
///
/// Synthetic (D38): every bit asserted is one the test set.
/// </remarks>
public sealed class PlayerConditionConformanceTests
{
    [Test]
    public void InCond_AConditionBelowThirtyTwo_ReadsTheFirstVariable()
    {
        // TF_COND_DISGUISED is 3 (`tf_shareddefs.h:693`), so bit 3 of m_nPlayerCond.
        PlayerConditions conditions = new(1 << 3, 0, 0, 0, 0);

        conditions.Has(PlayerConditions.Disguised).ShouldBeTrue();
    }

    [Test]
    public void InCond_AConditionInEachRange_ReadsItsOwnVariable()
    {
        // **One per variable, because the ranges are the whole mechanism.** A reader that always
        // used `m_nPlayerCond` would pass a test of condition 3 and fail every condition past 31 —
        // and 31 of the 66 fields in this table live past it.
        new PlayerConditions(0, 1 << 0, 0, 0, 0).Has(32).ShouldBeTrue("cond 32 is bit 0 of Ex");
        new PlayerConditions(0, 0, 1 << 5, 0, 0).Has(69).ShouldBeTrue("cond 69 is bit 5 of Ex2");
        new PlayerConditions(0, 0, 0, 1 << 2, 0).Has(98).ShouldBeTrue("cond 98 is bit 2 of Ex3");
        new PlayerConditions(0, 0, 0, 0, 1 << 7).Has(135).ShouldBeTrue("cond 135 is bit 7 of Ex4");
    }

    [Test]
    public void InCond_ABitSetInTheWrongVariable_IsNotRead()
    {
        // **The control, and it is what makes the ranges mean anything.** Bit 3 set in every
        // variable EXCEPT the first must not answer TF_COND_DISGUISED — otherwise "picks the right
        // variable" and "ors everything together" are the same observation, and a disguised-looking
        // spy would appear whenever any unrelated condition shared a bit number.
        PlayerConditions conditions = new(0, 1 << 3, 1 << 3, 1 << 3, 1 << 3);

        conditions.Has(PlayerConditions.Disguised).ShouldBeFalse();
    }

    [Test]
    public void InCond_AConditionNobodySet_IsAbsent()
    {
        // The other control: an empty set answers no to everything, so a reader returning true
        // unconditionally cannot pass.
        PlayerConditions none = new(0, 0, 0, 0, 0);

        none.Has(PlayerConditions.Disguised).ShouldBeFalse();
        none.Has(32).ShouldBeFalse();
        none.Has(135).ShouldBeFalse();
    }

    [Test]
    public void InCond_TheTopBitOfAVariable_IsRead()
    {
        // **Bit 31, because the variables are 32 bits and this is where a sign error lives.** An
        // implementation using a signed shift or an int where the engine has an unsigned reads this
        // as negative and answers wrongly — the exact family
        // `docs/memory/numeric-decoding-traps.md` records.
        new PlayerConditions(unchecked((int)0x8000_0000), 0, 0, 0, 0).Has(31).ShouldBeTrue();
        new PlayerConditions(0, unchecked((int)0x8000_0000), 0, 0, 0).Has(63).ShouldBeTrue();
    }
}
