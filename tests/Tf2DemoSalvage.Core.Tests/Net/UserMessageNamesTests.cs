using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Tests that the user message name table is only applied where it has been measured to hold.
/// </summary>
/// <remarks>
/// A user message carries no name on the wire — only an id, which is the game's registration
/// order. The table is transcribed from the 2013 SDK, so it describes one build, and the question
/// is how far it generalises.
///
/// **Measured: exactly to id 28, below protocol 24.** `CheapBreakModel` is a short and a coordinate
/// vector, so its full form is 85 bits, and that width identifies it wherever it appears. It sits
/// at id 40 in the 2009 demo, 41 in the 2011 pair, and 42 at protocol 24 — two insertions between
/// 2009 and 2013, moving every id after them.
/// </remarks>
public sealed class UserMessageNamesTests
{
    private const int Modern = 24;
    private const int Old = 15;

    [Fact]
    public void TheStableHeadIsNamedAtEveryProtocol()
    {
        // These eleven ids were confirmed at protocols 11, 14, 15, 16 and 24 with matching body
        // widths, which is what makes naming them safe rather than merely conventional.
        UserMessageNames.Lookup(0, Old).ShouldBe("Geiger");
        UserMessageNames.Lookup(13, Old).ShouldBe("Rumble");
        UserMessageNames.Lookup(18, Old).ShouldBe("Damage");
        UserMessageNames.Lookup(28, Old).ShouldBe("PlayerStatsUpdate");
    }

    [Fact]
    public void AboveTheStableHead_AnOldDemoGetsNoName()
    {
        // The regression. Until 2026-08-11 this returned "PlayerShieldBlocked" for the 2009 demo's
        // id 40, which is an 85-bit body - CheapBreakModel's width, not PlayerShieldBlocked's
        // declared two bytes. A wrong name on a correctly decoded message is the failure mode this
        // whole table is written to avoid, and it is invisible without a width to check against.
        UserMessageNames.Lookup(40, Old).ShouldBeNull();
        UserMessageNames.Lookup(41, 16).ShouldBeNull();
        UserMessageNames.Lookup(52, 16).ShouldBeNull();
    }

    [Fact]
    public void AboveTheStableHead_ProtocolTwentyFourIsNamed()
    {
        // The control. Withholding names everywhere would pass the test above while destroying the
        // table's whole purpose, so the era the transcription describes must still be named.
        UserMessageNames.Lookup(40, Modern).ShouldBe("PlayerShieldBlocked");
        UserMessageNames.Lookup(42, Modern).ShouldBe("CheapBreakModel");
        UserMessageNames.Lookup(52, Modern).ShouldBe("SpawnFlyingBird");
    }

    [Fact]
    public void AnIdPastTheTable_IsUnnamedAtEveryProtocol()
    {
        UserMessageNames.Lookup(500, Modern).ShouldBeNull();
        UserMessageNames.Lookup(-1, Modern).ShouldBeNull();
    }
}
