using System;
using System.IO;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Valve's entity palette, read from the FGD files the game ships.
/// </summary>
/// <remarks>
/// **Against the shipped files as well as against fixtures.** A hand-written FGD snippet is written
/// from the same belief the parser holds, so it cannot falsify the reading — the standing lesson in
/// `docs/memory/put-the-real-file-in-the-fixture.md`. The fixtures here pin the SHAPES that matter
/// (a stated colour, an inherited one, a cycle) and the shipped-file test proves the parser survives
/// three hundred kilobytes of real text.
/// </remarks>
public sealed class FgdClassesTests
{
    /// <summary>The game's <c>bin</c>, a sibling of <c>tf</c>, where the FGDs ship.</summary>
    /// <remarks>
    /// Derived from the install rather than named separately, so a machine that keeps its library
    /// somewhere else needs no second path. <c>bin</c> is outside <c>tf</c>, which is why this goes
    /// up one level instead of asking <see cref="GameInstall.Find"/>.
    /// </remarks>
    private static string Bin =>
        Path.GetFullPath(Path.Combine(GameInstall.Require(), "..", "bin"));

    [Test]
    public void Colour_AClassStatingOne_IsReadFromTheDeclaration()
    {
        FgdClasses classes = FgdClasses.Parse(
            "@SolidClass base(Targetname) color(0 255 255) = func_areaportal : \"desc\"\n");

        classes.Colour("func_areaportal").ShouldBe(((byte)0, (byte)255, (byte)255));

        // Case-insensitive, because a BSP's classname is not guaranteed to match the FGD's spelling.
        classes.Colour("FUNC_AREAPORTAL").ShouldBe(((byte)0, (byte)255, (byte)255));
    }

    [Test]
    public void Colour_AClassWithoutOne_IsInheritedThroughBase()
    {
        // Most classes state no colour; that is how a hundred entities share one without repeating
        // it, and a parser that read only the declaration would return null for nearly everything.
        FgdClasses classes = FgdClasses.Parse(
            "@BaseClass color(180 10 180) = Reflective : \"\"\n" +
            "@PointClass base(Targetname, Reflective) = env_thing : \"\"\n");

        classes.Colour("env_thing").ShouldBe(((byte)180, (byte)10, (byte)180));
    }

    [Test]
    public void Colour_AClassStatingNothingAnywhere_IsNullRatherThanADefault()
    {
        FgdClasses classes = FgdClasses.Parse("@PointClass base(Targetname) = env_plain : \"\"\n");

        // **Null, and this is a DIVERGENCE from Hammer rather than a match.** 58 of the 598 entity
        // classes in the shipped FGDs state a colour and 9 of the 80 base classes do, so most
        // entities resolve to nothing here while Hammer draws them as something — it has a default
        // and this does not reproduce it. Null keeps "Valve said this colour" and "nobody said"
        // distinguishable for the caller, which is worth having, but it is a gap and is recorded as
        // one rather than as a design win.
        classes.Colour("env_plain").ShouldBeNull();
        classes.Colour("never_declared").ShouldBeNull();
    }

    [Test]
    public void Colour_ACycleInTheBaseGraph_TerminatesInsteadOfHanging()
    {
        // An FGD is hand-edited text, so a cycle is a plausible typo. The cost of being wrong is
        // asymmetric: a viewer that never finishes loading a map, over a colour nobody would miss.
        FgdClasses classes = FgdClasses.Parse(
            "@BaseClass base(B) = A : \"\"\n@BaseClass base(A) = B : \"\"\n");

        classes.Colour("A").ShouldBeNull();
    }

    [Test]
    public void Parse_TheShippedFgds_ReadValvesOwnColours()
    {
        if (!Directory.Exists(Bin))
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        FgdClasses classes = FgdClasses.Parse(
            File.ReadAllText(Path.Combine(Bin, "base.fgd")),
            File.ReadAllText(Path.Combine(Bin, "halflife2.fgd")),
            File.ReadAllText(Path.Combine(Bin, "tf.fgd")));

        TestContext.Out.WriteLine($"FGD {classes.Count} classes");

        classes.Count.ShouldBeGreaterThan(
            300, "three FGDs totalling ~680 KB should define several hundred classes");

        // Spot values read out of the shipped files by eye, and chosen because they are the ones
        // this project would actually draw: an areaportal, a detail brush, and the sky camera.
        classes.Colour("func_areaportal").ShouldBe(((byte)0, (byte)255, (byte)255));
        classes.Colour("func_occluder").ShouldBe(((byte)0, (byte)255, (byte)255));
        classes.Colour("func_detail").ShouldBe(((byte)0, (byte)180, (byte)0));
        classes.Colour("sky_camera").ShouldBe(((byte)0, (byte)0, (byte)255));

        // The control: a classname that does not exist must not pick up a neighbour's colour, which
        // is what a loose match over a file this size would do.
        classes.Colour("func_areaportal_not_a_real_class").ShouldBeNull();
    }
}
