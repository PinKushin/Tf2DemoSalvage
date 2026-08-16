using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Entity message ids, which are per-class and therefore ambiguous by design.
/// </summary>
/// <remarks>
/// **The same byte means two different things, and that is the whole finding.** An entity message
/// carries a type byte interpreted by the RECEIVING class's <c>ReceiveMessage</c>, not by a global
/// table — so <c>BASEENTITY_MSG_REMOVE_DECALS</c> and <c>PLAY_PLAYER_JINGLE</c> are both 1, in two
/// different headers, and which one a stream means depends entirely on what the entity is.
///
/// A decoder that built one id-to-name map would name every jingle a decal removal, or the reverse.
/// Neither is an error and both produce a plausible trace line, which is why this project resolves
/// the name through the class and returns null when it cannot.
///
/// **Both constants are in the SDK**, in <c>game/shared</c> rather than <c>public</c>, so the
/// collision is checkable rather than remembered.
/// </remarks>
public sealed class EntityMessageConformanceTests
{
    /// <summary>Where the base entity's message ids live.</summary>
    private const string BaseEntity = "src/game/shared/baseentity_shared.h";

    /// <summary>Where the player's do.</summary>
    private const string BasePlayer = "src/game/shared/baseplayer_shared.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void BothMessageIdsAreTheEngines()
    {
        SourceSdk.Constants(BaseEntity)["BASEENTITY_MSG_REMOVE_DECALS"]
            .ShouldBe(EntityMessageNames.RemoveDecals);

        SourceSdk.Constants(BasePlayer)["PLAY_PLAYER_JINGLE"]
            .ShouldBe(EntityMessageNames.PlayerJingle);
    }

    [Test]
    public void TheyCollideAndThatIsWhyTheClassDecides()
    {
        // **Asserted rather than described.** If these ever stopped colliding, a single global
        // id-to-name table would become correct and the class-aware lookup would be unnecessary
        // complexity. While they do collide, that lookup is the only thing standing between a trace
        // and a confidently mislabelled message.
        EntityMessageNames.RemoveDecals.ShouldBe(EntityMessageNames.PlayerJingle);

        // And the resolver uses the class, which is the behaviour the collision demands.
        EntityMessageNames.Lookup("CBaseAnimating", EntityMessageNames.RemoveDecals)
            .ShouldNotBe(EntityMessageNames.Lookup("CTFPlayer", EntityMessageNames.PlayerJingle));
    }
}
