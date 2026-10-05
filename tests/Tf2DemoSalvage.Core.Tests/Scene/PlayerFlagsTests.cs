using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// `FL_FROZEN` by the demo's own `DT_BasePlayer.m_fFlags` width: `SendPropInt( SENDINFO( m_fFlags ), PLAYER_FLAG_BITS, … )`.
/// </summary>
/// <remarks>
/// The orangebox `const.h` lists nine player bits with `FL_FROZEN (1&lt;&lt;5)`; the multiplayer list that inserted
/// `FL_ANIMDUCKING (1&lt;&lt;2)` has eleven, `FL_FROZEN (1&lt;&lt;6)` (source-sdk-2013 const.h:124-158, hl2sdk `orangebox`).
/// Measured with the `schema` probe: 9 bits in the 2007, 2008 and 2009 specimens, 11 in 2011 and 2013, 32 in a 2026 demo.
/// </remarks>
public sealed class PlayerFlagsTests
{
    [TestCase(9, 1 << 5, TestName = "Frozen_NinePlayerFlagBits_IsBitFive")]
    [TestCase(11, 1 << 6, TestName = "Frozen_ElevenPlayerFlagBits_IsBitSix")]
    [TestCase(32, 1 << 6, TestName = "Frozen_ThirtyTwoPlayerFlagBits_IsBitSix")]
    public void Frozen_ByTheDeclaredWidth(int bits, int expected) =>
        PlayerFlags.Frozen(Schema(bits)).ShouldBe(expected);

    [Test]
    public void Frozen_NoPlayerTable_IsTheCurrentBit() =>
        PlayerFlags.Frozen(new DemoSchema([], [])).ShouldBe(1 << 6);

    [Test]
    public void Build_TheSyntheticElevenBitPlayer_CarriesBitSix() =>
        DemoTimeline.Build(SyntheticPlayer.DemoWithItemEffectMeterFields()).FrozenFlag.ShouldBe(1 << 6);

    private static DemoSchema Schema(int bits) => new(
        [new SendTable("DT_BasePlayer", NeedsDecoder: true, [new SendProperty(SendPropType.Int, "m_fFlags", 0, string.Empty, 0f, 0f, bits, 0)])],
        []);
}
