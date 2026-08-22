using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Render;

/// <summary>
/// The blend states a material's mode maps to, as <c>BlendType_t</c> defines them.
/// </summary>
/// <remarks>
/// **Extracted so the factors can be asserted against Valve's own definition.** They used to be
/// built inline where they are created, which left the one thing worth checking — the source and
/// destination factors — reachable only by reading the renderer.
///
/// **The equations are published, which corrects a note this project carried for a while.** The
/// translucent state was commented as interpolated, on the grounds that
/// <c>SetDefaultBlendingShadowState</c> lives in the closed material system. That is true of the
/// FUNCTION and not of the DEFINITION: <c>public/shaderlib/BaseShader.h</c> declares
/// <c>BlendType_t</c> with each mode's equation written out beside it —
///
/// <code>
/// // no alpha blending
/// BT_NONE = 0,
/// // src * srcAlpha + dst * (1-srcAlpha)
/// BT_BLEND,
/// // src * one + dst * one
/// BT_ADD,
/// // src * srcAlpha + dst * one
/// BT_BLENDADD
/// </code>
///
/// So these are read from published source rather than inferred from a name, which is a different
/// and much stronger evidence class. See <c>docs/findings/17-translucency.md</c>.
/// </remarks>
internal static class BlendStates
{
    /// <summary><c>BT_ADD</c>: <c>src * one + dst * one</c>.</summary>
    public static BlendDesc Additive => Describe(Blend.One, Blend.One, Blend.One, Blend.One);

    /// <summary><c>BT_BLEND</c>: <c>src * srcAlpha + dst * (1-srcAlpha)</c>.</summary>
    /// <remarks>
    /// The alpha channel takes <c>one</c> and <c>1-srcAlpha</c>, which is the second pass Valve
    /// describes for the HDR path folded into one state — it keeps the destination's coverage
    /// correct when several translucent surfaces stack.
    /// </remarks>
    public static BlendDesc Translucent =>
        Describe(Blend.SrcAlpha, Blend.InvSrcAlpha, Blend.One, Blend.InvSrcAlpha);

    /// <summary>The <c>Modulate</c> shader: the framebuffer multiplied by the texture.</summary>
    /// <remarks>
    /// **Not one of <c>BlendType_t</c>'s modes**, because Modulate is a shader rather than a blend
    /// flag — a Modulate material declares neither <c>$translucent</c> nor <c>$additive</c>, so it
    /// fell through every predicate and was drawn opaque, painting over what it was meant to
    /// darken. White leaves the destination alone; black blacks it out.
    /// </remarks>
    public static BlendDesc Modulate =>
        Describe(Blend.DestColor, Blend.Zero, Blend.One, Blend.Zero);

    private static BlendDesc Describe(
        Blend source, Blend destination, Blend sourceAlpha, Blend destinationAlpha)
    {
        BlendDesc description = default;

        description.RenderTarget[0].BlendEnable = 1;
        description.RenderTarget[0].SrcBlend = source;
        description.RenderTarget[0].DestBlend = destination;
        description.RenderTarget[0].BlendOp = BlendOp.Add;
        description.RenderTarget[0].SrcBlendAlpha = sourceAlpha;
        description.RenderTarget[0].DestBlendAlpha = destinationAlpha;
        description.RenderTarget[0].BlendOpAlpha = BlendOp.Add;
        description.RenderTarget[0].RenderTargetWriteMask = (byte)ColorWriteEnable.All;

        return description;
    }
}
