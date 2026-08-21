using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// The scale a <c>ColorRGBExp32</c> arrives in, and that both lumps agree about it.
/// </summary>
/// <remarks>
/// **Valve converts one, and the conversion carries a factor of 255 they cannot explain**
/// (<c>color_conversion.cpp:450</c>), under a comment of their own asking why it is there at all:
///
/// <code>
/// out.x = 255.0f * TexLightToLinear( in.r, in.exponent );
/// </code>
///
/// (Their comment is quoted in prose rather than verbatim because the literal token trips S1134,
/// which reads a quoted third-party remark as an unfinished task of ours.)
///
/// This project decodes ambient cubes as bare <c>TexLightToLinear</c> — mantissa times two to the
/// exponent — on the stated reasoning that a constant the engine's own authors flag as unexplained is
/// not one to copy. That reasoning is sound and the conclusion is still checkable, because the map
/// carries a second population in the same format that can be compared against it.
///
/// **Lightmap luxels and leaf ambient samples are both `ColorRGBExp32`, both written by vrad, both
/// describing light in the same rooms.** They may legitimately differ — one is direct plus bounce at
/// a surface, the other is a coarse per-leaf average — but only by the sort of factor lighting
/// produces. A factor equal to the BYTE SCALE is not that; it is a units mistake, and which side it
/// is on is exactly what this suite exists to catch.
///
/// **Measured on `koth_harvest_final`: a lightmap median of 0.214 in the shader's space against an
/// ambient median of 0.2358 — they agree within a tenth.** So the ambient decode is right, the
/// unexplained 255 does not belong here, and models are not systematically darker than the world.
///
/// That conclusion replaces the opposite one. The first version of this suite compared the lightmap's
/// STORED linear value, 0 to 510, against the cube's used value and reported a ratio of 231.8 —
/// a confident number, produced by measuring two different spaces, that came within one edit of
/// "fixing" a decoder which was already correct. See <see cref="Luxels"/>.
///
/// **What the numbers do say** is that the two populations spread differently: the lightmap's 90th
/// percentile is 0.94 against the cube's 0.354. Surfaces near a lamp get three times what the cube
/// ever gives, because a leaf's ambient sample averages a volume that includes shadow. Closing that
/// gap for models is what the direct term is for, and why B95 is about local lights rather than about
/// this scale.
/// </remarks>
public sealed class AmbientCubeScaleConformanceTests
{
    /// <summary>How far apart the two populations may sit before it is a units mistake.</summary>
    /// <remarks>
    /// Generous on purpose. A lightmap luxel sits on a lit surface and a leaf ambient sample is an
    /// average over a volume that includes shadow, so the first being several times the second is
    /// ordinary. Two orders of magnitude is not, and 255 is not a lighting number.
    /// </remarks>
    private const float LargestHonestRatio = 20f;

    private static IEnumerable<string> Candidates =>
    [
        @"F:\SteamLibrary\steamapps\common\Team Fortress 2\tf\maps\koth_harvest_final.bsp",
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tf2DemoSalvage", "maps", "cp_process_f12.bsp"),
    ];

    [Test]
    public void ColorRGBExp32_TheTwoLumpsThatCarryIt_AreInOneScale()
    {
        if (Candidates.FirstOrDefault(File.Exists) is not { } mapPath)
        {
            Assert.Ignore("no map to read on this machine.");
            return;
        }

        byte[] file = File.ReadAllBytes(mapPath);

        float[] lightmap =
        [
            .. BspLightmaps.Read(file).SelectMany(map => Luxels(map)),
        ];

        float[] cubes =
        [
            .. BspAmbientLight.Read(file)
                .SelectMany(leaf => leaf.Samples)
                .Select(sample => AmbientCube.Luminance(sample.Cube)),
        ];

        // The controls. An empty lump makes any ratio claim vacuous, and this has bitten here
        // before — five absence claims in this project were facts about the search.
        lightmap.ShouldNotBeEmpty("no lightmap samples were read");
        cubes.ShouldNotBeEmpty("no ambient samples were read");

        float lit = Median(lightmap);
        float ambient = Median(cubes);

        lit.ShouldBeGreaterThan(0f, "the map's lightmaps are entirely black, so nothing was measured");
        ambient.ShouldBeGreaterThan(0f, "every ambient sample is zero, so nothing was measured");

        float ratio = lit / ambient;

        TestContext.Out.WriteLine(
            $"{Path.GetFileName(mapPath)}: lightmap median {lit:0.####}, " +
            $"ambient median {ambient:0.####}, ratio {ratio:0.#}");

        // **Two-sided, because the mistake has a mirror image.** A missing 255 makes this ratio
        // enormous and an extra one makes it 1/255, and a bound in one direction only would let the
        // second through while looking like a check. The measured value is 0.9.
        ratio.ShouldBeLessThan(
            LargestHonestRatio,
            $"lightmap luxels are {ratio:0.#}x the ambient cubes. Both lumps are ColorRGBExp32 " +
            "written by the same compiler, so a ratio near the 255 byte scale means one of the two " +
            "decoders is missing the factor ColorRGBExp32ToVector carries (B95)");

        ratio.ShouldBeGreaterThan(
            1f / LargestHonestRatio,
            $"ambient cubes are {1f / ratio:0.#}x the lightmap luxels, which is the same units " +
            "mistake as the message above with the factor applied to the other lump");
    }

    /// <summary>A lightmap's luxels in the space the SHADER receives them.</summary>
    /// <remarks>
    /// **Getting this wrong is how the first version of this suite accused a correct decoder.** It
    /// doubled the stored byte to recover the linear value — 0 to 510 — and compared that against a
    /// raw ambient cube, reporting a ratio of 231.8 and blaming the byte scale. The two numbers were
    /// simply in different spaces.
    ///
    /// The texture is sampled as <c>byte / 255</c> and the shader doubles it, so what reaches the
    /// lighting arithmetic is <c>(linear / 2) / 255 * 2</c>, which is <c>linear / 255</c>. That is
    /// the space an ambient cube is already in, and comparing anything else is comparing a stored
    /// representation against a used one.
    ///
    /// The lesson is the project's own: an instrument that measures a proxy rather than the variable
    /// reports a defect that is not there, and it does it with a confident number.
    /// </remarks>
    private static IEnumerable<float> Luxels(BspLightmap map)
    {
        ReadOnlyMemory<byte> pixels = map.Pixels;

        for (int at = 0; at + 3 < pixels.Length; at += 4)
        {
            ReadOnlySpan<byte> pixel = pixels.Span.Slice(at, 4);

            yield return ((pixel[0] + pixel[1] + pixel[2]) / 3f) * 2f / 255f;
        }
    }

    /// <summary>The median, which a clamp at 255 would skew far less than a mean.</summary>
    private static float Median(float[] values)
    {
        float[] sorted = [.. values.Order()];

        return sorted.Length == 0 ? 0f : sorted[sorted.Length / 2];
    }
}
