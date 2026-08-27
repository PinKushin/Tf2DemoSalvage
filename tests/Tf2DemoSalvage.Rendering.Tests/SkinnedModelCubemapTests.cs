using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// That a model's cubemap is chosen from where the model IS, not from its model matrix.
/// </summary>
/// <remarks>
/// **B170's root cause, and it was found by a log line rather than by any of these tests.**
/// `DrawModel` picked its cubemap with
/// `BspCubemaps.Closest(_placements, matrix[12], matrix[13], matrix[14])` — the model matrix's
/// translation. For a BAKED model that is its world position and the choice is right. For a
/// **skinned** model the placement travels in the bones and the matrix stays at identity, so the
/// translation is `(0, 0, 0)` and the lookup asks which cubemap is nearest the MAP ORIGIN.
///
/// Measured in the viewer on `cp_process_f12`, 2026-08-27, with the eye at `(-4816, -1280, 648)`:
///
/// <code>
/// c_scattergun  at (0, 0, 0) reflects cubemap 39 of 40 at (0, 0, 608)
/// c_scout_arms  at (0, 0, 0) reflects cubemap 39 of 40 at (0, 0, 608)
/// scout         at (0, 0, 0) reflects cubemap 39 of 40 at (0, 0, 608)
/// soldier       at (0, 0, 0) reflects cubemap 39 of 40 at (0, 0, 608)
/// </code>
///
/// Every skinned model on the map reflecting one cube, chosen from a position none of them occupy,
/// about five thousand units from the player. Weapons show it because TF2's `c_` weapon materials
/// declare `$envmap` and arms and player skins do not — which is precisely the owner's report:
/// *"it is only the weapons too, not the arms or hands"*.
///
/// ## Why four earlier offscreen tests could not catch it
///
/// **Every one of them translated its model through the model matrix**, so `matrix[12..14]` was the
/// real position and the lookup was correct by construction. That is the second of `CLAUDE.md`'s
/// four ways a test cannot fail — an input for which correct and broken predict the same
/// observation — and it was built four separate times while concluding the reflection was fine.
///
/// This test supplies the two independently, which is the only arrangement where they can disagree.
/// </remarks>
public sealed class SkinnedModelCubemapTests
{
    [Test]
    public void ModelCubemap_ChosenForASkinnedModel_ComesFromItsOwnPositionNotItsMatrix()
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

        if (Reflective(assets) is not { } material)
        {
            Assert.Ignore("no material on this map asks for the map's own cubemap");
            return;
        }

        (MapPlacedCubemap Near, MapPlacedCubemap Far) pair = MostUnalike(assets);

        // **The geometry does not move between these two draws.** It stands at `Near` both times,
        // and only the position handed to the cubemap lookup changes. That isolates the parameter:
        // any difference is the choice of cube and nothing else.
        (int R, int G, int B) fromMatrix = Draw(target, assets, material, pair.Near.Placement, null);

        (int R, int G, int B) fromOrigin = Draw(
            target, assets, material, pair.Near.Placement,
            (pair.Far.Placement.X, pair.Far.Placement.Y, pair.Far.Placement.Z));

        TestContext.Out.WriteLine(
            $"material {material}: cube from the matrix {fromMatrix}, cube from the supplied " +
            $"origin {fromOrigin}");

        // The control: a model that drew nothing would report the cleared background twice and the
        // inequality below would be measuring an absence.
        (fromMatrix.R + fromMatrix.G + fromMatrix.B).ShouldBeGreaterThan(
            0, "the model must be drawn before which cube it reflects can be measured");

        fromMatrix.ShouldNotBe(
            fromOrigin,
            "a supplied origin must decide which cubemap a model reflects, because a skinned " +
            "model's matrix carries no placement — its bones do, and the matrix reads (0,0,0)");
    }

    /// <summary>The two placements whose cubemaps differ most, so a wrong choice is visible.</summary>
    /// <remarks>
    /// **Chosen by measurement rather than by taking the first two.** Two cubemaps in the same
    /// corridor look alike, and a test that picked those would report "the choice does not matter"
    /// when the choice was simply invisible — the fourth of `CLAUDE.md`'s four, an effect below the
    /// resolution of the condition.
    /// </remarks>
    private static (MapPlacedCubemap Near, MapPlacedCubemap Far) MostUnalike(MapAssets assets)
    {
        MapPlacedCubemap first = assets.PlacedCubemaps[0];
        MapPlacedCubemap second = assets.PlacedCubemaps[0];
        float widest = -1f;

        foreach (MapPlacedCubemap a in assets.PlacedCubemaps)
        {
            foreach (MapPlacedCubemap b in assets.PlacedCubemaps)
            {
                float apart = Math.Abs(a.Placement.X - b.Placement.X) +
                    Math.Abs(a.Placement.Y - b.Placement.Y) +
                    Math.Abs(a.Placement.Z - b.Placement.Z);

                if (apart > widest)
                {
                    widest = apart;
                    first = a;
                    second = b;
                }
            }
        }

        return (first, second);
    }

    /// <summary>The DARKEST material asking for the map's own cubemap.</summary>
    /// <remarks>
    /// **Darkest, and it is a measurement decision rather than a preference** — the same one
    /// `ReflectionRenderTests.LocallyReflective` makes, for the same reason. Taking the first
    /// reflective material gave material 222, which read `(255, 255, 255)` both ways: clipped, where
    /// two entirely different cubes are indistinguishable however wrong the choice is. A dark base
    /// texture leaves the reflection somewhere to show.
    /// </remarks>
    private static int? Reflective(MapAssets assets)
    {
        int? darkest = null;
        double dimmest = double.MaxValue;

        for (int index = 0; index < assets.LocalReflections.Count; index++)
        {
            if (assets.LocalReflections[index] is null ||
                index >= assets.Textures.Count ||
                assets.Textures[index] is not { } texture)
            {
                continue;
            }

            ReadOnlySpan<byte> pixels = texture.Image.ToRgba(texture.Width, texture.Height);
            double total = 0;
            long texels = 0;

            for (int at = 0; at + 3 < pixels.Length; at += 4)
            {
                total += pixels[at] + pixels[at + 1] + pixels[at + 2];
                texels++;
            }

            double brightness = texels == 0 ? double.MaxValue : total / (texels * 3);

            if (brightness < dimmest)
            {
                dimmest = brightness;
                darkest = index;
            }
        }

        return darkest;
    }

    private static MapAssets? Assets =>
        Tf2Install.Folder is { } tf && File.Exists(Path.Combine(tf, "maps", "cp_process_final.bsp"))
            ? MapCache.Load()
            : null;

    /// <summary>Draws a quad standing at one placement, choosing its cube from another.</summary>
    private static (int R, int G, int B) Draw(
        OffscreenTarget target,
        MapAssets assets,
        int material,
        BspCubemap at,
        (float X, float Y, float Z)? origin)
    {
        // Facing up, so the reflection samples the cube's upper hemisphere — where two cubemaps in
        // different parts of a map differ most, one seeing sky and another a ceiling.
        (float X, float Y, float Z) normal = (0f, 0f, 1f);

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

        float[] model =
        [
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            at.X, at.Y, at.Z, 1f,
        ];

        float[] camera = new FreeCamera
        {
            Origin = (at.X, at.Y - 300f, at.Z),
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

            // **A mid ambient rather than none, and the reason is saturation.** With no cube the
            // shader takes its full-brightness path and this quad read `(255, 255, 255)` both ways —
            // clipped, where two different cubes would look identical however wrong the choice was.
            // A clipped measurement is an effect above the instrument's resolution, which fails the
            // same way as one below it. Half brightness puts the surface in range so the reflection
            // has somewhere to show.
            light: new AmbientCube(
                (0.5f, 0.5f, 0.5f), (0.5f, 0.5f, 0.5f), (0.5f, 0.5f, 0.5f),
                (0.5f, 0.5f, 0.5f), (0.5f, 0.5f, 0.5f), (0.5f, 0.5f, 0.5f)),
            bothSides: true,
            origin: origin);

        return target.PixelAt(32, 32);
    }
}
