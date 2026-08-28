using System;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// That the model constant buffer's size and the shader's own declaration agree.
/// </summary>
/// <remarks>
/// **Nothing at runtime can catch this, and the failure is spectacular.** A buffer smaller than the
/// `cbuffer` the shader declares leaves D3D reading past the end of the allocation, and
/// `Map.WriteDiscard` renames it each frame — so the tail is different garbage every frame. That is
/// exactly what happened when a `float4` was added to the material buffer and a replace-all grew
/// two of the three arrays: the whole scene strobed between two colours and the owner's report was
/// *"the colors are kinda doing a disco now"*.
///
/// **The material buffer got a runtime guard; this one cannot have the same.** `SetMaterial` throws
/// when the array it is handed disagrees with the shader struct's length, because it is handed an
/// array whose length is meaningful. The model buffer is filled by index from a single sized array,
/// so there is nothing to compare at runtime — the disagreement is between a `const int` and a
/// string of HLSL in the same file, and only a reader of both can see it.
///
/// **So the denominator is generated from the shader source.** Counting the float4s in the
/// declaration means a field added to the HLSL and forgotten in the constant fails here, and a
/// number nobody maintains cannot go stale — the split
/// `docs/memory/instrument-bugs-outnumber-decoder-bugs.md` describes as generated-catches-missing
/// against hand-written-catches-wrong.
/// </remarks>
public sealed class ModelConstantLayoutTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    [Test]
    public void ModelConstants_AgainstTheShadersOwnDeclaration_AreTheSameSize()
    {
        string shader = ShaderSource();

        Match block = Regex.Match(
            shader,
            @"cbuffer Model : register\(b2\)\s*\{(.*?)\n *\};",
            RegexOptions.Singleline,
            Limit);

        block.Success.ShouldBeTrue("the Model cbuffer is declared in the shader source");

        int floats = FloatsIn(block.Groups[1].Value);

        floats.ShouldBe(
            Declared(),
            "the cbuffer and ModelConstants must describe the same buffer, or D3D reads past it");
    }

    /// <summary>That the counter can see a field, so a zero above would be a real answer.</summary>
    /// <remarks>
    /// **The control, and it is the point of the whole file.** A regex that matched nothing would
    /// report zero floats and fail loudly — but one that matched the block and counted nothing
    /// inside it would report zero and could be "fixed" by setting the constant to zero. This says
    /// the counter finds the fields it is supposed to find.
    /// </remarks>
    [Test]
    public void FloatsIn_ForADeclarationWithKnownFields_CountsEveryOne()
    {
        const string Sample = """
            row_major float4x4 model;
            float4 ambientCube[6];
            float4 sunColour;
            float4 localLightPosition[4];
            """;

        // Sixteen for the matrix, twenty-four for the cube, four for the sun, sixteen for the lamps.
        FloatsIn(Sample).ShouldBe(60);
    }

    /// <summary>Counts the floats a `cbuffer` body declares, arrays included.</summary>
    private static int FloatsIn(string body)
    {
        int floats = 0;

        foreach (Match matrix in Regex.Matches(body, @"float4x4 \w+;", RegexOptions.None, Limit))
        {
            _ = matrix;
            floats += 16;
        }

        foreach (Match vector in
            Regex.Matches(body, @"float4 \w+(\[(\d+)\])?;", RegexOptions.None, Limit))
        {
            floats += 4 * (vector.Groups[2].Success
                ? int.Parse(vector.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)
                : 1);
        }

        return floats;
    }

    /// <summary>The shader text, which lives as a string constant on the renderer.</summary>
    /// <remarks>
    /// Found by CONTENT rather than by name, because the field's name is an implementation detail
    /// and a rename would otherwise turn this guard off silently — which is the failure it exists
    /// to prevent, one level up.
    /// </remarks>
    private static string ShaderSource() =>
        ShaderField() ?? throw new InvalidOperationException(
            "the renderer's shader source could not be found; this test reads it by reflection");

    private static string? ShaderField()
    {
        foreach (FieldInfo field in typeof(WorldRenderer).GetFields(
            BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public))
        {
            if (field.FieldType == typeof(string) &&
                field.GetValue(null) is string text &&
                text.Contains("cbuffer Model", StringComparison.Ordinal))
            {
                return text;
            }
        }

        return null;
    }

    private static int Declared()
    {
        FieldInfo? constant = typeof(WorldRenderer).GetField(
            "ModelConstants", BindingFlags.NonPublic | BindingFlags.Static);

        constant.ShouldNotBeNull("ModelConstants sizes the buffer and this test reads it");

        return (int)constant.GetRawConstantValue()!;
    }
}
