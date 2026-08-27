using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// What the reflection adds to a REAL weapon model, rather than to a flat quad.
/// </summary>
/// <remarks>
/// **Every earlier measurement drew a quad, and a quad cannot answer this.** A flat surface with one
/// uniform normal samples the cubemap in a single direction; a weapon is curved, so its normals
/// sweep a hemisphere and different parts of it sample wildly different texels — including, on a
/// map with sky, the bright ones. `WeaponReflectionStrengthTests` measured 8 of 255 on a quad and
/// the owner sees a weapon washed out, and the geometry is the difference that was never tested.
///
/// **The manipulation is the owner's own**, 2026-08-27: *"toggling off makes it look right, togling
/// back on makes it look wrong again"*. Same draw, `mat_specular` on against off. Reproducible in
/// both directions, which is what ruled out the alternative that toggling merely re-sent the camera
/// constants and repaired stale state.
///
/// **The bound is arithmetic, not a guess.** `$envmaptint` scales the cubemap sample before anything
/// else touches it, and every placed cubemap on this map is `Dxt1` — LDR, so a texel cannot exceed
/// white. A material tinted to `t` therefore cannot brighten any channel by more than `t * 255`,
/// doubled here for headroom because contrast, saturation and filtering all run afterwards.
///
/// **What this still is not.** It draws real geometry through `OffscreenTarget`, not through
/// `Device3D.DrawViewmodels` — which has its own camera, its own near-compressed viewport and runs
/// after the world pass. If this passes, that pass is what is left, and it is the same gap that hid
/// B187.
/// </remarks>
public sealed class WeaponModelReflectionTests
{
    /// <summary>What one channel of tint can move a byte by, before headroom.</summary>
    private const float ByteRange = 255f;

    /// <summary>Slack: contrast, saturation and texture filtering all run after the tint.</summary>
    private const float Headroom = 2f;

    /// <summary>A weapon whose material asks for the map's own cubemap.</summary>
    private const string WeaponModel = "models/weapons/c_models/c_shotgun/c_shotgun.mdl";

    [Test]
    public void ReflectionOnARealWeaponModel_TurnedOnAndOff_AddsNoMoreThanItsTint()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(128, 128);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Loaded is not { } loaded)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        (MapAssets assets, EntityModelSet models) = loaded;

        IReadOnlyList<IReadOnlyList<WorldBatch>> frames = models.AllFrames(WeaponModel);

        if (frames.Count == 0 || frames[0].Count == 0 || models.Vertices.Count == 0)
        {
            Assert.Ignore($"{WeaponModel} carried no geometry");
            return;
        }

        float tint = TintOf(assets);

        if (tint <= 0f)
        {
            Assert.Ignore("no material on this weapon asks for the map's own cubemap");
            return;
        }

        BspCubemapPlacementOrigin at = Origin(assets);

        (int R, int G, int B) on = Draw(target, assets, models, frames[0], at, specular: true);
        (int R, int G, int B) off = Draw(target, assets, models, frames[0], at, specular: false);

        int added = Math.Max(
            Math.Abs(on.R - off.R), Math.Max(Math.Abs(on.G - off.G), Math.Abs(on.B - off.B)));

        float ceiling = tint * ByteRange * Headroom;

        TestContext.Out.WriteLine(
            $"REAL WEAPON MODEL {WeaponModel}: tint {tint:0.###}, " +
            $"mat_specular 1 {on}, mat_specular 0 {off}, added {added}, ceiling {ceiling:0.#}");

        // **The control, and it is deliberately strong.** A model that failed to draw reads as the
        // cleared background in both, `added` is zero, and the bound below passes on an absence.
        // The near-black `(1, 0, 0)` that an earlier version of this measurement produced cleared a
        // `> 0` control and measured nothing at all.
        (off.R + off.G + off.B).ShouldBeGreaterThan(
            30, "the weapon must be visibly drawn with the reflection off before the pair means anything");

        ((float)added).ShouldBeLessThan(
            ceiling,
            $"$envmaptint scales the cubemap sample, so a weapon tinted to {tint:0.###} cannot " +
            $"brighten a channel by more than that fraction of an LDR cube — a larger figure is " +
            $"the reflection arriving at a strength its material never asked for (B170)");
    }

    /// <summary>
    /// The same weapon drawn AT THE EYE, which is where a viewmodel actually is.
    /// </summary>
    /// <remarks>
    /// **The one condition none of the earlier measurements reproduced.** A world prop stands metres
    /// away; a viewmodel's model matrix IS the camera transform, so its geometry sits on top of the
    /// eye. The reflection is
    /// `reflect(-normalize(eyePosition - wpos), normal)`, and as `wpos` approaches `eyePosition`
    /// that subtraction collapses toward zero — where `normalize` is numerically unstable and the
    /// sampled direction stops being a reflection at all.
    ///
    /// **Which is a mechanism specific to viewmodels**, and that is exactly the shape of B170: only
    /// weapons (arms declare no `$envmap`, so a wrong direction costs them nothing), on every modern
    /// weapon, unaffected by `mat_fullbright` because the reflection is added either way, and
    /// removed outright by `mat_specular 0`.
    ///
    /// Same model, same cubemap, same material, same tint. **Only the range differs**, which is what
    /// makes this and the test above a controlled pair rather than two separate measurements.
    /// </remarks>
    [Test]
    public void ReflectionOnARealWeaponModel_DrawnAtTheEye_AddsNoMoreThanItsTint()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(128, 128);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Loaded is not { } loaded)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        (MapAssets assets, EntityModelSet models) = loaded;

        IReadOnlyList<IReadOnlyList<WorldBatch>> frames = models.AllFrames(WeaponModel);

        if (frames.Count == 0 || frames[0].Count == 0 || models.Vertices.Count == 0)
        {
            Assert.Ignore($"{WeaponModel} carried no geometry");
            return;
        }

        float tint = TintOf(assets);

        if (tint <= 0f)
        {
            Assert.Ignore("no material on this weapon asks for the map's own cubemap");
            return;
        }

        BspCubemapPlacementOrigin at = Origin(assets);

        (int R, int G, int B) on = Draw(target, assets, models, frames[0], at, specular: true, EyeRange);
        (int R, int G, int B) off = Draw(target, assets, models, frames[0], at, specular: false, EyeRange);

        int added = Math.Max(
            Math.Abs(on.R - off.R), Math.Max(Math.Abs(on.G - off.G), Math.Abs(on.B - off.B)));

        float ceiling = tint * ByteRange * Headroom;

        TestContext.Out.WriteLine(
            $"REAL WEAPON AT THE EYE ({EyeRange}u): tint {tint:0.###}, " +
            $"mat_specular 1 {on}, mat_specular 0 {off}, added {added}, ceiling {ceiling:0.#}");

        (off.R + off.G + off.B).ShouldBeGreaterThan(
            30, "the weapon must be visibly drawn with the reflection off before the pair means anything");

        ((float)added).ShouldBeLessThan(
            ceiling,
            $"a viewmodel is drawn at the eye; if the tint holds at 40 units and not at {EyeRange}, " +
            $"the reflection a VIEWMODEL receives is not the one its material asked for (B170)");
    }

    /// <summary>How close the eye is to a viewmodel's own geometry, in world units.</summary>
    /// <remarks>
    /// **Not zero, because a viewmodel is not exactly at the eye and a zero-length view vector would
    /// be a condition the renderer never actually meets.** `Device3D.DrawViewmodels` logs the posed
    /// model's forward tip as `tip36`, so the arms and weapon occupy roughly the first thirty-six
    /// units; the near parts of them are a few units out. Five is inside that and is a range the
    /// real pass genuinely draws at.
    /// </remarks>
    private const float EyeRange = 5f;

    /// <summary>Where to stand the model: a real cubemap placement, so it reflects a real cube.</summary>
    private readonly record struct BspCubemapPlacementOrigin(float X, float Y, float Z);

    private static BspCubemapPlacementOrigin Origin(MapAssets assets) =>
        assets.PlacedCubemaps.Count > 0
            ? new BspCubemapPlacementOrigin(
                assets.PlacedCubemaps[0].Placement.X,
                assets.PlacedCubemaps[0].Placement.Y,
                assets.PlacedCubemaps[0].Placement.Z)
            : new BspCubemapPlacementOrigin(0f, 0f, 0f);

    /// <summary>The largest reflection tint among this weapon's own materials.</summary>
    private static float TintOf(MapAssets assets)
    {
        float largest = 0f;

        for (int index = 0; index < assets.Materials.Count; index++)
        {
            if (!assets.Materials[index].Name.Contains("c_shotgun", StringComparison.OrdinalIgnoreCase) ||
                index >= assets.LocalReflections.Count ||
                assets.LocalReflections[index] is not { } shading)
            {
                continue;
            }

            largest = Math.Max(
                largest,
                Math.Max(shading.Tint.Red, Math.Max(shading.Tint.Green, shading.Tint.Blue)));
        }

        return largest;
    }

    /// <summary>The map, plus the weapon's real geometry loaded the way the viewer loads it.</summary>
    /// <remarks>
    /// **`EntityModelSet.Geometry` is the seam**, and `LevelSystems` sets it to `MapAssets.Geometry`
    /// on a map load. Setting the same thing here is what makes this the viewer's own path rather
    /// than a second loader that could drift from it.
    /// </remarks>
    private static (MapAssets Assets, EntityModelSet Models)? Loaded
    {
        get
        {
            if (GameInstall.Root is not { } tf ||
                !File.Exists(Path.Combine(tf, "maps", "cp_process_final.bsp")))
            {
                return null;
            }

            MapAssets assets = MapCache.Load(entityModels: [WeaponModel]);

            EntityModelSet models = new();

            models.Geometry = assets.Geometry;
            models.Precache([WeaponModel]);

            return (assets, models);
        }
    }

    /// <summary>Draws the weapon at a cubemap placement and reads the brightest pixel it covers.</summary>
    /// <remarks>
    /// **The brightest pixel rather than the centre**, because a model does not fill the frame the
    /// way a quad does: the centre of a shotgun may be a dark grip, and a reflection washing the
    /// weapon out shows on the metal. Taking the maximum finds the part the reflection reaches
    /// wherever the model happens to sit, which a fixed coordinate cannot.
    /// </remarks>
    private static (int R, int G, int B) Draw(
        OffscreenTarget target,
        MapAssets assets,
        EntityModelSet models,
        IReadOnlyList<WorldBatch> batches,
        BspCubemapPlacementOrigin at,
        bool specular,
        float range = 40f)
    {
        float[] model =
        [
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            at.X, at.Y, at.Z, 1f,
        ];

        // Viewmodel range: close enough that the view vector sweeps across the model, which is the
        // condition a weapon is actually drawn in.
        float[] camera = new FreeCamera
        {
            Origin = (at.X, at.Y - range, at.Z),
            Angles = (0f, 90f, 0f),
            Aspect = 1f,
        }.ToMatrix();

        target.Clear(0f, 0f, 0f);
        target.DrawModelPose(
            models.Vertices,
            batches,
            camera,
            model,
            assets,

            // No ambient cube: the shader takes its full-brightness path, which puts the albedo at
            // its largest and gives a reflection added on top the least room to hide.
            light: null,
            bothSides: true,
            specular: specular);

        (int R, int G, int B) brightest = (0, 0, 0);

        for (int y = 0; y < 128; y += 2)
        {
            for (int x = 0; x < 128; x += 2)
            {
                (int R, int G, int B) pixel = target.PixelAt(x, y);

                if (pixel.R + pixel.G + pixel.B > brightest.R + brightest.G + brightest.B)
                {
                    brightest = pixel;
                }
            }
        }

        return brightest;
    }
}
