using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Each of mat_fullbright's three states changes the picture, and changes it differently.
/// </summary>
/// <remarks>
/// **The conformance test says what the engine does; this one says our pixels do it.** That pair is
/// the standing rule here — a component test proves a value was computed, and says nothing about
/// whether production reads it. Three no-ops shipped in one session behind exactly that gap.
///
/// **Both non-zero states have to be measured, and against each other rather than only against
/// normal.** A single "fullbright differs from normal" assertion passes if BOTH modes do the same
/// wrong thing, and the likeliest wrong thing is implementing 2 as 1 — which is what happens when
/// the cvar is read as a boolean.
/// </remarks>
public sealed class FullbrightRenderTests
{
    private static MapAssets? Assets
    {
        get
        {
            string tf = GameInstall.Root ?? string.Empty;
            string map = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tf2DemoSalvage", "maps", "cp_process_f12.bsp");

            return !Directory.Exists(tf) || !File.Exists(map)
                ? null
                : MapAssets.Load(
                    File.ReadAllBytes(map), GameArchives.Open(tf), maximumTextureSize: 256);
        }
    }

    [Test]
    public void Fullbright_ItsThreeStates_ProduceThreeDifferentPictures()
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

        (int Red, int Green, int Blue) Draw(Fullbright mode)
        {
            (List<WorldVertex> wall, WorldBatch batch) = Quad(0.9f, material: 0, colour: (1f, 1f, 1f));

            target.Clear(0f, 0f, 0f);
            target.DrawWorld(wall, [batch], Identity, assets, fullbright: mode);

            return target.PixelAt(32, 32);
        }

        (int Red, int Green, int Blue) normal = Draw(Fullbright.Off);
        (int Red, int Green, int Blue) unlit = Draw(Fullbright.NoLighting);
        (int Red, int Green, int Blue) lighting = Draw(Fullbright.LightingOnly);

        TestContext.Out.WriteLine(
            $"FULLBRIGHT normal {normal} / no-lighting {unlit} / lighting-only {lighting}");

        // **1 removes the lighting**, so a surface can only get brighter — the lightmap is replaced
        // by a fully-lit one and a lightmap never exceeds full.
        (unlit.Red + unlit.Green + unlit.Blue).ShouldBeGreaterThan(
            normal.Red + normal.Green + normal.Blue,
            "mat_fullbright 1 replaces the lightmap with a fully-lit one, so nothing can darken");

        // **2 removes the albedo**, which is a different substitution and must not land on the same
        // pixel. This is the assertion that fails if the mode was implemented as a boolean.
        lighting.ShouldNotBe(
            unlit, "lighting-only and no-lighting are different substitutions, not one flag");

        lighting.ShouldNotBe(normal, "lighting-only replaces the albedo with grey");
    }

    private static (List<WorldVertex> Vertices, WorldBatch Batch) Quad(
        float depth, int material, (float Red, float Green, float Blue) colour)
    {
        (float r, float g, float b) = colour;

        // **The lightmap coordinate is the CONDITION, and (0,0) is the wrong one.** That corner of
        // the atlas is the reserved white texel props use when they have no lightmap, so a quad
        // sampling it is already fully lit — and replacing its lightmap with a fully-lit one then
        // changes nothing at all. Measured: normal and mat_fullbright 1 produced the identical
        // pixel, (255,255,8), and the test could not fail however broken the mode was.
        //
        // Half way into the atlas lands on a real face's baked lighting, which is darker. That is
        // an input where correct and broken differ, which is the property a test needs before its
        // assertion matters.
        const float lit = 0.5f;

        List<WorldVertex> vertices =
        [
            new(-1f, -1f, depth, 0f, 0f, lit, lit, 0f, r, g, b),
            new(1f, 1f, depth, 1f, 1f, lit, lit, 0f, r, g, b),
            new(1f, -1f, depth, 1f, 0f, lit, lit, 0f, r, g, b),
            new(-1f, -1f, depth, 0f, 0f, lit, lit, 0f, r, g, b),
            new(-1f, 1f, depth, 0f, 1f, lit, lit, 0f, r, g, b),
            new(1f, 1f, depth, 1f, 1f, lit, lit, 0f, r, g, b),
        ];

        return (vertices, new WorldBatch(material, 0, vertices.Count));
    }

    private static float[] Identity =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];
}
