using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Turning a typed name, user id or index into the entity meant (D153).
/// </summary>
/// <remarks>
/// **Synthetic, because the roster HAS ground truth here** — the names are the ones this file put
/// there, where a corpus demo can only say what happens to be in it (D38). The awkward cases are
/// deliberately built in: a name that is a prefix of another, one differing only in case, and the
/// SourceTV slot.
/// </remarks>
[TestFixture]
public sealed class PlayerLookupTests
{
    /// <summary>A roster shaped like a competitive match's, traps included.</summary>
    private static PlayerInfo[] Roster =>
    [
        new PlayerInfo("b4nnyPog", UserId: 11, SteamId: "a", EntityIndex: 2, IsBot: false, IsSourceTv: false),
        new PlayerInfo("b4nny", UserId: 12, SteamId: "b", EntityIndex: 3, IsBot: false, IsSourceTv: false),
        new PlayerInfo("[TAG] koel", UserId: 13, SteamId: "c", EntityIndex: 4, IsBot: false, IsSourceTv: false),
        new PlayerInfo("SourceTV", UserId: 1, SteamId: "d", EntityIndex: 1, IsBot: true, IsSourceTv: true),
    ];

    [Test]
    public void Resolve_AnExactName_BeatsALongerOneContainingIt()
    {
        // **The trap this ordering exists for.** `b4nnyPog` appears first and contains `b4nny`, so
        // a partial-only match would make the shorter name unselectable for ever.
        PlayerLookup.Resolve(Roster, "b4nny").ShouldBe(3);

        // And the longer name still resolves to itself.
        PlayerLookup.Resolve(Roster, "b4nnyPog").ShouldBe(2);
    }

    [Test]
    public void Resolve_APartOfAName_FindsThePlayerWithTheTag()
    {
        // Competitive names carry clan tags and unicode that nobody will retype, which is why
        // partial matching cannot simply be dropped in favour of exactness.
        PlayerLookup.Resolve(Roster, "koel").ShouldBe(4);
    }

    [Test]
    public void Resolve_ADifferentCase_StillFindsThePlayer()
    {
        PlayerLookup.Resolve(Roster, "B4NNY").ShouldBe(3);
        PlayerLookup.Resolve(Roster, "KoEl").ShouldBe(4);
    }

    [Test]
    public void Resolve_TheSourceTvSlot_IsNeverAMatch()
    {
        // **It is not a player.** Spectating it is the fault `docs/findings/29` records — three
        // identical captures of nothing — and it must not be reachable by naming it either.
        PlayerLookup.Resolve(Roster, "SourceTV").ShouldBeNull();
    }

    [Test]
    public void Resolve_ANumber_IsAUserIdBeforeAnEntityIndex()
    {
        // A user id is what the demo's own game events carry, so it wins; the fall-through keeps
        // the older `--spectate <entity>` spelling working for a number nobody claims.
        PlayerLookup.Resolve(Roster, "12").ShouldBe(3);
        PlayerLookup.Resolve(Roster, "99").ShouldBe(99);
    }

    [Test]
    public void Resolve_NothingOrNobody_IsNull()
    {
        PlayerLookup.Resolve(Roster, null).ShouldBeNull();
        PlayerLookup.Resolve(Roster, "   ").ShouldBeNull();

        // A name nobody has, and not a number, is a refusal rather than a silent first player -
        // spectating the wrong person because a name was mistyped is a recording of somebody else.
        PlayerLookup.Resolve(Roster, "nobodyhere").ShouldBeNull();
    }
}
