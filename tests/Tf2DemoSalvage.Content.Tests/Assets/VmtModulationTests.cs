using System.IO;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// <c>VmtMaterial.Modulation</c>, the per-material colour and alpha every shader folds in.
/// </summary>
/// <remarks>
/// **The semantics are pinned by <c>UnimplementedRenderingConformanceTests</c> against the SDK;
/// this file is the ordinary coverage underneath it.** The conformance test states what
/// <c>CBaseVSShader::ColorVarsToVector</c> does and needs the SDK checked out to run. These do not,
/// so they run on the measurement box, which is where the mutants actually get killed.
///
/// **The interesting cases are the two spellings and the clamp asymmetry**, not the multiply. A
/// colour written <c>{255 128 0}</c> is bytes and <c>[1 0.5 0]</c> is floats; reading one as the
/// other is a factor of 255, which saturates a surface to white rather than erroring. And alpha is
/// clamped where colour is not, so a single "clamp everything" or "clamp nothing" implementation is
/// wrong in one direction each.
/// </remarks>
public sealed class VmtModulationTests
{
    [Test]
    public void VmtModulation_AMaterialNamingNothing_ModulatesByOne()
    {
        // The identity, and the control for every case below: without it, an implementation that
        // tinted every material would pass all the tests that name a colour.
        Parse("\"LightmappedGeneric\" { \"$basetexture\" \"concrete/floor\" }")
            .Modulation.ShouldBe((1f, 1f, 1f, 1f));
    }

    [Test]
    public void VmtModulation_AMaterialNamingNothing_IsNotModulated()
    {
        Parse("\"LightmappedGeneric\" { \"$basetexture\" \"concrete/floor\" }")
            .IsModulated.ShouldBeFalse();
    }

    [Test]
    public void VmtModulation_AColourAlone_LeavesAlphaOpaque()
    {
        // Channels chosen all different so a transposed component cannot pass, and alpha asserted
        // because a naive implementation that packs four values from a three-value key shifts it.
        Parse("\"UnlitGeneric\" { \"$color\" \"[0.25 0.5 0.75]\" }")
            .Modulation.ShouldBe((0.25f, 0.5f, 0.75f, 1f));
    }

    [Test]
    public void VmtModulation_AnAlphaAlone_LeavesColourWhite()
    {
        Parse("\"UnlitGeneric\" { \"$alpha\" \"0.25\" }")
            .Modulation.ShouldBe((1f, 1f, 1f, 0.25f));
    }

    [Test]
    public void VmtModulation_EitherHalfAlone_CountsAsModulated()
    {
        Parse("\"UnlitGeneric\" { \"$color\" \"[1 1 0]\" }").IsModulated.ShouldBeTrue();
        Parse("\"UnlitGeneric\" { \"$alpha\" \"0.5\" }").IsModulated.ShouldBeTrue();
    }

    [Test]
    public void VmtModulation_AColourAndAlphaBothOne_IsNotModulated()
    {
        // **Stated explicitly, not left to the absent case.** A material CAN name the identity, and
        // an implementation asking "did the material declare $color" rather than "is the result
        // one" reports it as modulated and does the work for nothing. The distinction is invisible
        // on screen, which is why it needs a test rather than a look.
        Parse("\"UnlitGeneric\" { \"$color\" \"[1 1 1]\" \"$alpha\" \"1\" }")
            .IsModulated.ShouldBeFalse();
    }

    [Test]
    public void VmtModulation_AByteSpelling_IsScaledBy255()
    {
        // {255 128 0} and [255 128 0] are a factor of 255 apart, and both parse. Reading the brace
        // form as floats gives a tint of 255 and saturates the surface to white — no exception, a
        // plausible picture.
        Parse("\"UnlitGeneric\" { \"$color\" \"{255 128 0}\" }")
            .Modulation.Red.ShouldBe(1f);

        Parse("\"UnlitGeneric\" { \"$color\" \"{255 128 0}\" }")
            .Modulation.Green.ShouldBe(128f / 255f, 0.0001f);

        Parse("\"UnlitGeneric\" { \"$color\" \"{255 128 0}\" }")
            .Modulation.Blue.ShouldBe(0f);
    }

    [Test]
    public void VmtModulation_AScalarColour_BroadcastsToEveryChannel()
    {
        // ColorVarsToVector's else branch. Not 0.5, because 0.5 is also what a half-alpha would be;
        // 0.3 collides with nothing else in this file.
        Parse("\"UnlitGeneric\" { \"$color\" \"0.3\" }")
            .Modulation.ShouldBe((0.3f, 0.3f, 0.3f, 1f));
    }

    [Test]
    public void VmtModulation_AScalarColourWithBrackets_IsRejected()
    {
        // **The narrowness of the scalar acceptance is the point.** "[0.3]" is a vector var with
        // one component, which the engine reads through GetVecValue for THREE and does not get.
        // Broadening the reader to shrug at any count would hide a genuinely malformed material,
        // so the broadcast applies only to a value written without brackets at all.
        Should.Throw<InvalidDataException>(
            () => Parse("\"UnlitGeneric\" { \"$color\" \"[0.3]\" }").Modulation);
    }

    [Test]
    public void VmtModulation_ASecondColour_MultipliesTheFirst()
    {
        // Half times half is a quarter — a value neither replacing (0.5) nor adding (1.0) produces.
        Parse("\"UnlitGeneric\" { \"$color\" \"[0.5 1 0.5]\" \"$color2\" \"[0.5 0.5 1]\" }")
            .Modulation.ShouldBe((0.25f, 0.5f, 0.5f, 1f));
    }

    [Test]
    public void VmtModulation_ASecondColourAlone_TintsOnItsOwn()
    {
        // Absent $color is one, so $color2 alone is the whole factor. An implementation that reads
        // $color2 only when $color is present drops the tint entirely here.
        Parse("\"UnlitGeneric\" { \"$color2\" \"[0.5 0.25 0.125]\" }")
            .Modulation.ShouldBe((0.5f, 0.25f, 0.125f, 1f));
    }

    [Test]
    public void VmtModulation_AlphaAboveOne_IsClampedDown()
    {
        Parse("\"UnlitGeneric\" { \"$alpha\" \"1.75\" }").Modulation.Alpha.ShouldBe(1f);
    }

    [Test]
    public void VmtModulation_AlphaBelowZero_IsClampedUp()
    {
        // The other side, because a one-sided clamp — Math.Min rather than Math.Clamp — passes the
        // test above.
        Parse("\"UnlitGeneric\" { \"$alpha\" \"-0.25\" }").Modulation.Alpha.ShouldBe(0f);
    }

    [Test]
    public void VmtModulation_ColourAboveOne_IsNotClamped()
    {
        // **The asymmetry.** Over-bright modulation is real: the linear-space variant of the
        // modulation setter tests `color[i] > 1.0f` before converting, which is only meaningful for
        // a channel that may exceed one. Clamping here would quietly cap a glow.
        Parse("\"UnlitGeneric\" { \"$color\" \"[2 3 4]\" }")
            .Modulation.ShouldBe((2f, 3f, 4f, 1f));
    }

    [Test]
    public void VmtModulation_ColourBelowZero_IsNotClamped()
    {
        Parse("\"UnlitGeneric\" { \"$color\" \"[-1 0 0]\" }")
            .Modulation.Red.ShouldBe(-1f);
    }

    [Test]
    public void VmtModulation_AMalformedColour_IsRejected()
    {
        Should.Throw<InvalidDataException>(
            () => Parse("\"UnlitGeneric\" { \"$color\" \"[1 2]\" }").Modulation);

        Should.Throw<InvalidDataException>(
            () => Parse("\"UnlitGeneric\" { \"$color\" \"[1 2 red]\" }").Modulation);
    }

    private static VmtMaterial Parse(string text) =>
        VmtMaterial.Parse(Encoding.UTF8.GetBytes(text));
}
