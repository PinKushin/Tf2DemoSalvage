using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>What one compositor pass does with its inputs (compositor_ps2x.fxc's combine modes, B754).</summary>
public enum CompositorCombine
{
    /// <summary>`combine_multiply`: the product, a missing input 1.</summary>
    Multiply,

    /// <summary>`combine_add`: the sum, a missing input 0.</summary>
    Add,

    /// <summary>`combine_lerp`: the first toward the second by the third's red; red unless exactly three inputs.</summary>
    Lerp,

    /// <summary>`select`: white where the groups texture's sixteenth matches a selector, else zero; yellow unless one input.</summary>
    Select,

    /// <summary>`apply_sticker`: the sticker over what lies under it by its alpha, the spec's red into alpha.</summary>
    Blend,
}

/// <summary>The 2D affine `A * (u, v, 1)` the compositor's vertex shader applies (compositor_vs20.fxc).</summary>
/// <param name="M00">Row 0, u.</param>
/// <param name="M01">Row 0, v.</param>
/// <param name="M02">Row 0, constant.</param>
/// <param name="M10">Row 1, u.</param>
/// <param name="M11">Row 1, v.</param>
/// <param name="M12">Row 1, constant.</param>
public readonly record struct CompositorAffine(float M00, float M01, float M02, float M10, float M11, float M12)
{
    /// <summary>The identity.</summary>
    public static CompositorAffine Identity => new(1f, 0f, 0f, 0f, 1f, 0f);

    /// <summary>The coordinate a pixel at (u, v) samples.</summary>
    /// <param name="u">The pixel's u, 0 to 1.</param>
    /// <param name="v">The pixel's v.</param>
    /// <returns>The sampled coordinate.</returns>
    public (float U, float V) Apply(float u, float v) => ((M00 * u) + (M01 * v) + M02, (M10 * u) + (M11 * v) + M12);
}

/// <summary>One input of a pass: a source texture, or an earlier pass's render target, read through levels and an affine.</summary>
/// <param name="Texture">The source texture's name under `materials/`, or null for a pass's target.</param>
/// <param name="Pass">The earlier pass whose target this reads, or -1 for a texture.</param>
/// <param name="Srgb">Whether it is read through the sRGB curve: every input but a select's groups texture.</param>
/// <param name="Black">`AdjustLevels`' black point.</param>
/// <param name="White">Its white point.</param>
/// <param name="Gamma">Its gamma.</param>
/// <param name="Matrix">The UV affine.</param>
public sealed record CompositorInput(
    string? Texture, int Pass, bool Srgb, float Black, float White, float Gamma, CompositorAffine Matrix)
{
    /// <summary>A sticker's spec, which becomes the engine's black when it does not load rather than failing (§3.7).</summary>
    public bool BlackIfMissing { get; init; }
}

/// <summary>One full-screen pass into its own RGBA8 sRGB target; at most its first four inputs are read.</summary>
/// <param name="Combine">The combine.</param>
/// <param name="Inputs">The inputs, in sampler order.</param>
/// <param name="Selectors">A select's sixteen selectors, each already times 1/16; empty for any other pass.</param>
public sealed record CompositorPass(CompositorCombine Combine, IReadOnlyList<CompositorInput> Inputs, IReadOnlyList<float> Selectors);

/// <summary>A resolved compositor tree: its passes in draw order, the last being the composite (findings 111 §3).</summary>
/// <param name="Width">Every target's width.</param>
/// <param name="Height">Every target's height.</param>
/// <param name="Passes">The passes, children before parents.</param>
/// <param name="Required">Every texture that must load, the composite failing otherwise — chosen or not (§3.1).</param>
public sealed record CompositorPlan(int Width, int Height, IReadOnlyList<CompositorPass> Passes, IReadOnlyList<string> Required);
