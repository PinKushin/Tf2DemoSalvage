using System.IO;

using Tf2DemoSalvage.Content.Bsp;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>A baked static prop's colour mesh reaching the model shader (B426).</summary>
/// <remarks>
/// `engine.dll` `0x1800f1bd0` draws a static prop with baked colours through the model draw with its
/// per-placement colour mesh — one colour per vertex of the shared model. Here that mesh is a second
/// vertex stream, multiplied into the vertex colour. `mat_drawflat` makes the albedo white and an even cube
/// lights the quad alike, so with no stream the picture is one grey, and with one each triangle keeps that
/// grey in its own channel only: red where the stream says red, blue where it says blue. (An offscreen draw
/// binds no lightmap, which is why the test lights through a cube rather than as a real baked prop draws.)
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
                light: Even,
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

    /// <summary>The same light from every side, so every face of the quad is lit alike.</summary>
    private static AmbientCube Even =>
        new((0.5f, 0.5f, 0.5f), (0.5f, 0.5f, 0.5f), (0.5f, 0.5f, 0.5f),
            (0.5f, 0.5f, 0.5f), (0.5f, 0.5f, 0.5f), (0.5f, 0.5f, 0.5f));

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
