using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Whether the material constant buffer is the size the shader reading it declares.
/// </summary>
/// <remarks>
/// **They disagreed by two float4s for as long as <c>$envmap</c> has existed, and the picture was
/// correct throughout.** <c>EnsureMaterialBuffer</c> sizes the buffer from
/// <c>WorldRenderer.MaterialRestingValues</c>, and its comment said the two therefore *"cannot
/// disagree"*. The resting array did not grow when the shader's <c>Material</c> block gained
/// <c>envmapTint</c> and <c>envmapControl</c>, so the buffer was created 160 bytes wide against a
/// declared 192, and <c>SetMaterial</c> copied 192 bytes into it.
///
/// **An out-of-bounds write into a mapped constant buffer, and an out-of-bounds read by the
/// shader.** It drew reflections whose pixels this project then measured and asserted on. Nothing
/// could see it: every instrument here reads the picture, and the picture was right — this driver
/// simply tolerated the overrun.
///
/// **What it would have looked like on a driver that did not.** A read past a constant buffer
/// returns zero, and `hasEnvmap` sits in the part that fell off — so reflections would vanish
/// entirely, on someone else's machine, with every test still green here.
///
/// A comment stating an invariant is not an invariant. This is.
/// </remarks>
public sealed class MaterialBufferTests
{
    [Test]
    public void MaterialBuffer_ItsRestingValues_FillTheShadersOwnDeclaration()
    {
        // **Counted from the shader source rather than from a number typed twice**, which is the
        // whole point: a literal here would need editing every time the struct grows, and that is
        // precisely the edit that was missed.
        string source = WorldRenderer.ShaderSourceText;

        int start = source.IndexOf("cbuffer Material", StringComparison.Ordinal);

        start.ShouldBeGreaterThan(-1, "the shader declares a Material constant buffer");

        int end = source.IndexOf("};", start, StringComparison.Ordinal);

        end.ShouldBeGreaterThan(start);

        int rows = Regex.Count(source[start..end], @"^\s+float4\s+\w+", RegexOptions.Multiline);

        rows.ShouldBeGreaterThan(0, "the block declares float4 rows");

        WorldRenderer.MaterialRestingValues.Count.ShouldBe(
            rows * 4,
            $"the constant buffer is sized from these {WorldRenderer.MaterialRestingValues.Count} " +
            $"floats and the shader declares {rows} float4s; a shortfall writes past the end of a " +
            "mapped buffer and reads zeros for whatever fell off");
    }

    [Test]
    public void MaterialBuffer_TheRestingReflection_IsNoReflectionRatherThanZero()
    {
        // **Zero is wrong for three of these six and that is why they are asserted.** A material
        // with no cubemap still gets the whole struct, so its resting values have to mean "reflect
        // nothing" rather than "all parameters zero":
        //
        //   tint white     — a tint of zero would black out any reflection that IS bound
        //   contrast 0     — normal; 1 squares it
        //   saturation 1   — normal; 0 is greyscale
        //   Fresnel 1      — NO falloff, which is the engine's default ("1.0 == mirror")
        //
        // Contrast and saturation point opposite ways at the same number, and Fresnel's identity is
        // 1 where a term called "fresnel" would be expected to rest at 0. An implementation
        // defaulting the block to zero greys out every reflection and attenuates it to nothing, and
        // neither reads as an error.
        // **Located by NAME, not by counting back from the end.** The first version of this test
        // indexed `[^8]` through `[^1]`, which was correct until $phong added three rows after the
        // reflection's and silently moved the subject — so the test would have been asserting the
        // phong tint's values against the envmap's expectations. Asking the shader where its own
        // rows are cannot drift.
        float[] resting = [.. WorldRenderer.MaterialRestingValues];

        int tintRow = Row("envmapTint");
        int controlRow = Row("envmapControl");

        controlRow.ShouldBe(tintRow + 1, "the two reflection rows are adjacent");

        (resting[tintRow * 4], resting[(tintRow * 4) + 1], resting[(tintRow * 4) + 2],
                resting[(tintRow * 4) + 3])
            .ShouldBe((1f, 1f, 1f, 0f), "white, and contrast normal at zero");

        (resting[controlRow * 4], resting[(controlRow * 4) + 1], resting[(controlRow * 4) + 2],
                resting[(controlRow * 4) + 3])
            .ShouldBe((1f, 0f, 0f, 1f), "saturation normal at one, no mask, no cube, no falloff");

        static int Row(string name)
        {
            string source = WorldRenderer.ShaderSourceText;
            int start = source.IndexOf("cbuffer Material", StringComparison.Ordinal);
            int end = source.IndexOf("};", start, StringComparison.Ordinal);

            string[] rows =
            [
                .. Regex.Matches(source[start..end], @"^\s+float4\s+(\w+)", RegexOptions.Multiline)
                    .Select(match => match.Groups[1].Value),
            ];

            int at = Array.IndexOf(rows, name);

            at.ShouldBeGreaterThan(-1, $"the shader declares a row called {name}");

            return at;
        }
    }
}
