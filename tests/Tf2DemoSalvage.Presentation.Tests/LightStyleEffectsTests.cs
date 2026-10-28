using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Tests;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The light styles a frame sets and the lightmaps it rebuilds for them — `R_AnimateLight` (`engine.dll`
/// `0x1800d3ec0`) from the `lightstyles` table, then `R_BuildLightMap` for a face whose style changed — as they left the
/// view (D214).
/// </summary>
/// <remarks>
/// The demo is one this file wrote (D38, D213): style 0 is `m`, 1 is `a`, 2 is `z` and 3 alternates `az` every tenth of
/// a second. A style's value is its letter's distance from `a` times 22, and its scale that over 264: `m` is 1. The
/// atlas holds two one-luxel faces, one lit by style 0 alone and one by styles 0 and 3.
///
/// `LevelLighting.StylesChanged` is read through a static prop's handle (`StyledRankings`, B607). **Not here:** the faces
/// a dynamic light reaches, which need a map's nodes; `WorldDynamicLights` and `LightmapAtlas` have their own suites.
/// </remarks>
public sealed class LightStyleEffectsTests
{
    private static readonly MapLevel Empty = new(
        null, null, null, new Dictionary<int, string>(), null, null, null, [], [], [], [], null, new VertexNormals([], []));

    private static readonly DemoTimeline Timeline = DemoTimeline.Build(SyntheticEffects.DemoWithLightStyles(
        [new(1, SyntheticEffects.PlayerClass, SyntheticEffects.Standing(0f, 0f, 0f, 2))],
        [],
        [],
        ["m", "a", "z", "az"],
        []));

    /// <summary>What a map holds: two faces, a level with no lights, and a walk that counts how often it is asked for.</summary>
    private sealed class Map
    {
        public LightmapAtlas Atlas { get; } = LightmapAtlas.PackAll([Face(0), Face(0, 3)]);

        public LevelLighting Lighting { get; init; } = LevelLighting.Unlit(new RecordingLogger());

        public int Walked { get; private set; }

        private static BspFaceLighting Face(params byte[] styles) =>
            new(new BspLightmap(1, 1, new byte[] { 1, 2, 3, 255 }), [])
            {
                Styles = [.. Array.ConvertAll(styles, static style => new BspStyleLayer(style, [[40f, 0f, 0f]]))],
            };

        public LightStyleMap Data(bool lightmaps = true) => new(
            lightmaps ? Atlas : null,
            Lighting,
            Empty,
            () =>
            {
                Walked++;

                return DecalWorld.Empty;
            });
    }

    private static void Step(LightStyleEffects lights, Map map, FakeFrameDevice device, int tick) =>
        lights.StepOver(Timeline, map.Data(), tick, new DynamicLights(), [], device);

    [Test]
    public void StepOver_TheFirstFrame_SetsEveryStyleFromTheDemosTableAndRebuildsEveryStyledFace()
    {
        LightStyleEffects lights = new(new RecordingLogger());
        Map map = new();
        FakeFrameDevice device = new();

        Step(lights, map, device, 0);

        lights.Scale(0).ShouldBe(1f, "m: twelve letters past a, times 22, over 264");
        lights.Scale(1).ShouldBe(0f, "a is off");
        lights.Scale(2).ShouldBe(550f / 264f, "z: twenty-five letters past a");
        lights.Scale(3).ShouldBe(0f, "az at time zero is its first letter");
        lights.Scale(10).ShouldBe(256f / 264f, "a style the table gives no pattern is 256");

        (byte[] pixels, int width, AtlasRegion[] regions) = device.Lightmaps.ShouldHaveSingleItem();

        pixels.ShouldBeSameAs(map.Atlas.Pixels);
        width.ShouldBe(map.Atlas.Width);
        regions.Length.ShouldBe(2, "every style changed, so both faces were rebuilt");
    }

    [Test]
    public void StepOver_AStyleThatStepsToItsNextLetter_RebuildsOnlyTheFacesItLights()
    {
        LightStyleEffects lights = new(new RecordingLogger());
        Map map = new();
        FakeFrameDevice device = new();

        Step(lights, map, device, 0);

        // Tick 6 is 0.09 s, still the first tenth; tick 7 is 0.105 s, the second: style 3 goes from a to z.
        Step(lights, map, device, 6);
        device.Lightmaps[1].Regions.ShouldBeEmpty("nothing changed, and the upload is still made with nothing in it");

        Step(lights, map, device, 7);
        lights.Scale(3).ShouldBe(550f / 264f);
        device.Lightmaps[2].Regions.ShouldHaveSingleItem().ShouldBe(device.Lightmaps[0].Regions[1], "the face styles 0 and 3 light");
    }

    [Test]
    public void StepOver_WithNoLightmaps_SetsNothingAndUploadsNothing()
    {
        LightStyleEffects lights = new(new RecordingLogger());
        Map map = new();
        FakeFrameDevice device = new();

        lights.StepOver(Timeline, map.Data(lightmaps: false), 0, new DynamicLights(), [], device);

        device.Lightmaps.ShouldBeEmpty();
        lights.Scale(0).ShouldBe(0f, "the styles were never advanced");
        map.Walked.ShouldBe(0);
    }

    [Test]
    public void StepOver_TheLevelsLighting_ReadsItsStylesFromTheSameValues()
    {
        LightStyleEffects lights = new(new RecordingLogger());
        Map map = new();

        Step(lights, map, new FakeFrameDevice(), 7);

        Func<int, float> scale = map.Lighting.StyleScale.ShouldNotBeNull();

        scale(2).ShouldBe(550f / 264f);
        scale(3).ShouldBe(lights.Scale(3));
    }

    /// <summary>A lamp on style 3 twenty units above a static prop's lighting origin, and the level holding only it.</summary>
    private static Map WithAStyleThreeLamp() => new()
    {
        Lighting = new LevelLighting(
            null,
            [],
            [new BspWorldLight((0f, 0f, 120f), (400f, 400f, 400f), (0f, 0f, -1f), WorldLightKind.Point, QuadraticAttenuation: 1f) { Style = 3 }],
            null,
            new RecordingLogger()),
    };

    /// <remarks>
    /// **B607, the production ordering.** Ranked before the first frame, a handle's styled lights are at `Steady` (one);
    /// the first frame's `Advance` reports every style changed, the level answers to style 3, and `LightStyleEffects`
    /// stamps them — so the next draw ranks again, at the demo's `a`, which is off.
    /// </remarks>
    [Test]
    public void StepOver_TheFirstFrame_RanksAStaticPropsStyledLightsAgainAtTheDemosValue()
    {
        LightStyleEffects lights = new(new RecordingLogger());
        Map map = WithAStyleThreeLamp();

        map.Lighting.StaticPlusDynamicLights(1000, 0f, 0f, 100f).Count.ShouldBe(1, "the control: unscaled, the lamp lights it");

        Step(lights, map, new FakeFrameDevice(), 0);

        map.Lighting.StaticPlusDynamicLights(1000, 0f, 0f, 100f).ShouldBeEmpty("style 3 is a at time zero");
        map.Lighting.StyledRankings.ShouldBe(2);
    }

    /// <remarks>
    /// **B607, the per-style stamp reaching the handle** (`FUN_1801ba590` bit 4: `+0x88` stands until a listed style's stamp
    /// passes `+0xf8`). A frame in which style 3 holds its letter ranks nothing; the frame it steps ranks once, at its new
    /// value.
    /// </remarks>
    [Test]
    public void StepOver_AListedStyleHoldingThenStepping_RanksAStaticPropsStyledLightsOnlyWhenItSteps()
    {
        LightStyleEffects lights = new(new RecordingLogger());
        Map map = WithAStyleThreeLamp();
        FakeFrameDevice device = new();

        Step(lights, map, device, 0);
        map.Lighting.StaticPlusDynamicLights(1000, 0f, 0f, 100f).ShouldBeEmpty();

        Step(lights, map, device, 6);
        map.Lighting.StaticPlusDynamicLights(1000, 0f, 0f, 100f).ShouldBeEmpty();
        map.Lighting.StyledRankings.ShouldBe(1, "style 3 held its letter: +0x88 read");

        Step(lights, map, device, 7);
        map.Lighting.StaticPlusDynamicLights(1000, 0f, 0f, 100f).Count.ShouldBe(1, "z: the lamp is on");
        map.Lighting.StyledRankings.ShouldBe(2);
    }

    [Test]
    public void StepOver_OverManyFrames_AsksForTheMapsWalkOnce()
    {
        LightStyleEffects lights = new(new RecordingLogger());
        Map map = new();
        FakeFrameDevice device = new();

        Step(lights, map, device, 0);
        Step(lights, map, device, 1);

        map.Walked.ShouldBe(1, "the dynamic lights' walk is built with the first frame of a map");
    }

    [Test]
    public void ClearMap_AfterAMap_ForgetsItsStylesAndItsWalk()
    {
        LightStyleEffects lights = new(new RecordingLogger());
        Map map = new();
        FakeFrameDevice device = new();

        Step(lights, map, device, 0);
        lights.ClearMap();

        lights.Scale(0).ShouldBe(0f, "a new map's styles start unset");

        Step(lights, map, device, 0);

        map.Walked.ShouldBe(2);
        device.Lightmaps[1].Regions.Length.ShouldBe(2, "and every style counts as changed again");
    }
}
