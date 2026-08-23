using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// Image data ready for the GPU: a format, and the mip levels in it, largest first.
/// </summary>
/// <remarks>
/// **One shape for every texture, whatever it came from.** A DXT file hands over its blocks
/// untouched, a 24-bit VTF is widened to RGBA because no GPU format matches it, and a chequer
/// generated in code arrives the same way. The renderer switches on <see cref="Format"/> and does
/// not care which of those happened.
///
/// **This replaced a plain <c>ReadOnlyMemory&lt;byte&gt;</c> of RGBA pixels (B149).** That shape
/// could only describe an expanded image, so every texture had to be expanded to fit it — 16.87 s of
/// CPU on one map load, producing something four to eight times larger to upload than what the file
/// already held.
///
/// **The owner's standing rule, stated twice while this was being done:**
///
/// > *"in production all stuff that can be done on the gpu should be done on the gpu"*
///
/// > *"you offload everything you can to the gpu, because the gpu is faster for almost everything
/// > outside of pure arithmetic and getting user input"*
///
/// **Levels rather than one image**, because a block-compressed texture cannot have its mips
/// generated on the device — `GenerateMips` needs a render target and BC formats are not one — and
/// Valve's chain is already in the file, already filtered.
/// </remarks>
/// <param name="Format">What the bytes are. <see cref="VtfFormat.Rgba8888"/> for anything widened.</param>
/// <param name="Levels">The mip chain, largest first. One entry when there is no chain.</param>
public readonly record struct TextureImage(
    VtfFormat Format,
    IReadOnlyList<ReadOnlyMemory<byte>> Levels)
{
    /// <summary>Plain RGBA, for images that were never compressed or had to be widened.</summary>
    /// <param name="pixels">Four bytes per pixel, red first.</param>
    /// <returns>The image.</returns>
    public static TextureImage Rgba(ReadOnlyMemory<byte> pixels) =>
        new(VtfFormat.Rgba8888, [pixels]);

    /// <summary>Nothing at all, for a slot a material did not fill.</summary>
    public static TextureImage None => new(VtfFormat.None, []);

    /// <summary>Whether there is anything to upload.</summary>
    public bool IsEmpty => Levels.Count == 0 || Levels[0].Length == 0;

    /// <summary>The largest level, which is what a caller wanting one image means.</summary>
    public ReadOnlyMemory<byte> Top => Levels.Count > 0 ? Levels[0] : default;

    /// <summary>Whether the GPU samples this format directly as block compression.</summary>
    public bool IsBlockCompressed => Format is
        VtfFormat.Dxt1 or VtfFormat.Dxt1OneBitAlpha or VtfFormat.Dxt3 or VtfFormat.Dxt5;

    /// <summary>Expands the largest level to RGBA, for a caller that has to read texels.</summary>
    /// <param name="width">The image's width.</param>
    /// <param name="height">Its height.</param>
    /// <returns>Four bytes per pixel, red first.</returns>
    /// <remarks>
    /// **Nothing in the drawing path calls this, and nothing should.** Production hands blocks to
    /// the GPU, which is what Valve's material system does and what the owner asked for. This exists
    /// for the places that genuinely have to look at values rather than draw them — measuring a
    /// Phong ramp, reading a normal map's channels, comparing a material's brightness against the
    /// map's stated reflectivity.
    ///
    /// **It is a capability with a reason, not a fallback for convenience.** Reaching for it inside
    /// a load is the mistake B149 was filed about; it is the whole 16.87 s.
    ///
    /// **And it is why the CPU expander stays rather than being deleted.** The obvious tidy-up once
    /// the GPU samples DXT directly is to verify blocks by reading them back off the device instead
    /// — which would be the better test of what is actually drawn, and is the worst possible place
    /// to put it, because **CI has no GPU**. A verification that cannot run where the suite runs is
    /// not a verification. The owner put it plainly: *"no it has to be cpu, the ci has no gpu"*.
    /// </remarks>
    public byte[] ToRgba(int width, int height) => IsBlockCompressed
        ? VtfTexture.Expand(Top.Span, Format, width, height)
        : Top.ToArray();
}
