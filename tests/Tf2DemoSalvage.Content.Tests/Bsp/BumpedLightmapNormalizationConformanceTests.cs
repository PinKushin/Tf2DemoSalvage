using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// The engine renormalizes a bump-lit face's three directional lightmaps so their average is the flat one (B514 part 4).
/// </summary>
/// <remarks>
/// *Read in disassembly*: <c>materialsystem.dll FUN_1800249c0</c>, the bumped lightmap page writer (its flat sibling is
/// <c>FUN_180028550</c>; both hand each texel to the encoder <c>FUN_180036450</c>). Per luxel, per channel:
///
/// <code>
/// avg   = (bump1 + bump2 + bump3) * 0.33333334
/// scale = avg != 0 ? flat / avg : 0
/// write flat; write bump1 * scale, bump2 * scale, bump3 * scale
/// </code>
///
/// It runs on the float images <c>R_BuildLightMap</c> built, so after the light styles are summed and the dynamic
/// lights added. vrad's directional samples are NOT what is drawn: on cp_process_f12's outdoor ground their raw
/// average is 0.63 of the flat set, which is how dark the viewer drew it.
/// </remarks>
public sealed class BumpedLightmapNormalizationConformanceTests
{
    private const int TexinfoLump = 6;
    private const int FacesLump = 7;
    private const int LightingLump = 8;

    /// <summary><c>texinfo_t.flags</c>, after two 2x4 float vectors; and <c>SURF_BUMPLIGHT</c>.</summary>
    private const int TexinfoFlagsOffset = 64;
    private const int SurfBumpLight = 0x800;

    /// <remarks>Average (60 + 90 + 150) / 3 = 100 against a flat 120: every set is scaled by 1.2.</remarks>
    [Test]
    public void NormalizeBumped_ALuxelWhoseSetsAverageBelowFlat_ScalesThemToAverageFlat()
    {
        float[] flat = [120f];
        float[] one = [60f];
        float[] two = [90f];
        float[] three = [150f];

        BspLightmaps.NormalizeBumped(flat, one, two, three);

        one[0].ShouldBe(72f, 1e-3f);
        two[0].ShouldBe(108f, 1e-3f);
        three[0].ShouldBe(180f, 1e-3f);
        ((one[0] + two[0] + three[0]) / 3f).ShouldBe(120f, 1e-3f);
    }

    /// <remarks>
    /// Red: average 100, flat 200, scale 2. Green: average 60, flat 30, scale 0.5. Blue: average 0, so scale 0 — for
    /// blue alone, though its flat is 80.
    /// </remarks>
    [Test]
    public void NormalizeBumped_ChannelsWithDifferentAveragesAndOneOfZero_ScalesEachChannelByItsOwn()
    {
        float[] flat = [200f, 30f, 80f];
        float[] one = [100f, 30f, 0f];
        float[] two = [100f, 60f, 0f];
        float[] three = [100f, 90f, 0f];

        BspLightmaps.NormalizeBumped(flat, one, two, three);

        one.ShouldBe([200f, 15f, 0f], 1e-3);
        two.ShouldBe([200f, 30f, 0f], 1e-3);
        three.ShouldBe([200f, 45f, 0f], 1e-3);
        flat.ShouldBe([200f, 30f, 80f]);
    }

    /// <remarks>One bump-lit luxel, red only: flat 120, directional 60 / 90 / 150. Read back to R11G11B10's six-bit mantissa.</remarks>
    [Test]
    public void ReadAll_ABumpLitFace_StoresItsDirectionalSetsNormalizedAndItsFlatSetUnchanged()
    {
        BspFaceLighting face = BspLightmaps.ReadAll(BumpedMap([120, 0, 0, 0, 60, 0, 0, 0, 90, 0, 0, 0, 150, 0, 0, 0]))[0];

        face.Directional.Count.ShouldBe(3);
        Red(face.Flat).ShouldBe(120f, 1f);
        Red(face.Directional[0]).ShouldBe(72f, 1f);
        Red(face.Directional[1]).ShouldBe(108f, 1.5f);
        Red(face.Directional[2]).ShouldBe(180f, 2f);
    }

    /// <remarks>
    /// **The order is the claim: sum the styles, THEN normalize.** Style 0 is flat 100 with 50 / 50 / 50; style 32 is
    /// flat 60 with 10 / 20 / 90. Both on: flat 160, directional 60 / 70 / 140, average 90, scale 16/9 —
    /// 106.67 / 124.44 / 248.89. Normalizing each style alone and summing would give 115 / 130 / 235.
    /// </remarks>
    [Test]
    public void Compose_ABumpLitFaceWithTwoStyles_NormalizesTheSumNotEachStyle()
    {
        BspFaceLighting face = BspLightmaps.ReadAll(BumpedMap(
            [100, 0, 0, 0, 50, 0, 0, 0, 50, 0, 0, 0, 50, 0, 0, 0, 60, 0, 0, 0, 10, 0, 0, 0, 20, 0, 0, 0, 90, 0, 0, 0],
            second: 32))[0];
        byte[] built = new byte[16];

        BspLightmaps.Compose(face.Styles, static _ => 1f, [], built);

        BspLightmaps.Load(built.AsSpan(0, 4)).Red.ShouldBe(160f, 1.5f);
        BspLightmaps.Load(built.AsSpan(4, 4)).Red.ShouldBe(106.67f, 1f);
        BspLightmaps.Load(built.AsSpan(8, 4)).Red.ShouldBe(124.44f, 1f);
        BspLightmaps.Load(built.AsSpan(12, 4)).Red.ShouldBe(248.89f, 2f);
    }

    /// <remarks>
    /// `R_BuildLightMap` adds the dynamic lights before the page writer runs, so they are inside the normalization:
    /// flat 120 + 30 = 150 over directional 60 + 30, 90, 150 (average 110), scale 15/11 — 122.73 / 122.73 / 204.55.
    /// </remarks>
    [Test]
    public void Compose_ABumpLitFaceWithAddedLight_NormalizesAfterAddingIt()
    {
        BspFaceLighting face = BspLightmaps.ReadAll(BumpedMap([120, 0, 0, 0, 60, 0, 0, 0, 90, 0, 0, 0, 150, 0, 0, 0]))[0];
        byte[] built = new byte[16];

        BspLightmaps.Compose(face.Styles, static _ => 1f, [30f, 0f, 0f, 30f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f], built);

        BspLightmaps.Load(built.AsSpan(0, 4)).Red.ShouldBe(150f, 1.5f);
        BspLightmaps.Load(built.AsSpan(4, 4)).Red.ShouldBe(122.73f, 1f);
        BspLightmaps.Load(built.AsSpan(8, 4)).Red.ShouldBe(122.73f, 1f);
        BspLightmaps.Load(built.AsSpan(12, 4)).Red.ShouldBe(204.55f, 2f);
    }

    /// <summary>
    /// The output-level half: on the parity map, what <see cref="BspLightmaps.ReadAll"/> hands the atlas averages to
    /// the flat set on every bump-lit luxel.
    /// </summary>
    /// <remarks>
    /// Left out, and counted: a channel whose three sets are all zero (the engine's zero scale), and one where a set
    /// reached the store's ceiling of 16, which clamps after the scale. The tolerance is the store's: a five- or six-bit
    /// mantissa on each of four values.
    /// </remarks>
    [Test]
    public void ReadAll_CpProcessF12BumpLitFaces_AverageTheirDirectionalSetsToTheFlatSet()
    {
        string map = Path.Combine(GameInstall.Require(), "maps", "cp_process_f12.bsp");

        if (!File.Exists(map))
        {
            Assert.Ignore("cp_process_f12 is not installed");
            return;
        }

        const float Ceiling = BspLightmaps.HdrLightMapScale * 255f * 0.98f;
        long faces = 0;
        long compared = 0;
        long zero = 0;
        long clamped = 0;
        double flatSum = 0;
        double averageSum = 0;
        Span<float> light = stackalloc float[12];

        foreach (BspFaceLighting face in BspLightmaps.ReadAll(File.ReadAllBytes(map)))
        {
            if (!face.IsBumped)
            {
                continue;
            }

            faces++;

            for (int at = 0; at + 4 <= face.Flat.Pixels.Length; at += 4)
            {
                Channels(face.Flat, at, light[..3]);
                Channels(face.Directional[0], at, light.Slice(3, 3));
                Channels(face.Directional[1], at, light.Slice(6, 3));
                Channels(face.Directional[2], at, light.Slice(9, 3));

                for (int channel = 0; channel < 3; channel++)
                {
                    float flat = light[channel];
                    float one = light[3 + channel];
                    float two = light[6 + channel];
                    float three = light[9 + channel];

                    if (one + two + three <= 0f)
                    {
                        zero++;
                        continue;
                    }

                    if (MathF.Max(flat, MathF.Max(one, MathF.Max(two, three))) >= Ceiling)
                    {
                        clamped++;
                        continue;
                    }

                    float average = (one + two + three) / 3f;

                    compared++;
                    flatSum += flat;
                    averageSum += average;
                    average.ShouldBe(flat, (flat * 0.05f) + 0.05f, $"luxel {at / 4} channel {channel}: {one} {two} {three}");
                }
            }
        }

        TestContext.Out.WriteLine(
            $"F12 BUMPED {faces} faces, {compared} channels compared, {zero} all-zero, {clamped} at the ceiling; " +
            $"mean directional average / mean flat {averageSum / Math.Max(flatSum, 1e-9):0.0000}");

        faces.ShouldBeGreaterThan(0, "the control: the map has bump-lit faces");
        compared.ShouldBeGreaterThan(zero + clamped, "the control: most channels were actually compared");
    }

    private static void Channels(BspLightmap lightmap, int at, Span<float> into) =>
        (into[0], into[1], into[2]) = BspLightmaps.Load(lightmap.Pixels.Span.Slice(at, 4));

    private static float Red(BspLightmap lightmap) => BspLightmaps.Load(lightmap.Pixels.Span).Red;

    /// <summary>One bump-lit face of one luxel over the given lighting: four sets per style, style 0 and optionally a second.</summary>
    private static byte[] BumpedMap(byte[] lighting, byte second = 255)
    {
        byte[] face = new byte[56];
        byte[] texinfo = new byte[72];

        (face[16], face[17], face[18], face[19]) = ((byte)0, second, (byte)255, (byte)255);
        BinaryPrimitives.WriteInt32LittleEndian(texinfo.AsSpan(TexinfoFlagsOffset), SurfBumpLight);

        return SyntheticBsp.Build(new Dictionary<int, byte[]>
        {
            [TexinfoLump] = texinfo,
            [FacesLump] = face,
            [LightingLump] = lighting,
        });
    }
}
