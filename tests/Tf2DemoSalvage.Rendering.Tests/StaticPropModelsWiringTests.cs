using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>Every static prop drawn as a model, on a real map (B426, D198).</summary>
/// <remarks>
/// `engine.dll` `0x1800f1bd0`: a prop with baked `.vhv` colours draws them with no cube and no local
/// lights, and one without draws lit per frame like any model — both through the model draw. So on a
/// real map every placement must reach <see cref="MapAssets.StaticModels"/>, the baked ones must carry
/// their colours, and the world's batches must hold no prop at all.
/// </remarks>
public sealed class StaticPropModelsWiringTests
{
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Load_TheReferenceMap_DrawsEveryPropAsAModelAndTheBakedOnesWithTheirColours()
    {
        MapCache.LoadedMap loaded = MapCache.With();
        MapAssets assets = loaded.Assets;

        // The loader's own counts, carried from where it produced them: placements, and how many had baked colours.
        string summary = loaded.Log.From("props").First(line => line.Contains("ASKED FOR", StringComparison.Ordinal));
        int placed = int.Parse(Regex.Match(summary, @"ASKED FOR (\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
        int baked = int.Parse(Regex.Match(summary, @"HAVE baked lighting for (\d+)").Groups[1].Value, CultureInfo.InvariantCulture);

        baked.ShouldBeGreaterThan(0, "the control: the reference map has baked placements");
        assets.StaticModels.Count.ShouldBe(placed, "every placement is a model draw");
        assets.StaticModelColours.Count.ShouldBe(baked, "every baked placement carries its colours, and only those");

        // **Through the production pose**: the set that draws them, handed what `LevelSystems` hands it.
        EntityModelSet models = new()
        {
            Geometry = assets.Geometry,
            StaticPropColours = assets.StaticModelColours,
        };

        models.Add(assets.StaticModels);

        List<ModelInstance> drawn = [];
        models.Instances(assets.StaticModels, drawn);

        // Every placement drawn — which needs the static props' own models loaded with the map, not only
        // the ones the demo precaches (B426).
        drawn.Count.ShouldBe(placed, "a static prop whose model the map did not load");

        List<ModelInstance> coloured = [.. drawn.Where(instance => assets.StaticModelColours.ContainsKey(instance.EntityIndex))];

        coloured.Count.ShouldBe(baked, "every baked placement is drawn");

        // **Except a model flagged bump mapped** (B514): a `$phong` or `$bumpmap` material takes the whole model off
        // its colours (`studiorender.dll FUN_180057da0`, `engine.dll FUN_1800eb1a0`).
        Dictionary<int, Core.Scene.SceneProp> byEntity = assets.StaticModels.ToDictionary(prop => prop.EntityIndex);
        bool Refused(ModelInstance instance) =>
            StaticPropVertexLighting.RefusesColourMesh(assets.Geometry(byEntity[instance.EntityIndex].ModelPath).ShouldNotBeNull().StudioFlags);

        List<ModelInstance> refused = [.. coloured.Where(Refused)];
        coloured = [.. coloured.Where(instance => !Refused(instance))];

        TestContext.Out.WriteLine($"REFERENCE MAP {refused.Count} of {baked} baked placements are bump mapped and draw no colour mesh");
        refused.Count.ShouldBeInRange(1, baked - 1, "the control: the map has both kinds");
        refused.Count(instance => instance.BakedColours is not null).ShouldBe(0, "a bump-mapped placement drawn with a colour mesh");
        coloured.Count(instance => instance.BakedColours is null)
            .ShouldBe(0, "a baked placement drawn without its colour mesh");
        coloured.Count(instance => instance.Light is not null || instance.Locals is { Count: > 0 })
            .ShouldBe(0, "a baked placement given a cube or lamps");
        drawn.Count(instance => !assets.StaticModelColours.ContainsKey(instance.EntityIndex) && instance.BakedColours is not null)
            .ShouldBe(0, "an unbaked placement drawn with colours");
    }

    /// <remarks>
    /// **The drawn colour term is the shader's decode of the map's own `.vhv` bytes** — `GammaToLinear( colour · 2 )`,
    /// `common_vs_fxc.h:870-874`. On the reference map, the first baked placement's `sp_hdr_N.vhv` is read straight from
    /// the pakfile and every value its production draw carries must be pow( 2b / 255, 2.2 ) of one of that file's bytes.
    /// The control: some drawn value exceeds one, which the old <c>min( 1, 2c )</c> could never produce.
    /// </remarks>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Instances_TheReferenceMapsFirstBakedProp_DrawItsVhvBytesThroughGammaToLinearOfTwice()
    {
        byte[] map = MapCache.Bytes();
        MapAssets assets = MapCache.With().Assets;

        EntityModelSet models = new() { Geometry = assets.Geometry, StaticPropColours = assets.StaticModelColours };
        models.Add(assets.StaticModels);
        List<ModelInstance> drawn = [];
        models.Instances(assets.StaticModels, drawn);

        // The brightest baked placement, so the over-one control has something to find.
        ModelInstance prop = drawn
            .Where(instance => instance.BakedColours is not null)
            .OrderByDescending(instance => instance.BakedColours!.Max())
            .First();

        int lumpIndex = prop.EntityIndex - PropModels.FirstStaticPropEntityIndex;
        string path = Content.Assets.StudioVertexLighting.PathsFor(lumpIndex, Content.Bsp.BspMapFlags.Read(map)).Single();
        byte[] file = Content.Assets.PakFile.ReadFrom(map).ReadFile(path).ShouldNotBeNull(path);
        int checksum = BitConverter.ToInt32(file, 4);

        HashSet<float> decoded = [];

        foreach (IReadOnlyList<(byte Red, byte Green, byte Blue)> mesh in Content.Assets.StudioVertexLighting.Read(file, checksum))
        {
            foreach ((byte red, byte green, byte blue) in mesh)
            {
                decoded.Add(MathF.Pow(red / 255f * 2f, 2.2f));
                decoded.Add(MathF.Pow(green / 255f * 2f, 2.2f));
                decoded.Add(MathF.Pow(blue / 255f * 2f, 2.2f));
            }
        }

        float[] colours = prop.BakedColours!;
        TestContext.Out.WriteLine($"{path}: {colours.Length} drawn values, max {colours.Max()}, {decoded.Count} decoded");

        colours.Where(value => !decoded.Contains(value)).ShouldBeEmpty(path);
        colours.Max().ShouldBeGreaterThan(1f, "the control: a bake brighter than half a byte decodes above one");
    }

    /// <remarks>
    /// **B424 corrected, on a real map.** On DX9 (`engine.dll` `0x1800f1bd0`, hardware config `+0x150` true) a baked
    /// prop keeps its colours and takes `FUN_1801ba590(handle, 6)`: its handle's styled lights (`FUN_1801b6bf0`) at their
    /// current values. Measured 2026-09-28 (`styledprops`): `koth_dryfield` carries one light on style 1, `world.cpp`'s
    /// FLICKER, reaching 56 placements. So with style 1 at 'n' some baked placements must reach the draw WITH their colours
    /// and a local light, and with style 1 at 'a' (value 0) none may carry one. The first version asserted the colours
    /// dropped — the non-DX9 branch.
    /// </remarks>
    [TestCase("n", true)]
    [TestCase("a", false)]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Instances_KothDryfieldWithStyleOne_KeepBakedColoursAndCarryTheLampWhileItIsLit(string pattern, bool on)
    {
        const string Map = "koth_dryfield";
        MapAssets assets = MapCache.With(mapName: Map).Assets;
        LevelLighting lighting = LevelLighting.From(MapLevel.Read(MapCache.Bytes(Map), NullLogger.Instance), NullLogger.Instance);

        LightStyleValues values = new();
        values.Set(1, pattern);
        values.Advance(0d);
        lighting.StyleScale = values.Scale;

        EntityModelSet models = new()
        {
            Geometry = assets.Geometry,
            StaticPropColours = assets.StaticModelColours,
            StaticPlusDynamicLights = lighting.StaticPlusDynamicLights,
        };

        models.Add(assets.StaticModels);

        List<ModelInstance> drawn = [];
        models.Instances(assets.StaticModels, drawn, lighting.ModelLightingAt, lighting.ModelSunAt);

        // A model flagged bump mapped never had the colour mesh to keep (B514); those are not this test's subject.
        Dictionary<int, Core.Scene.SceneProp> byEntity = assets.StaticModels.ToDictionary(prop => prop.EntityIndex);

        List<ModelInstance> baked =
        [
            .. drawn.Where(instance =>
                assets.StaticModelColours.ContainsKey(instance.EntityIndex) &&
                !StaticPropVertexLighting.RefusesColourMesh(assets.Geometry(byEntity[instance.EntityIndex].ModelPath).ShouldNotBeNull().StudioFlags)),
        ];

        baked.Count.ShouldBeGreaterThan(0, "the control: the map has baked placements that keep their colours");
        baked.Count(instance => instance.BakedColours is null).ShouldBe(0, "DX9 never drops the colour mesh");

        List<ModelInstance> lit = [.. baked.Where(instance => instance.Locals is { Count: > 0 })];

        if (on)
        {
            lit.Count.ShouldBeInRange(1, 56);
            lit.ShouldAllBe(instance => instance.Light == LevelLighting.NoStaticState, "flags 6 hold no static state");
        }
        else
        {
            lit.ShouldBeEmpty();
        }
    }

    /// <remarks>
    /// **B607, bit 4's cache on a real map.** `FUN_1801ba590` keeps a handle's ranked styled lights at `+0x88` until a
    /// listed style's stamp passes `+0xf8`. On `koth_dryfield`, whose only styled light is style 1's (`styledprops`,
    /// 2026-09-28), a second draw with nothing stamped ranks nothing, and switching style 1 to `a` ranks every handle that
    /// ranked before once more and takes the lamp off every placement.
    /// </remarks>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Instances_KothDryfieldDrawnTwiceThenStyleOneSwitchedOff_RanksOnlyOnTheStamp()
    {
        const string Map = "koth_dryfield";
        MapAssets assets = MapCache.With(mapName: Map).Assets;
        LevelLighting lighting = LevelLighting.From(MapLevel.Read(MapCache.Bytes(Map), NullLogger.Instance), NullLogger.Instance);

        LightStyleValues values = new();
        values.Set(1, "n");
        lighting.StyleScale = values.Scale;
        lighting.StylesChanged(values.Advance(0d));

        EntityModelSet models = new()
        {
            Geometry = assets.Geometry,
            StaticPropColours = assets.StaticModelColours,
            StaticPlusDynamicLights = lighting.StaticPlusDynamicLights,
        };

        models.Add(assets.StaticModels);

        List<ModelInstance> drawn = [];
        models.Instances(assets.StaticModels, drawn, lighting.ModelLightingAt, lighting.ModelSunAt);

        int lit = drawn.Count(instance => instance.BakedColours is not null && instance.Locals is { Count: > 0 });
        int ranked = lighting.StyledRankings;

        lit.ShouldBeGreaterThan(0, "the control: style 1's lamp lights baked placements");
        ranked.ShouldBeGreaterThanOrEqualTo(lit);

        drawn.Clear();
        models.Instances(assets.StaticModels, drawn, lighting.ModelLightingAt, lighting.ModelSunAt);
        lighting.StyledRankings.ShouldBe(ranked, "nothing stamped: every handle read +0x88");

        values.Set(1, "a");
        IReadOnlyList<int> changed = values.Advance(0d);
        changed.ShouldBe([1]);
        lighting.StylesChanged(changed);

        drawn.Clear();
        models.Instances(assets.StaticModels, drawn, lighting.ModelLightingAt, lighting.ModelSunAt);

        drawn.Count(instance => instance.BakedColours is not null && instance.Locals is { Count: > 0 }).ShouldBe(0);
        lighting.StyledRankings.ShouldBe(2 * ranked, "every handle that ranked lists style 1, and only style 1 changed");
    }

    /// <remarks>
    /// **B514, on the parity map.** `models/props_hydro/observatory_dome.mdl` draws
    /// `materials/models/props_hydro/observatory_dome.vmt`, a `VertexLitGeneric` with `$phong 1` and no `$bumpmap`.
    /// `studiorender.dll FUN_180057da0` flags the model `STUDIOHDR_FLAGS_USES_BUMPMAPPING` for the `$phong`, and
    /// `engine.dll FUN_1800eb1a0` then refuses it static lighting: its `.vhv` colours are never drawn, and it is lit by
    /// its handle's full state (`FUN_1801ba590` flags 7) — the leaf cube, its local lights and the sky light. The
    /// viewer drew the colours with no cube and no light for the phong term, at 17-82% of TF2. The census at the end is
    /// every placement on the map that takes the same path.
    /// </remarks>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Instances_CpProcessF12ObservatoryDome_DrawsNoBakedColoursAndTakesItsCubeItsLampAndTheSun()
    {
        const string Map = "cp_process_f12";

        if (!MapCache.Exists(Map))
        {
            Assert.Ignore("cp_process_f12 or the game is not installed");
            return;
        }

        MapAssets assets = MapCache.With(mapName: Map).Assets;
        MapLevel level = MapLevel.Read(MapCache.Bytes(Map), NullLogger.Instance);
        LevelLighting lighting = LevelLighting.From(level, NullLogger.Instance);

        EntityModelSet models = new()
        {
            Geometry = assets.Geometry,
            StaticPropColours = assets.StaticModelColours,
            StaticPlusDynamicLights = lighting.StaticPlusDynamicLights,
            StaticPropLighting = lighting.StaticPropLightingAt,
            StaticPropFullLighting = lighting.StaticPropFullLightingAt,
        };

        models.Add(assets.StaticModels);

        List<ModelInstance> drawn = [];
        models.Instances(assets.StaticModels, drawn, lighting.ModelLightingAt, lighting.ModelSunAt);

        Dictionary<int, Core.Scene.SceneProp> byEntity = assets.StaticModels.ToDictionary(prop => prop.EntityIndex);
        List<ModelInstance> domes =
            [.. drawn.Where(instance => byEntity[instance.EntityIndex].ModelPath.Contains("observatory_dome", StringComparison.OrdinalIgnoreCase))];

        domes.Count.ShouldBe(2, "the control: the map places the dome twice");

        foreach (ModelInstance dome in domes)
        {
            int flags = assets.Geometry(byEntity[dome.EntityIndex].ModelPath).ShouldNotBeNull().StudioFlags;

            (flags & Content.Assets.StudioModelFlags.UsesBumpmapping).ShouldNotBe(0, "its $phong material flags the model at load");
            assets.StaticModelColours.ContainsKey(dome.EntityIndex).ShouldBeTrue("the control: vrad baked colours for it, which the engine does not draw");
            Core.Scene.ScenePose at = byEntity[dome.EntityIndex].Pose;

            TestContext.Out.WriteLine(
                $"F12 DOME entity {dome.EntityIndex} at ({at.X:0} {at.Y:0} {at.Z:0}): sun {(dome.Sun is null ? "none" : "yes")}, lamps " +
                string.Join(" | ", (dome.Locals ?? []).Select(lamp => $"({lamp.X:0} {lamp.Y:0} {lamp.Z:0}) {lamp.Red:0} {lamp.Green:0} {lamp.Blue:0}")));

            dome.BakedColours.ShouldBeNull("refused its colour mesh");

            // **The sun** — the map's `emit_skylight`, as the lump states it (`prop-light`): travelling
            // (0.2113, -0.3660, -0.9063) at intensity (2.3125, 2.1162, 1.5030), warm. The dome's lighting point is
            // inside a brush light passes through, and the engine's `MASK_OPAQUE` trace reaches the sky from it
            // (`MapLevelSweepTests.StrikesSky_FromAPointInsideABrushThatIsNotOpaque_…`).
            SunLight sun = dome.Sun.ShouldNotBeNull("the sky light reaches its lighting point");

            sun.DirectionX.ShouldBe(0.2113f, 0.0005f);
            sun.DirectionY.ShouldBe(-0.3660f, 0.0005f);
            sun.DirectionZ.ShouldBe(-0.9063f, 0.0005f);
            sun.Red.ShouldBe(2.3125f, 0.0005f);
            sun.Green.ShouldBe(2.1162f, 0.0005f);
            sun.Blue.ShouldBe(1.5030f, 0.0005f);

            // And the `light` the mapper stood beside each dome: intensity 31,373, 290 units away.
            dome.Locals.ShouldNotBeNull().ShouldContain(
                lamp => lamp.Red > 30000f && MathF.Abs(lamp.X - at.X) < 400f && MathF.Abs(lamp.Y - at.Y) < 400f,
                "the lamp beside the dome is in its state too");
            // **And its cube is computed, not its leaf's** (`ComputeAmbientFromSphericalSamples`, `engine.dll`
            // `FUN_1801b74f0`): above the roofs, the upward face is the map's sky ambient light almost alone.
            AmbientCube cube = dome.Light.ShouldNotBeNull("lit by its handle's cube");
            (float X, float Y, float Z) point = models.IlluminationPoint(byEntity[dome.EntityIndex], at);
            AmbientCube leaf = lighting.ComputeLighting(point.X, point.Y, point.Z);
            BspWorldLight skyAmbient = level.WorldLights.First(light => light.Kind == WorldLightKind.SkyAmbient);

            TestContext.Out.WriteLine(
                $"F12 DOME cube up {cube.PositiveZ}, down {cube.NegativeZ}, +x {cube.PositiveX}; its leaf's up {leaf.PositiveZ}, down {leaf.NegativeZ}; " +
                $"sky ambient {skyAmbient.Intensity}");

            cube.ShouldNotBe(leaf, "the leaf's samples are what a moving model takes");
            cube.PositiveZ.Red.ShouldBe(skyAmbient.Intensity.Red, skyAmbient.Intensity.Red * 0.1f);
            cube.PositiveZ.Green.ShouldBe(skyAmbient.Intensity.Green, skyAmbient.Intensity.Green * 0.1f);
            cube.PositiveZ.Blue.ShouldBe(skyAmbient.Intensity.Blue, skyAmbient.Intensity.Blue * 0.1f);
        }

        // The census, for what else changes on screen: every placement whose model is flagged, by model.
        List<IGrouping<string, Core.Scene.SceneProp>> refused =
        [
            .. assets.StaticModels
                .Where(prop => assets.Geometry(prop.ModelPath) is { } frames && StaticPropVertexLighting.RefusesColourMesh(frames.StudioFlags))
                .GroupBy(prop => prop.ModelPath, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count()),
        ];

        TestContext.Out.WriteLine(
            $"F12 BUMP-MAPPED STATIC PROPS {refused.Sum(group => group.Count())} of {assets.StaticModels.Count} placements, {refused.Count} models");

        foreach (IGrouping<string, Core.Scene.SceneProp> group in refused)
        {
            TestContext.Out.WriteLine($"  {group.Count(),4}  {group.Key}");
        }

        drawn.Count(instance =>
                assets.Geometry(byEntity[instance.EntityIndex].ModelPath) is { } frames &&
                StaticPropVertexLighting.RefusesColourMesh(frames.StudioFlags) &&
                instance.BakedColours is not null)
            .ShouldBe(0, "a flagged placement drawn with a colour mesh");
    }

    /// <remarks>
    /// **B429, on a real map.** `FUN_1800eac60` (`engine.dll`) gives an unbaked placement a CPU-lit colour mesh
    /// (`FUN_1800f36e0` → `FUN_1800ee4a0`) when its model has `STUDIOHDR_FLAGS_STATIC_PROP`; one without stays lit per
    /// frame. `koth_harvest_final` has 8 unbaked placements of 652; the census of their models' flags is below, and every
    /// one must reach the draw on the path its flags choose.
    /// </remarks>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Instances_KothHarvestUnbakedPlacements_TakeACpuColourMeshExactlyWhenCompiledStatic()
    {
        const string Map = "koth_harvest_final";
        MapAssets assets = MapCache.With(mapName: Map).Assets;
        LevelLighting lighting = LevelLighting.From(MapLevel.Read(MapCache.Bytes(Map), NullLogger.Instance), NullLogger.Instance);

        EntityModelSet models = new()
        {
            Geometry = assets.Geometry,
            StaticPropColours = assets.StaticModelColours,
            StaticPropLighting = lighting.StaticPropLightingAt,
        };

        models.Add(assets.StaticModels);

        List<ModelInstance> drawn = [];
        models.Instances(assets.StaticModels, drawn, lighting.ModelLightingAt, lighting.ModelSunAt);

        List<ModelInstance> unbaked = [.. drawn.Where(instance => !assets.StaticModelColours.ContainsKey(instance.EntityIndex))];
        unbaked.Count.ShouldBe(8, "the control: koth_harvest_final's unbaked placements");

        // **They are the eight REFUSED `.vhv`s** (B436): `0x1800f4760` refuses each header, so `FUN_1800f36e0` bakes
        // them on the CPU exactly as if the file were absent — the refusal is counted AND the prop is CPU-lit.
        assets.RefusedPropLighting.Count.ShouldBe(8);
        assets.RefusedPropLighting.ShouldAllBe(line => line.Contains("CPU-baked instead"));
        unbaked.Select(instance => instance.EntityIndex - PropModels.FirstStaticPropEntityIndex).Order().ShouldBe(
            assets.RefusedPropLighting.Select(line => int.Parse(line.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture)).Order());

        Dictionary<int, Core.Scene.SceneProp> byEntity = assets.StaticModels.ToDictionary(prop => prop.EntityIndex);
        int compiledStatic = 0;
        int bumpMapped = 0;

        foreach (ModelInstance instance in unbaked)
        {
            string path = byEntity[instance.EntityIndex].ModelPath;
            int flags = assets.Geometry(path).ShouldNotBeNull().StudioFlags;

            // A model flagged bump mapped is refused a colour mesh of either kind (B514, `engine.dll FUN_1800eb1a0`).
            bool cpu = StaticPropVertexLighting.Lights(flags) && !StaticPropVertexLighting.RefusesColourMesh(flags);

            bumpMapped += StaticPropVertexLighting.RefusesColourMesh(flags) ? 1 : 0;
            TestContext.Out.WriteLine($"HARVEST UNBAKED {path} flags 0x{flags:X} cpu {cpu}");

            if (cpu)
            {
                compiledStatic++;
                instance.BakedColours.ShouldNotBeNull(path).Length.ShouldBeGreaterThan(0);
                (instance.Light is null && instance.Locals is not { Count: > 0 }).ShouldBeTrue(path);
            }
            else
            {
                instance.BakedColours.ShouldBeNull(path);
                instance.Light.ShouldNotBeNull(path);
            }
        }

        (compiledStatic + bumpMapped).ShouldBe(8, "measured 2026-09-28: all eight are box_cluster01/02 and tractor_tire001, flags 0x11 in the file");
        compiledStatic.ShouldBeGreaterThan(0, "the control: some are still CPU-lit");
    }

    /// <summary>A measurement, not a test (D38): per map, placements refused at the header and those with `.vhv` meshes `FUN_1800f1550` never writes (B436).</summary>
    [TestCase("koth_harvest_final")]
    [TestCase("cp_process_final")]
    [TestCase("cp_badlands")]
    [TestCase("koth_dryfield")]
    [Explicit]
    public void UnwrittenVhvMeshesDiagnostic(string map)
    {
        MapAssets assets = MapCache.With(mapName: map).Assets;
        assets.StaticModelColours.ShouldNotBeEmpty("the control: a baked map must have .vhv colours to count");
        int partial = assets.StaticModelColours.Values.Count(colours => Array.Exists(colours, float.IsNaN));

        TestContext.Out.WriteLine(
            $"{map}: {assets.StaticModels.Count} placements, {assets.StaticModelColours.Count} with a .vhv, " +
            $"{assets.RefusedPropLighting.Count} refused at the header, {partial} with unwritten meshes");
    }

    /// <remarks>
    /// **B427, on the map the loader actually builds.** `CStaticProp::Init` (`engine.dll` `0x1802052c0`)
    /// lights a `STATIC_PROP_USE_LIGHTING_ORIGIN` prop at the lump's `m_LightingOrigin`. Measured
    /// 2026-09-28: 161 of 234 shipped maps flag some props (4,542 of 354,469); `cp_process_final` flags
    /// none and `koth_harvest_final` flags 3 of 652, so the three must reach the draw carrying exactly
    /// their lump point and nothing else may carry one.
    /// </remarks>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Load_KothHarvest_CarriesEachFlaggedPropsLumpLightingOrigin()
    {
        const string Map = "koth_harvest_final";
        IReadOnlyList<Content.Bsp.BspStaticProp> lump = Content.Bsp.BspStaticProps.Read(MapCache.Bytes(Map));
        IReadOnlyList<Core.Scene.SceneProp> models = MapCache.With(mapName: Map).Assets.StaticModels;

        List<Core.Scene.SceneProp> lit = [.. models.Where(prop => prop.LightingOrigin is not null)];

        lit.Count.ShouldBe(3);

        foreach (Core.Scene.SceneProp prop in lit)
        {
            Content.Bsp.BspStaticProp placement = lump[prop.EntityIndex - PropModels.FirstStaticPropEntityIndex];
            placement.UsesLightingOrigin.ShouldBeTrue();
            prop.LightingOrigin.ShouldBe(placement.LightingOrigin);
        }
    }
}
