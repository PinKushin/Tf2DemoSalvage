using System;
using System.Text.RegularExpressions;

using Silk.NET.Direct3D11;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Every blend state, against the equation <c>BlendType_t</c> writes beside its mode.
/// </summary>
/// <remarks>
/// **The last of the audited rendering gaps, and the one that corrected a standing note.**
/// `$additive` and `$translucent` were implemented and claimed in `MaterialCensus` with nothing
/// comparing their behaviour to the engine's — and the translucent state carried a comment saying
/// its factors were *interpolated*, because `SetDefaultBlendingShadowState` lives in the closed
/// material system.
///
/// That was true of the function and false of the definition. `public/shaderlib/BaseShader.h`
/// declares `BlendType_t` with each mode's equation written out as a comment:
///
/// <code>
/// // src * srcAlpha + dst * (1-srcAlpha)
/// BT_BLEND,
/// // src * one + dst * one
/// BT_ADD,
/// </code>
///
/// So the factors are **read from published source**, not inferred from a name — a different
/// evidence class, and the reason this test can exist at all.
///
/// **The equations are parsed out of that header rather than restated here.** Writing
/// `src * one + dst * one` into the test would pin what I believe BT_ADD means; extracting it makes
/// the SDK the authority, so a change in Valve's own definition surfaces as a failure rather than
/// staying invisible.
/// </remarks>
public sealed class BlendStateConformanceTests
{
    private const string BaseShader = "src/public/shaderlib/BaseShader.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void BlendState_Additive_IsOnePlusOne()
    {
        // BT_ADD: "src * one + dst * one". Both factors are one, so a dark texel contributes
        // nothing and a light cone brightens what it covers instead of covering it.
        Equation("BT_ADD").ShouldBe("src * one + dst * one");

        BlendDesc additive = BlendStates.Additive;

        additive.RenderTarget[0].SrcBlend.ShouldBe(Blend.One);
        additive.RenderTarget[0].DestBlend.ShouldBe(Blend.One);
        additive.RenderTarget[0].BlendOp.ShouldBe(BlendOp.Add);
        additive.RenderTarget[0].BlendEnable.Value.ShouldBe(1u);
    }

    [Test]
    public void BlendState_Translucent_IsSrcAlphaOverInvSrcAlpha()
    {
        // BT_BLEND: "src * srcAlpha + dst * (1-srcAlpha)" - the ordinary over operator, and the
        // one whose factors this project previously recorded as interpolated.
        Equation("BT_BLEND").ShouldBe("src * srcAlpha + dst * (1-srcAlpha)");

        BlendDesc translucent = BlendStates.Translucent;

        translucent.RenderTarget[0].SrcBlend.ShouldBe(Blend.SrcAlpha);
        translucent.RenderTarget[0].DestBlend.ShouldBe(Blend.InvSrcAlpha);
        translucent.RenderTarget[0].BlendOp.ShouldBe(BlendOp.Add);
        translucent.RenderTarget[0].BlendEnable.Value.ShouldBe(1u);
    }

    [Test]
    public void BlendState_Modulate_MultipliesFramebufferByTexture()
    {
        // **Not a BlendType_t mode**, because Modulate is a shader rather than a blend flag - so
        // this one is asserted on its own terms. DestColor times zero-source is the framebuffer
        // multiplied by the texture: white leaves the destination alone, black blacks it out.
        BlendDesc modulate = BlendStates.Modulate;

        modulate.RenderTarget[0].SrcBlend.ShouldBe(Blend.DestColor);
        modulate.RenderTarget[0].DestBlend.ShouldBe(Blend.Zero);
        modulate.RenderTarget[0].BlendOp.ShouldBe(BlendOp.Add);
        modulate.RenderTarget[0].BlendEnable.Value.ShouldBe(1u);
    }

    [Test]
    public void BlendState_EveryState_WritesEveryChannel()
    {
        // A write mask short of All silently drops a channel, which looks like a colour bug rather
        // than a state bug. Asserted across all three because it is the kind of field that gets
        // copied correctly twice and wrongly once.
        foreach (BlendDesc description in
            new[] { BlendStates.Additive, BlendStates.Translucent, BlendStates.Modulate })
        {
            description.RenderTarget[0].RenderTargetWriteMask.ShouldBe((byte)ColorWriteEnable.All);
            description.RenderTarget[0].BlendOpAlpha.ShouldBe(BlendOp.Add);
        }
    }

    [Test]
    public void BlendState_TheEquations_AreFoundInTheSdkHeader()
    {
        // **The control.** Both assertions above compare against strings this test extracted, and a
        // regex that matched nothing would make them fail confusingly rather than clearly. This
        // says plainly whether the header was read.
        Equation("BT_ADD").ShouldNotBeNullOrWhiteSpace();
        Equation("BT_BLEND").ShouldNotBeNullOrWhiteSpace();

        // And that the two are not the same string, which a too-greedy pattern would produce.
        Equation("BT_ADD").ShouldNotBe(Equation("BT_BLEND"));
    }

    /// <summary>The equation commented immediately above a <c>BlendType_t</c> member.</summary>
    /// <remarks>
    /// The declaration puts each mode's formula in the comment block before it, so the member name
    /// is the anchor and the LAST comment line above it is the equation. Anchoring on the member
    /// rather than searching for the formula is what keeps this reading Valve's definition rather
    /// than confirming mine.
    /// </remarks>
    private static string Equation(string member)
    {
        string text = SourceSdk.Text(BaseShader).ShouldNotBeNull();

        Match match = Regex.Match(
            text,
            @"//\s*(?<equation>src \*[^\r\n]*)\s*(?://[^\r\n]*\s*)*?" + Regex.Escape(member) + @"\s*,",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        return match.Success ? match.Groups["equation"].Value.Trim() : string.Empty;
    }
}
