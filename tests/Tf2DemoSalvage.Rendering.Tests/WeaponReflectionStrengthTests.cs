using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Whether a weapon's reflection arrives at the strength its material asked for.
/// </summary>
/// <remarks>
/// **B170's arithmetic, turned into a measurement.** The owner: *"toggling reflections actually
/// makes the weapon look right too"*, and *"it is only the weapons too, not the arms or hands"*.
/// Read from the renderer's own load, the two weapons the f12 demo draws ask for very little:
///
/// <code>
/// c_scattergun  $envmaptint [.085 .085 .085]  no mask
/// c_shotgun     $envmaptint [.05 .05 .05]     no mask
/// </code>
///
/// **And all 43 of this map's placed cubemaps are `Dxt1`**, which is LDR — a texel cannot exceed
/// white. So the reflection this material asks for can move a channel by at most `0.085 * 255`,
/// about **22 levels of 255**, and a term that small is not something anyone notices, let alone
/// something whose removal makes a weapon "look right".
///
/// That is the contradiction this test resolves. A swing far above the tint's ceiling says the tint
/// is not reaching the shader, and the term is arriving at something near full strength.
///
/// **The bound is the assertion, and it is arithmetic rather than a guess.** `$envmaptint` scales
/// the cubemap sample before anything else touches it (`specular *= envmapTint.rgb`), so the
/// largest difference two normals can produce is the tint times the largest difference two texels
/// can hold — which for an LDR cubemap is one. Doubled for headroom, because contrast and
/// saturation run afterwards and the sample is filtered.
/// </remarks>
public sealed class WeaponReflectionStrengthTests
{
    /// <summary>What one channel of tint can move a byte by, before headroom.</summary>
    private const float ByteRange = 255f;

    /// <summary>
    /// Slack over the arithmetic ceiling: filtering, contrast and saturation all run after the tint.
    /// </summary>
    private const float Headroom = 2f;

    [Test]
    public void WeaponReflection_OnAMaterialTintedToATwelfth_StaysWithinThatTint()
    {
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

        if (Weapon(assets) is not { } found)
        {
            Assert.Ignore("no weapon material on this map asks for the map's own cubemap");
            return;
        }

        (int Index, float Tint, string Name) = found;

        BspCubemap at = assets.PlacedCubemaps[0].Placement;

        // Two normals, so the reflection samples two different directions of the cube. Everything
        // else about the draw is identical, which is what makes the difference the reflection.
        (int R, int G, int B) facingCamera = DrawModelAt(target, assets, Index, at, (0f, -1f, 0f));
        (int R, int G, int B) facingUp = DrawModelAt(target, assets, Index, at, (0f, 0f, 1f));

        int swing = Math.Max(
            Math.Abs(facingCamera.R - facingUp.R),
            Math.Max(
                Math.Abs(facingCamera.G - facingUp.G),
                Math.Abs(facingCamera.B - facingUp.B)));

        float ceiling = Tint * ByteRange * Headroom;

        TestContext.Out.WriteLine(
            $"WEAPON REFLECTION {Name}: tint {Tint:0.###}, facing camera {facingCamera}, " +
            $"facing up {facingUp}, swing {swing}, ceiling {ceiling:0.#}");

        // **The control, and `> 0` is not strong enough for it.** The first run of this test drew
        // the weapon at `(1, 0, 0)` — near black, because a dim ambient cube was supplied and the
        // shader's full-brightness path was therefore skipped — and a sum of 1 cleared a `> 0`
        // control while the swing measured nothing. A drawn, textured model at full brightness is
        // far above this; a black one is far below.
        (facingCamera.R + facingCamera.G + facingCamera.B).ShouldBeGreaterThan(
            30, "the model must be visibly drawn before its reflection can be measured");

        ((float)swing).ShouldBeLessThan(
            ceiling,
            $"$envmaptint scales the cubemap sample, so a material tinted to {Tint:0.###} cannot " +
            $"move a channel further than that fraction of an LDR cube; a larger swing means the " +
            $"tint is not reaching the shader");
    }

    /// <summary>
    /// The same measurement at VIEWMODEL range, which is where a weapon is actually drawn.
    /// </summary>
    /// <remarks>
    /// **The test above uses a condition where a correct renderer and a broken one agree**, which
    /// is the second of `CLAUDE.md`'s four ways a test cannot fail. It puts the camera 300 units
    /// from the surface — ordinary world-prop range — and the reflection behaved perfectly there.
    /// A viewmodel is drawn at the EYE.
    ///
    /// **Why the distance can matter at all.** The reflection is
    /// `reflect(-normalize(eyePosition - wpos), normal)`. At 300 units the view vector barely
    /// changes across a 128-unit quad; at arm's length it sweeps most of a hemisphere, so
    /// neighbouring pixels sample wildly different parts of the cube. Whether that stays inside the
    /// tint's ceiling is the question, and it is not one the far case can answer.
    ///
    /// Same material, same cubemap, same normals, same everything else — only the range differs,
    /// which is what makes the pair a controlled comparison rather than two measurements.
    /// </remarks>
    [Test]
    public void WeaponReflection_AtViewmodelRange_StaysWithinItsTint()
    {
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

        if (Weapon(assets) is not { } found)
        {
            Assert.Ignore("no weapon material on this map asks for the map's own cubemap");
            return;
        }

        (int Index, float Tint, string Name) = found;

        BspCubemap at = assets.PlacedCubemaps[0].Placement;

        (int R, int G, int B) near = DrawModelAt(target, assets, Index, at, (0f, -1f, 0f), ViewmodelRange);
        (int R, int G, int B) nearUp = DrawModelAt(target, assets, Index, at, (0f, 0f, 1f), ViewmodelRange);

        int swing = Math.Max(
            Math.Abs(near.R - nearUp.R),
            Math.Max(Math.Abs(near.G - nearUp.G), Math.Abs(near.B - nearUp.B)));

        float ceiling = Tint * ByteRange * Headroom;

        TestContext.Out.WriteLine(
            $"WEAPON REFLECTION AT {ViewmodelRange}u {Name}: tint {Tint:0.###}, " +
            $"facing camera {near}, facing up {nearUp}, swing {swing}, ceiling {ceiling:0.#}");

        (near.R + near.G + near.B).ShouldBeGreaterThan(
            30, "the model must be visibly drawn before its reflection can be measured");

        ((float)swing).ShouldBeLessThan(
            ceiling,
            $"a weapon is drawn at the eye, so if $envmaptint holds at 300 units and not at " +
            $"{ViewmodelRange}, the reflection a VIEWMODEL gets is not the one its material asked for");
    }

    /// <summary>How far a viewmodel sits from the eye, in world units.</summary>
    /// <remarks>
    /// **Measured from the viewer's own log, not chosen.** `Device3D.DrawViewmodels` reports the
    /// posed model's forward tip as `tip36`, so the arms and weapon occupy roughly the first
    /// thirty-six units in front of the camera. Twenty is inside that.
    /// </remarks>
    private const float ViewmodelRange = 20f;

    /// <summary>A weapon material that reflects the map's own cubemap, with its tint.</summary>
    /// <remarks>
    /// **Weakly tinted first**, because the whole point is to catch a tint that is being ignored,
    /// and the material with the smallest tint is where ignoring it shows most.
    /// </remarks>
    private static (int Index, float Tint, string Name)? Weapon(MapAssets assets)
    {
        List<(int Index, float Tint, string Name)> found = [];

        for (int index = 0; index < assets.Materials.Count; index++)
        {
            string name = assets.Materials[index].Name;

            if (!name.Contains("weapons/", StringComparison.OrdinalIgnoreCase) ||
                index >= assets.LocalReflections.Count ||
                assets.LocalReflections[index] is not { } shading ||
                assets.Textures.Count <= index || assets.Textures[index] is null)
            {
                continue;
            }

            found.Add((index, Math.Max(shading.Tint.Red, Math.Max(shading.Tint.Green, shading.Tint.Blue)), name));
        }

        return found.Count == 0 ? null : found.OrderBy(entry => entry.Tint).First();
    }

    /// <summary>The map plus the weapons the f12 parity demo actually draws.</summary>
    private static MapAssets? Assets
    {
        get
        {
            if (Tf2Install.Folder is not { } tf ||
                !File.Exists(Path.Combine(tf, "maps", "cp_process_final.bsp")))
            {
                return null;
            }

            return MapCache.Load(entityModels:
            [
                "models/weapons/c_models/c_scattergun.mdl",
                "models/weapons/c_models/c_shotgun/c_shotgun.mdl",
            ]);
        }
    }

    /// <summary>Draws a quad as a model at a cubemap's placement, and reads the centre.</summary>
    private static (int R, int G, int B) DrawModelAt(
        OffscreenTarget target,
        MapAssets assets,
        int material,
        BspCubemap at,
        (float X, float Y, float Z) normal,
        float range = 300f)
    {
        WorldVertex Corner(float dx, float dz, float u, float v) =>
            new(dx, 0f, dz, u, v, 0f, 0f, 0f)
            {
                NormalX = normal.X,
                NormalY = normal.Y,
                NormalZ = normal.Z,
            };

        WorldVertex[] face =
        [
            Corner(-64f, -64f, 0f, 0f), Corner(64f, -64f, 1f, 0f), Corner(64f, 64f, 1f, 1f),
            Corner(-64f, -64f, 0f, 0f), Corner(64f, 64f, 1f, 1f), Corner(-64f, 64f, 0f, 1f),
        ];

        // **The placement goes in the MODEL matrix, not into the vertices**, which is how
        // `ReflectionRenderTests` does it and is not cosmetic: the reflection is computed from
        // `input.wpos`, and building world coordinates into the vertices while passing an identity
        // matrix is a different arrangement from the one the renderer actually uses.
        float[] model =
        [
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            at.X, at.Y, at.Z, 1f,
        ];

        float[] camera = new FreeCamera
        {
            Origin = (at.X, at.Y - range, at.Z),
            Angles = (0f, 90f, 0f),
            Aspect = 1f,
        }.ToMatrix();

        target.Clear(0f, 0f, 0f);
        target.DrawModelPose(
            face,
            [new WorldBatch(material, 0, face.Length)],
            camera,
            model,
            assets,

            // **No ambient cube, deliberately, and this cost a run.** The model shader wraps its
            // whole direct term in `if (ambientCube[0].w > 0.5f)` and takes a FULL BRIGHTNESS path
            // when none is supplied. Supplying a dim uniform cube instead — 0.1 on every face —
            // drew the weapon at `(1, 0, 0)`, near black, and the swing between two normals then
            // measured nothing at all while the test reported a pass.
            //
            // Full brightness is also the right condition for this measurement: it puts the albedo
            // at its largest, so a reflection added on top has the least room to hide.
            light: null,
            bothSides: true);

        return target.PixelAt(32, 32);
    }
}
