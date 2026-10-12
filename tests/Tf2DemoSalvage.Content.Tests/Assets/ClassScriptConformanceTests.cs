using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// A class script's keys as `KeyValues` reads them: a `//` comment is not a key (B523).
/// </summary>
/// <remarks>
/// **`CUtlBuffer::EatCPPComment`** (`tier1/utlbuffer.cpp:422-440`) — before every token `KeyValues::ReadToken` eats
/// white space and then, while the next two characters are `//`, the rest of the line. So
/// <code>
///     //"DontDoNewJump"	"1"
/// </code>
/// — which is how the shipped `soldier.ctx` and `medic.ctx` carry it, *read from the game's shipped data* — is no key
/// at all, and `GetInt( "DontDoNewJump", 0 )` (`tf_classdata.cpp:188`) answers 0.
///
/// **The fixtures are the shipped lines**, tabs included, because the defect was in what a real file looks like and
/// not in any value a test would think to invent.
/// </remarks>
public sealed class ClassScriptConformanceTests
{
    private static byte[] Script(string body) =>
        Encoding.UTF8.GetBytes("PlayerClass\n{\n" + body + "\n}\n");

    [Test]
    public void DontDoNewJump_TheShippedCommentedOutLine_IsNotSet() =>
        ClassScript.DontDoNewJump(Script("\t\"weapon3\"\t\t\"TF_WEAPON_ROCKETLAUNCHER\"\n\t\n\t//\"DontDoNewJump\"\t\"1\"\n"))
            .ShouldBeFalse();

    [Test]
    public void DontDoAirwalk_TheShippedCommentedOutLine_IsNotSet() =>
        ClassScript.DontDoAirwalk(Script("\t//\"DontDoAirwalk\"\t\"1\"\n\t//\"DontDoNewJump\"\t\"1\"\n"))
            .ShouldBeFalse();

    /// <summary>The control: the same lines with the comment taken off are set.</summary>
    [Test]
    public void DontDoNewJump_TheLineUncommented_IsSet()
    {
        byte[] script = Script("\t\"DontDoAirwalk\"\t\"1\"\n\t\"DontDoNewJump\"\t\"1\"\n");

        ClassScript.DontDoNewJump(script).ShouldBeTrue();
        ClassScript.DontDoAirwalk(script).ShouldBeTrue();
    }

    /// <summary>A commented-out key does not hide the live one below it — the shipped `health_max` pair's shape.</summary>
    [Test]
    public void Model_BelowACommentedOutModel_IsTheLiveOne() =>
        ClassScript.Model(Script("//\t\"model\"\t\t\"models/player/old.mdl\"\n\t\"model\"\t\t\"models/player/soldier.mdl\"\n"))
            .ShouldBe("models/player/soldier.mdl");

    /// <summary>`//` inside a quoted value is the value, not a comment: only a token's start is asked.</summary>
    [Test]
    public void Model_AfterAValueHoldingTwoSlashes_IsStillRead() =>
        ClassScript.Model(Script("\t\"localize_name\"\t\"a//b\"\t\"model\"\t\"models/player/soldier.mdl\"\n"))
            .ShouldBe("models/player/soldier.mdl");
}
