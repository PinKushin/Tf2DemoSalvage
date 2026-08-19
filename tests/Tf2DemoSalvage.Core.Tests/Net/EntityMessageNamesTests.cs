using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Tests that an entity message's type byte is named only when the class makes it unambiguous.
/// </summary>
/// <remarks>
/// The whole difficulty of this message is that value 1 is `BASEENTITY_MSG_REMOVE_DECALS` to most
/// handlers and `PLAY_PLAYER_JINGLE` to `C_BasePlayer`. A table keyed on the byte alone would be
/// a coin flip presented as a fact, so every case here varies the *class* and holds the byte
/// fixed — which is the manipulation that can actually falsify the lookup.
/// </remarks>
public sealed class EntityMessageNamesTests
{
    [Test]
    public void EntityMessageNames_TheSameByte_IsNamedByTheReceivingClass()
    {
        // The measured case: every entity message in the corpus is CBaseAnimating type 1, which
        // inherits C_BaseEntity's handler because CBaseAnimating declares no ReceiveMessage.
        EntityMessageNames.Lookup("CBaseAnimating", 1).ShouldBe("BASEENTITY_MSG_REMOVE_DECALS");

        // The collision, and the reason the class is a required argument rather than a nicety.
        EntityMessageNames.Lookup("CTFPlayer", 1).ShouldBe("PLAY_PLAYER_JINGLE");
        EntityMessageNames.Lookup("CBasePlayer", 1).ShouldBe("PLAY_PLAYER_JINGLE");
    }

    [Test]
    public void EntityMessageNames_AnUnknownByteOrClass_IsNotNamed()
    {
        // Nothing in the inherited set defines a second case, so any other byte is unnamed rather
        // than guessed - the number is still reported by the caller.
        EntityMessageNames.Lookup("CBaseAnimating", 2).ShouldBeNull();
        EntityMessageNames.Lookup("CTFPlayer", 0).ShouldBeNull();

        // No class resolved means no claim can be made, which is the state the trace is in when
        // entity expansion is off and no schema has been built.
        EntityMessageNames.Lookup(null, 1).ShouldBeNull();
    }

    [Test]
    public void EntityMessageNames_AClassMerelyContainingPlayer_IsNotAPlayer()
    {
        // The suffix match is deliberate and this is its control. TF2 ships CTFPlayerResource and
        // CTFPlayerDestructionLogic, neither of which is a C_BasePlayer, and both of which would
        // be misnamed by a substring test.
        EntityMessageNames.Lookup("CTFPlayerResource", 1).ShouldBe("BASEENTITY_MSG_REMOVE_DECALS");
        EntityMessageNames.Lookup("CTFPlayerDestructionLogic", 1)
            .ShouldBe("BASEENTITY_MSG_REMOVE_DECALS");
    }
}
