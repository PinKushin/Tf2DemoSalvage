using System.IO;

using Tf2DemoSalvage.Content.Bsp;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>A baked static prop's colour mesh reaching the model shader (B426).</summary>
/// <remarks>
/// `engine.dll` `0x1800f1bd0` draws a static prop with baked colours through the model draw with its
/// per-placement colour mesh — one colour per vertex of the shared model. Here that mesh is a second
/// vertex stream which, with no cube, multiplies the light. `mat_drawflat` makes the albedo white, so with no
/// stream the picture is one grey, and with one each triangle keeps that grey in its own channel only: red
/// where the stream says red, blue where it says blue. (This test drew through an even cube until B424's
/// correction, when a colour mesh with a cube became the static-plus-dynamic sum rather than a product.)
/// </remarks>
public sealed class BakedColourStreamRenderTests
{
    [Test]
    public void DrawModelPose_WithAColourPerVertex_PaintsEachTriangleItsOwnColour()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (GameInstall.Root is not { } tf ||
            !File.Exists(Path.Combine(tf, "maps", "cp_process_final.bsp")))
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        MapAssets assets = MapCache.With().Assets;

        ((int Red, int Green, int Blue) LowerRight, (int Red, int Green, int Blue) UpperLeft) Draw(float[]? colours)
        {
            target.Clear(0f, 0f, 0f);
            target.DrawModelPose(
                Face(),
                [new WorldBatch(0, 0, 6)],
                Camera(),
                Identity(),
                assets,
                bothSides: true,
                debug: new DebugModes(DrawFlat: true),
                bakedColours: colours);

            return (target.PixelAt(48, 48), target.PixelAt(16, 16));
        }

        // The first triangle red, the second blue: a stream read at stride zero, or not at all, cannot
        // give the two halves different colours.
        float[] baked = [1f, 0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f];

        ((int Red, int Green, int Blue) LowerRight, (int Red, int Green, int Blue) UpperLeft) none = Draw(null);
        ((int Red, int Green, int Blue) LowerRight, (int Red, int Green, int Blue) UpperLeft) coloured = Draw(baked);

        TestContext.Out.WriteLine($"BAKED STREAM none {none} / coloured {coloured}");

        // The control: drawn, and grey — no stream multiplies by white.
        none.LowerRight.Red.ShouldBeGreaterThan(0, "the model must be drawn before a colour can be read on it");
        none.LowerRight.ShouldBe((none.LowerRight.Red, none.LowerRight.Red, none.LowerRight.Red));
        none.UpperLeft.ShouldBe(none.LowerRight);

        coloured.LowerRight.ShouldBe((none.LowerRight.Red, 0, 0), "the first triangle's vertices are red");
        coloured.UpperLeft.ShouldBe((0, 0, none.LowerRight.Red), "the second triangle's vertices are blue");
    }

    /// <remarks>
    /// **The colour mesh IS the static light, decoded and nothing else.** `DoLighting` (`common_vs_fxc.h:870-874`) and
    /// `PixelShaderDoLightingLinear` (`common_vertexlitgeneric_dx9.h:272`) take <c>GammaToLinear( colour · 2 )</c> as the
    /// whole static term: no lightmap multiplies it. So a colour byte of 64 drawn over a white albedo with no cube
    /// writes pow( 128 / 255, 2.2 ) = 0.2196 → 56 to the offscreen target, which stores the shader's output as it is.
    /// </remarks>
    [Test]
    public void DrawModelPose_ABakedByteWithNoCube_WritesGammaToLinearOfTwiceIt()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (GameInstall.Root is not { } tf ||
            !File.Exists(Path.Combine(tf, "maps", "cp_process_final.bsp")))
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        MapAssets assets = MapCache.With().Assets;
        float term = PropModels.FromVertexByte(64);

        target.Clear(0f, 0f, 0f);
        target.DrawModelPose(
            Face(), [new WorldBatch(0, 0, 6)], Camera(), Identity(), assets,
            bothSides: true, debug: new DebugModes(DrawFlat: true), bakedColours: [.. System.Linq.Enumerable.Repeat(term, 18)]);

        int expected = (int)System.MathF.Round(System.MathF.Pow(128f / 255f, 2.2f) * 255f);
        expected.ShouldBe(56, "worked by hand");
        target.PixelAt(32, 32).ShouldBe((expected, expected, expected));
    }

    /// <remarks>
    /// **B424 corrected: static plus dynamic, ADDED.** On DX9 the model draw keeps the colour mesh and hands the studio
    /// render the static-plus-dynamic flag (`engine.dll` `0x1800f1bd0`, `param_8 + 0x40`) with a lighting state of zero
    /// cube plus local lights (`FUN_1801ba590` flags 6). The shader sums them: `PixelShaderDoLightingLinear`,
    /// `common_vertexlitgeneric_dx9.h:268-315` — static colour, then cube, then each light, all `+=`. So in linear light
    /// the pixel is the colours-only pixel plus the lamp-only pixel, within one step of the 8-bit target.
    /// </remarks>
    [Test]
    public void DrawModelPose_ColoursWithALamp_AddTheLampToTheColours()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (GameInstall.Root is not { } tf ||
            !File.Exists(Path.Combine(tf, "maps", "cp_process_final.bsp")))
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        MapAssets assets = MapCache.With().Assets;

        (int Red, int Green, int Blue) Draw(AmbientCube? light, float[]? colours, LocalLight[]? locals)
        {
            target.Clear(0f, 0f, 0f);
            target.DrawModelPose(
                Face(), [new WorldBatch(0, 0, 6)], Camera(), Identity(), assets,
                light: light, bothSides: true, debug: new DebugModes(DrawFlat: true), bakedColours: colours, locals: locals);

            return target.PixelAt(32, 32);
        }

        float[] dim = [.. System.Linq.Enumerable.Repeat(0.1f, 18)];

        // A red lamp 150 in front of the quad, so the sum shows in one channel and not the others.
        LocalLight[] lamp = [new LocalLight(0f, -150f, 0f, 0.3f, 0f, 0f, 0f, 0f, 1f / (150f * 150f), 1000f)];

        (int Red, int Green, int Blue) colours = Draw(null, dim, null);
        (int Red, int Green, int Blue) lampOnly = Draw(default(AmbientCube), null, lamp);
        (int Red, int Green, int Blue) both = Draw(default(AmbientCube), dim, lamp);

        TestContext.Out.WriteLine($"STATIC+DYNAMIC colours {colours} / lamp {lampOnly} / both {both}");

        colours.Red.ShouldBeGreaterThan(0, "the control: the colours alone draw");
        lampOnly.Red.ShouldBeGreaterThan(0, "the control: the lamp alone lights");
        lampOnly.Green.ShouldBe(0, "the control: the lamp is red");

        both.Green.ShouldBeInRange(colours.Green - 1, colours.Green + 1, "the lamp adds no green");

        // The offscreen target stores the shader's output as it is (0.1 of colour reads 26, 0.1 · 255), so the sum is a
        // sum of bytes.
        int predicted = colours.Red + lampOnly.Red;
        both.Red.ShouldBeInRange(predicted - 1, predicted + 1, "colours plus lamp");
    }

    /// <summary>Two triangles facing the camera: the first lower right, the second upper left.</summary>
    private static WorldVertex[] Face() =>
    [
        Vertex(-200f, -200f),
        Vertex(200f, -200f),
        Vertex(200f, 200f),
        Vertex(-200f, -200f),
        Vertex(200f, 200f),
        Vertex(-200f, 200f),
    ];

    private static WorldVertex Vertex(float x, float z) =>
        new(x, 0f, z, 0f, 0f, 0f, 0f, 0f) { NormalY = -1f, NormalZ = 0f };

    private static float[] Camera() =>
        new FreeCamera
        {
            Origin = (0f, -300f, 0f),
            Angles = (0f, 90f, 0f),
            Aspect = 1f,
        }.ToMatrix();

    private static float[] Identity() =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];
}
