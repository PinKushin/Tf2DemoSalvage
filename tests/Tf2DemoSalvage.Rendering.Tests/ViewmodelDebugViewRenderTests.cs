using System.IO;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Whether the debug views change a posed MODEL, which is what a viewmodel is.
/// </summary>
/// <remarks>
/// **B187: `mat_drawflat` and its neighbours changed the world and left the weapon in hand alone.**
/// The owner reported it alongside B186 and B170, and the cost is larger than the feature: B170 is
/// washed-out viewmodels, and the tools built to diagnose exactly that could not be pointed at the
/// thing that was wrong.
///
/// **The root cause was a call with too few arguments.** `Device3D.DrawViewmodels` set the pass
/// camera with `SetCamera(_device, _context, camera)` — three arguments against a method whose
/// remaining four are OPTIONAL — so the viewmodel pass ran with `fullbright: Off, debug: default`
/// while the world around it ran with whatever the user had chosen. It compiled, it ran, and it drew
/// something plausible.
///
/// **In TF2 these are material-system overrides**, applied to everything drawn rather than to a
/// pass, so a viewmodel exempt from them is a departure nobody chose.
///
/// ## What this can and cannot check
///
/// **It checks the half that could genuinely have been missing**: whether the model shader honours a
/// debug mode at all, on the same `DrawModelPose` path a viewmodel takes. If it did not, passing the
/// state at the call site would have fixed nothing.
///
/// **It cannot check the call site**, because `DrawViewmodels` is private to `Device3D` and the
/// state it sets is written into a constant buffer rather than exposed. That half is one line —
/// passing what the world pass already passes — and its confirmation is a person looking at the
/// viewer, which is the honest answer for a visual claim rather than an assertion dressed up as one.
///
/// **The harness reproduced the bug rather than exposing it**, which is why a full offscreen render
/// suite did not catch this: `OffscreenTarget.DrawModelPose` made the same three-argument call, so
/// every posed-model test in this project has always drawn with the debug views off.
/// </remarks>
public sealed class ViewmodelDebugViewRenderTests
{
    /// <summary>A player model from the installed game, or null when there is none.</summary>
    private static MapAssets? Assets
    {
        get
        {
            if (Tf2Install.Folder is not { } tf ||
                !File.Exists(Path.Combine(tf, "maps", "cp_process_final.bsp")))
            {
                return null;
            }

            // A PLAYER model, because that is the population a viewmodel belongs to — the weapon in
            // hand is a `v_` or `c_` model, not map geometry.
            return MapCache.Load(entityModels: ["models/player/scout.mdl"]);
        }
    }

    [Test]
    public void DrawModelPose_WithDrawFlat_ChangesThePicture()
    {
        // **`mat_drawflat` replaces the texture with flat white and keeps the lighting**, so a
        // textured surface and a flat one cannot land on the same colour unless the mode was
        // ignored. That is the whole assertion: not what the colour becomes, but that it moves.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        (int Red, int Green, int Blue) Draw(DebugModes debug)
        {
            target.Clear(0f, 0f, 0f);
            target.DrawModelPose(
                Face(),
                [new WorldBatch(0, 0, 6)],
                Camera(),
                Identity(),
                assets,

                // **An ambient cube is REQUIRED or the model draws black**, which cost this test a
                // run: the model shader wraps its whole direct term in `if (ambientCube[0].w > 0.5)`,
                // so a model with no cube gets neither ambient nor sun. `PhongRenderTests` records
                // the same trap, and the control below is what turned it into a diagnosis rather
                // than a confusing "the mode changed nothing".
                light: Neutral,
                bothSides: true,
                debug: debug);

            return target.PixelAt(32, 32);
        }

        (int Red, int Green, int Blue) normal = Draw(DebugModes.None);
        (int Red, int Green, int Blue) flat = Draw(new DebugModes(DrawFlat: true));

        TestContext.Out.WriteLine($"VIEWMODEL DEBUG normal {normal} / drawflat {flat}");

        // **A control first**: if the model drew nothing at all, both readings would be the cleared
        // background and "the mode changed nothing" would be indistinguishable from "there was
        // nothing to change". That is the trap B187's own first test fell into elsewhere today.
        (normal.Red + normal.Green + normal.Blue).ShouldBeGreaterThan(
            0, "the model must actually be drawn before a debug view can be measured on it");

        flat.ShouldNotBe(normal, "mat_drawflat must reach a posed model, which is what a viewmodel is");
    }

    /// <summary>Two triangles facing the camera, textured across their whole extent.</summary>
    private static WorldVertex[] Face()
    {
        (float X, float Y, float Z) normal = (0f, -1f, 0f);

        return
        [
            Vertex(-64f, 0f, -64f, 0f, 0f, normal),
            Vertex(64f, 0f, -64f, 1f, 0f, normal),
            Vertex(64f, 0f, 64f, 1f, 1f, normal),
            Vertex(-64f, 0f, -64f, 0f, 0f, normal),
            Vertex(64f, 0f, 64f, 1f, 1f, normal),
            Vertex(-64f, 0f, 64f, 0f, 1f, normal),
        ];
    }

    private static WorldVertex Vertex(
        float x, float y, float z, float u, float v, (float X, float Y, float Z) normal) =>
        new(x, y, z, u, v, 0f, 0f, 0f)
        {
            NormalX = normal.X,
            NormalY = normal.Y,
            NormalZ = normal.Z,
        };

    private static float[] Camera() =>
        new FreeCamera
        {
            Origin = (0f, -300f, 0f),
            Angles = (0f, 90f, 0f),
            Aspect = 1f,
        }.ToMatrix();

    /// <summary>The same ambient on every face, so it contributes a constant to both draws.</summary>
    private static AmbientCube Neutral =>
        new((0.1f, 0.1f, 0.1f), (0.1f, 0.1f, 0.1f), (0.1f, 0.1f, 0.1f),
            (0.1f, 0.1f, 0.1f), (0.1f, 0.1f, 0.1f), (0.1f, 0.1f, 0.1f));

    private static float[] Identity() =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];
}
