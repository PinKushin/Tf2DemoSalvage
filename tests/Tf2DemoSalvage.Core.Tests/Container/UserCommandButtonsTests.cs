using Tf2DemoSalvage.Core.Container;

namespace Tf2DemoSalvage.Core.Tests.Container;

/// <summary>
/// Tests the naming of the <c>buttons</c> bitfield against <c>in_buttons.h</c>.
/// </summary>
/// <remarks>
/// The field is thirty-two bits wide and Valve's published header names twenty-five of them, so a
/// namer that silently dropped the rest would read as complete while hiding whatever a later build
/// added. Every case here therefore checks the residual as well as the names.
/// </remarks>
public sealed class UserCommandButtonsTests
{
    [Test]
    public void KnownBitsAreNamedInTheirDeclaredOrder()
    {
        // Lowest bit first, which is the order the header declares and the order a reader scanning
        // a trace expects. IN_ATTACK is bit 0 and IN_DUCK is bit 2.
        UserCommandButtons.Describe(0b101).ShouldBe("IN_ATTACK|IN_DUCK");

        // A single high bit, to catch a namer that only walks the low byte.
        UserCommandButtons.Describe(1u << 24).ShouldBe("IN_GRENADE2");
    }

    [Test]
    public void NoButtonsIsNamedRatherThanBlank()
    {
        // Zero is the common case - a player standing still still sends commands - and an empty
        // string in a trace reads as a rendering bug rather than as an idle tick.
        UserCommandButtons.Describe(0).ShouldBe("none");
    }

    [Test]
    public void AnUnnamedBitIsReportedAsItsValueRatherThanDropped()
    {
        // Bit 25 is IN_ATTACK3 in the live game and is absent from the published header, so it is
        // exactly the case this must not swallow. The residual is the whole point: a name that is
        // not in the source is a guess, and a dropped bit is a lie.
        UserCommandButtons.Describe(1u << 25).ShouldBe("0x02000000");
        UserCommandButtons.Describe(1u | (1u << 25)).ShouldBe("IN_ATTACK|0x02000000");

        // The top bit, which no header names and which sign-extends if the field is ever mistaken
        // for an int.
        UserCommandButtons.Describe(1u << 31).ShouldBe("0x80000000");
    }
}
