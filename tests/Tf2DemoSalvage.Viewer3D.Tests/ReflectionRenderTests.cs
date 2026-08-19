using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// The baked reflection, drawn on a real device and measured in pixels.
/// </summary>
/// <remarks>
/// **Everything else about this feature is falsifiable against map data; the shader is not.** The
/// lump reader, the name derivation, the face decode and the material parameters were each checked
/// against 43 real cubemaps. What none of that can say is whether the HLSL samples the cube, or
/// whether the constants reach it — and the failure there is a picture, which no assertion about a
/// byte can see.
///
/// This is the closest a test gets: render offscreen through the real pipeline and read the pixels
/// back. It cannot say the reflection looks RIGHT — that is a question for someone looking at the
/// screen, and this file does not pretend otherwise — but it can say the cube is being sampled at
/// all, which is the difference between a feature and a no-op.
///
/// **The discriminator is the surface normal.** A reflection vector is the view direction mirrored
/// about the normal, so two otherwise identical surfaces facing different ways sample different
/// texels of the cube and come out different colours. If the envmap branch never runs, the two are
/// identical — nothing else in this shader's world path varies with the normal.
/// </remarks>
public sealed class ReflectionRenderTests
{
    /// <summary>A perspective camera, because an identity matrix has no eye position.</summary>
    /// <remarks>
    /// **The existing offscreen tests use the identity matrix and therefore never reach this
    /// code.** Inverting the identity and taking its third row gives a w of zero — parallel rays,
    /// converging nowhere — so <c>EyePosition</c> correctly reports no camera and the shader
    /// correctly skips the reflection. A test written on that matrix would have measured nothing
    /// and passed.
    /// </remarks>
    private static float[] Camera =>
        new FreeCamera
        {
            Origin = (0f, -600f, 64f),
            Angles = (0f, 90f, 0f),
            Aspect = 1f,
        }.ToMatrix();

    [Test]
    public void AReflectiveSurfaceChangesColourWithItsNormal()
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
            Assert.Ignore("no material on this map carries a baked cubemap");
            return;
        }

        (int R, int G, int B) facingUp = Draw(target, assets, material, (0f, 0f, 1f));
        (int R, int G, int B) facingSide = Draw(target, assets, material, (0f, -1f, 0f));

        TestContext.Out.WriteLine(
            $"material {material}: normal up {facingUp}, normal side {facingSide}");

        // Drawn at all, or the comparison below is between two blacks.
        (facingUp.R + facingUp.G + facingUp.B).ShouldBeGreaterThan(
            0, "the surface must be drawn before its reflection can be measured");

        (facingUp.R + facingUp.G + facingUp.B).ShouldNotBe(
            facingSide.R + facingSide.G + facingSide.B,
            "a reflection follows the normal, so two normals must sample different texels");
    }

    [Test]
    public void ASurfaceWithNoCubemapDoesNotChangeWithItsNormal()
    {
        // **The control, and it is the half that makes the test above mean anything.** If some
        // other part of the shader varied with the normal, the difference measured there would not
        // be the reflection. A material with no cubemap must come out identical.
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

        if (Matte(assets) is not { } material)
        {
            Assert.Ignore("every material on this map carries a cubemap, so there is no control");
            return;
        }

        (int R, int G, int B) facingUp = Draw(target, assets, material, (0f, 0f, 1f));
        (int R, int G, int B) facingSide = Draw(target, assets, material, (0f, -1f, 0f));

        TestContext.Out.WriteLine(
            $"matte material {material}: normal up {facingUp}, normal side {facingSide}");

        facingUp.ShouldBe(
            facingSide, "nothing but the reflection varies with the normal on a world surface");
    }

    /// <summary>Draws a full-view quad of one material with one normal, and reads the centre.</summary>
    private static (int R, int G, int B) Draw(
        OffscreenTarget target,
        MapAssets assets,
        int material,
        (float X, float Y, float Z) normal)
    {
        // A quad standing in front of the camera, which looks along +Y from y = -600.
        const float Y = 0f;

        List<WorldVertex> vertices =
        [
            Vertex(-256f, Y, -256f, 0f, 0f, normal),
            Vertex(256f, Y, -256f, 1f, 0f, normal),
            Vertex(256f, Y, 256f, 1f, 1f, normal),
            Vertex(-256f, Y, -256f, 0f, 0f, normal),
            Vertex(256f, Y, 256f, 1f, 1f, normal),
            Vertex(-256f, Y, 256f, 0f, 1f, normal),
        ];

        target.Clear(0f, 0f, 0f);
        target.DrawWorld(vertices, [new WorldBatch(material, 0, vertices.Count)], Camera, assets);

        return target.PixelAt(32, 32);
    }

    private static WorldVertex Vertex(
        float x, float y, float z, float u, float v, (float X, float Y, float Z) normal) =>
        new(x, y, z, u, v, 0f, 0f, 0f)
        {
            NormalX = normal.X,
            NormalY = normal.Y,
            NormalZ = normal.Z,
        };

    /// <summary>The first material index carrying a baked cubemap, or null.</summary>
    private static int? Reflective(MapAssets assets) =>
        Enumerable.Range(0, assets.Cubemaps.Count)
            .Cast<int?>()
            .FirstOrDefault(index =>
                assets.Cubemaps[index!.Value] is not null &&
                assets.Textures[index.Value] is not null);

    /// <summary>The first material index with a texture and no cubemap, or null.</summary>
    private static int? Matte(MapAssets assets) =>
        Enumerable.Range(0, assets.Cubemaps.Count)
            .Cast<int?>()
            .FirstOrDefault(index =>
                assets.Cubemaps[index!.Value] is null &&
                assets.Textures[index.Value] is not null);

    /// <summary>Real map assets, because the shader clips on texture alpha.</summary>
    private static MapAssets? Assets
    {
        get
        {
            if (Tf2Install.Folder is not { } tf)
            {
                return null;
            }

            string map = Path.Combine(tf, "maps", "cp_process_final.bsp");

            if (!File.Exists(map))
            {
                return null;
            }

            return MapAssets.Load(
                File.ReadAllBytes(map), GameArchives.Open(tf), maximumTextureSize: 64);
        }
    }
}
