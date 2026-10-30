using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Tests;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The legacy particles a frame runs and draws, as they left the view (D214): a `"Tracer"` dispatch's line
/// (`TracerCallback`, `CFXDiscreetLine`), a spark temp entity (`IEffects::Sparks`, `::MetalSparks`) and an impact's
/// debris (`ImpactCallback`'s `PerformCustomEffects`, tf_fx_impacts.cpp:51-134).
/// </summary>
/// <remarks>
/// The demo is one this file wrote (D38, D213). **What each effect looks like is Scene's and has its own suites**
/// (`DiscreetLineConformanceTests`, `ImpactEffectsConformanceTests`, `ImpactEffectRunnerTests`); asserted here is which
/// effects the presenter runs, with which seed and from where, by building the same effect beside it and requiring the
/// same quads.
/// </remarks>
public sealed class ImpactParticlesTests
{
    private const int Red = 2;
    private const int Blue = 3;
    private const int Shooter = 1;
    private const int Victim = 2;
    private const int Gun = 30;
    private const int Width = 1280;

    private const string DecalScript = """
        "TranslationData" { "-" "" "C" "Impact.Concrete" "M" "Impact.Metal" }
        "Impact.Concrete" { "decals/concrete/shot1" "1" }
        "Impact.Metal" { "decals/metal/shot1" "1" }
        """;

    private static readonly MapLevel Empty = new(
        null, null, null, new Dictionary<int, string>(), null, null, null, [], [], [], [], null, new VertexNormals([], []));

    private static readonly FreeCamera Camera = new() { Origin = (500f, -300f, 64f), Angles = (0f, 90f, 0f) };

    private static readonly Func<Vector3, Vector3, BspTrace> Trace =
        static (from, to) => Empty.TraceBrushOnly((from.X, from.Y, from.Z), (to.X, to.Y, to.Z), 0f);

    /// <summary>Every name is a material: the effects name theirs, and which they name is not this file's subject.</summary>
    private sealed class EveryMaterial : IReadOnlyDictionary<string, ParticleMaterial>
    {
        private static readonly ParticleMaterial Material = new(null, [], SpriteBlend.Additive);

        public IEnumerable<string> Keys => [];

        public IEnumerable<ParticleMaterial> Values => [];

        public int Count => 0;

        public ParticleMaterial this[string key] => Material;

        public bool ContainsKey(string key) => true;

        public bool TryGetValue(string key, [MaybeNullWhen(false)] out ParticleMaterial value)
        {
            value = Material;

            return true;
        }

        public IEnumerator<KeyValuePair<string, ParticleMaterial>> GetEnumerator() =>
            Enumerable.Empty<KeyValuePair<string, ParticleMaterial>>().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static SyntheticEffects.Effect Effect(int tick, string className, params (string Name, PropertyValue Value)[] values) =>
        new(tick, className, values.ToDictionary(static value => value.Name, static value => value.Value, StringComparer.Ordinal));

    private static DemoTimeline Timeline(params SyntheticEffects.Effect[] effects) =>
        DemoTimeline.Build(SyntheticEffects.Demo(
            [
                new(Shooter, SyntheticEffects.PlayerClass, SyntheticEffects.Standing(0f, 0f, 0f, Red)),
                new(Victim, SyntheticEffects.PlayerClass, SyntheticEffects.Standing(300f, 0f, 0f, Blue)),
            ],
            [string.Empty, "Tracer"],
            [],
            [],
            effects));

    private static SyntheticEffects.Effect TracerDispatch(int tick, float length = 1000f, int flags = 0) => Effect(
        tick,
        "CTEEffectDispatch",
        ("m_iEffectName", PropertyValue.FromInt(1)),
        ("m_vStart[2]", PropertyValue.FromFloat(64f)),
        ("m_vOrigin[0]", PropertyValue.FromFloat(length)),
        ("m_vOrigin[2]", PropertyValue.FromFloat(64f)),
        ("m_fFlags", PropertyValue.FromInt(flags)),
        ("entindex", PropertyValue.FromInt(Gun)),
        ("m_nAttachmentIndex", PropertyValue.FromInt(1)));

    /// <summary>What a map holds, and how often its walk was asked for.</summary>
    private sealed class Map
    {
        public int Walked { get; private set; }

        private static ImpactDecals Decals() =>
            new(
                DecalEmitters.Parse(Encoding.UTF8.GetBytes(DecalScript)),
                new DecalMaterials(
                    static path => path.StartsWith("materials/decals/concrete/", StringComparison.Ordinal)
                        ? Encoding.UTF8.GetBytes("\"LightmappedGeneric\" { \"$basetexture\" \"decals/any\" }")
                        : null,
                    static _ => (64, 64)),
                [new BspTexinfo(SurfaceProperties.None, 0), new BspTexinfo(SurfaceProperties.None, 1)],
                ['C', 'M']);

        public ImpactMap Data(IReadOnlyList<ShotImpact>? impacts = null, bool materials = true) => new(
            Empty,
            impacts ?? [],
            Decals(),
            new BspLightSamples(ReadOnlyMemory<byte>.Empty, []),
            LevelLighting.Unlit(new RecordingLogger()),
            static _ => null,
            materials ? new EveryMaterial() : null,
            () =>
            {
                Walked++;

                return DecalWorld.Empty;
            });
    }

    private static ImpactFrame Frame(DemoTimeline timeline, int tick, int width = Width) =>
        new(timeline, null, tick, new BulletScene(timeline, Empty, null, tick, true), Camera, width, static _ => 1f, static _ => DecalWorld.Empty);

    private static (ImpactParticles Impacts, FakePosedEntities Models) Presenter()
    {
        FakePosedEntities models = new();

        return (new ImpactParticles(models, new BulletStrikes(models, new RecordingLogger()), new RecordingLogger()), models);
    }

    /// <summary>The quads a tracer line from a start draws a number of ticks after it fired, as the presenter must draw them.</summary>
    private static List<DetailSpriteVertex> LineQuads(DemoTimeline timeline, Vector3 start, int ticksOld, int width = Width)
    {
        DiscreetLine line = DiscreetLines.Tracer(
            start, new Vector3(1000f, 0f, 64f), DiscreetLines.TracerSpeed, SeededDraw.For(SeededDraw.Of(0, 0, salt: 3))).ShouldNotBeNull();
        (float fx, float fy, float fz) = AngleVectors.Forward(Camera.Angles.Pitch, Camera.Angles.Yaw);
        List<DetailSpriteVertex> quads = [];

        DiscreetLines.Draw(
            line,
            (ticksOld + 1) * timeline.IntervalPerTick,
            new Vector3(Camera.Origin.X, Camera.Origin.Y, Camera.Origin.Z),
            new Vector3(fx, fy, fz),
            width * 0.5f,
            quads);

        quads.ShouldNotBeEmpty("the control: a line this old is on screen");

        return quads;
    }

    private static List<DetailSpriteVertex> Quads(IReadOnlyList<ParticleBatch> batches) => [.. batches.SelectMany(static batch => batch.Corners)];

    [Test]
    public void WithOver_ATracerDispatchOnItsOwnTick_DrawsItsLineOneTickOldFromItsStart()
    {
        DemoTimeline timeline = Timeline(TracerDispatch(100));
        int fired = timeline.Dispatches.All.ShouldHaveSingleItem().Tick;
        (ImpactParticles impacts, _) = Presenter();
        Map map = new();

        impacts.StepOver(Frame(timeline, fired), map.Data());

        Quads(impacts.WithOver([], Frame(timeline, fired), map.Data())).ShouldBe(LineQuads(timeline, new Vector3(0f, 0f, 64f), 0));
    }

    [Test]
    public void WithOver_ATracerLineThreeTicksOn_HasFlownThreeTicksFurther()
    {
        DemoTimeline timeline = Timeline(TracerDispatch(100));
        int fired = timeline.Dispatches.All[0].Tick;
        (ImpactParticles impacts, _) = Presenter();
        Map map = new();

        Quads(impacts.WithOver([], Frame(timeline, fired + 3), map.Data())).ShouldBe(LineQuads(timeline, new Vector3(0f, 0f, 64f), 3));
    }

    [Test]
    public void WithOver_ATracerLine_IsAsWideAsTheViewportMakesIt()
    {
        DemoTimeline timeline = Timeline(TracerDispatch(100));
        int fired = timeline.Dispatches.All[0].Tick;
        (ImpactParticles impacts, _) = Presenter();
        Map map = new();

        // Sixty-four pixels across: the line is under half a pixel wide at this depth, and is drawn half a pixel wide.
        List<DetailSpriteVertex> narrow = Quads(impacts.WithOver([], Frame(timeline, fired, width: 64), map.Data()));

        narrow.ShouldBe(LineQuads(timeline, new Vector3(0f, 0f, 64f), 0, width: 64));
        narrow.ShouldNotBe(LineQuads(timeline, new Vector3(0f, 0f, 64f), 0), "the control: half the screen's width is in the line's width");
    }

    [Test]
    public void WithOver_ATracerFromAnAttachment_WaitsForItsEntityThenStartsAtTheBarrelAndStaysThere()
    {
        DemoTimeline timeline = Timeline(TracerDispatch(100, flags: 2));
        int fired = timeline.Dispatches.All[0].Tick;
        (ImpactParticles impacts, FakePosedEntities models) = Presenter();
        Map map = new();

        impacts.WithOver([], Frame(timeline, fired), map.Data()).ShouldBeEmpty("TRACER_FLAG_USEATTACHMENT and nobody posed: asked again next frame");

        models.Numbered[(Gun, 1)] = Content.Assets.ParticleControlPoint.Unoriented(new Vector3(0f, 40f, 64f));
        Quads(impacts.WithOver([], Frame(timeline, fired), map.Data())).ShouldBe(LineQuads(timeline, new Vector3(0f, 40f, 64f), 0));

        // The gun turns; the line flies the path it was given.
        models.Numbered[(Gun, 1)] = Content.Assets.ParticleControlPoint.Unoriented(new Vector3(0f, -90f, 64f));
        Quads(impacts.WithOver([], Frame(timeline, fired + 1), map.Data())).ShouldBe(LineQuads(timeline, new Vector3(0f, 40f, 64f), 1));
    }

    [Test]
    public void WithOver_ATracerFromAnAttachmentItsPosedEntityLacks_StartsAtTheDispatchsOwnStart()
    {
        DemoTimeline timeline = Timeline(TracerDispatch(100, flags: 2));
        int fired = timeline.Dispatches.All[0].Tick;
        (ImpactParticles impacts, FakePosedEntities models) = Presenter();
        Map map = new();

        models.Posed.Add(Gun);

        Quads(impacts.WithOver([], Frame(timeline, fired), map.Data())).ShouldBe(LineQuads(timeline, new Vector3(0f, 0f, 64f), 0));
    }

    [Test]
    public void WithOver_ATracerPastItsWindow_IsNotDrawn()
    {
        DemoTimeline timeline = Timeline(TracerDispatch(100, length: 60000f));
        int fired = timeline.Dispatches.All[0].Tick;
        Map map = new();

        // A line 60,000 units long is still in flight at 121 ticks; only the window stops it.
        Presenter().Impacts.WithOver([], Frame(timeline, fired + 120), map.Data()).ShouldNotBeEmpty();
        Presenter().Impacts.WithOver([], Frame(timeline, fired + 121), map.Data()).ShouldBeEmpty();
    }

    [Test]
    public void WithOver_WithNoParticleMaterials_HandsBackTheBatchesItWasGiven()
    {
        DemoTimeline timeline = Timeline(TracerDispatch(100));
        (ImpactParticles impacts, _) = Presenter();
        Map map = new();
        ParticleBatch[] given = [new([], default)];

        impacts.WithOver(given, Frame(timeline, timeline.Dispatches.All[0].Tick), map.Data(materials: false)).ShouldBeSameAs(given);
    }

    [Test]
    public void WithOver_WithNothingRunning_HandsBackTheBatchesItWasGiven()
    {
        (ImpactParticles impacts, _) = Presenter();
        Map map = new();
        ParticleBatch[] given = [new([], default)];

        impacts.WithOver(given, Frame(Timeline(), 100), map.Data()).ShouldBeSameAs(given);
    }

    [Test]
    public void WithOver_AfterTheBatchesItWasGiven_AddsItsOwn()
    {
        DemoTimeline timeline = Timeline(TracerDispatch(100));
        (ImpactParticles impacts, _) = Presenter();
        Map map = new();
        ParticleBatch given = new([new DetailSpriteVertex(9f, 9f, 9f, 0f, 0f, 1f, 1f, 1f, 1f, 0f, 0f, 0f)], default);

        IReadOnlyList<ParticleBatch> all = impacts.WithOver([given], Frame(timeline, timeline.Dispatches.All[0].Tick), map.Data());

        all.Count.ShouldBe(2);
        all[0].ShouldBe(given, "drawn first, as it was built first");
    }

    /// <remarks>
    /// **The impact batches are rebuilt every frame and must cost no garbage doing it** (B599): the quads went in through
    /// `AddRange([…])`, an array and a wrapper per quad, beside a list, an array and a closure per frame. Both spark kinds
    /// and a tracer line are running, so every quad builder is on the path. Warmed once (first-use lists and the JIT),
    /// then built again for the same frame, which must hand back the same quads and allocate nothing. The render log has
    /// Debug off, as the viewer runs: the per-batch Debug line formats a string by design.
    /// </remarks>
    [Test]
    public void WithOver_SparksAndATracerBuiltAgain_AllocatesNothingAndDrawsTheSame()
    {
        DemoTimeline timeline = Timeline(Spark(100), Spark(100, metal: true), TracerDispatch(100));
        int tick = timeline.Sparks.All[0].Tick + 3;
        FakePosedEntities models = new();
        ImpactParticles impacts = new(models, new BulletStrikes(models, new RecordingLogger()), NullLogger.Instance);
        Map map = new();
        ParticleBatch given = new([new DetailSpriteVertex(9f, 9f, 9f, 0f, 0f, 1f, 1f, 1f, 1f, 0f, 0f, 0f)], default);
        IReadOnlyList<ParticleBatch> batches = [given];

        impacts.StepOver(Frame(timeline, tick), map.Data());
        ImpactMap? data = map.Data();
        ImpactFrame frame = Frame(timeline, tick);
        List<DetailSpriteVertex> first = Quads(impacts.WithOver(batches, frame, data));

        long before = GC.GetAllocatedBytesForCurrentThread();
        IReadOnlyList<ParticleBatch> again = impacts.WithOver(batches, frame, data);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        first.Count.ShouldBeGreaterThan(6 * 3, "the control: sparks of both kinds and the tracer are on screen");
        again[0].ShouldBe(given);
        Quads(again).ShouldBe(first);
        bytes.ShouldBe(0L);
    }

    private static SyntheticEffects.Effect Spark(int tick, bool metal = false) => metal
        ? Effect(tick, "CTEMetalSparks", ("m_vecPos", PropertyValue.FromVector(10f, 20f, 100f)), ("m_vecDir", PropertyValue.FromVector(0f, 0f, 1f)))
        : Effect(
            tick,
            "CTESparks",
            ("m_vecOrigin[0]", PropertyValue.FromFloat(10f)), ("m_vecOrigin[1]", PropertyValue.FromFloat(20f)),
            ("m_vecOrigin[2]", PropertyValue.FromFloat(100f)), ("m_nMagnitude", PropertyValue.FromInt(2)),
            ("m_nTrailLength", PropertyValue.FromInt(1)), ("m_vecDir", PropertyValue.FromVector(0f, 0f, 1f)));

    /// <summary>The quads one spark's effect draws at a tick, built beside the presenter with its seed.</summary>
    private static List<DetailSpriteVertex> SparkQuads(DemoTimeline timeline, int tick, bool metal)
    {
        SceneSpark spark = timeline.Sparks.All.ShouldHaveSingleItem();
        Vector3 at = new(10f, 20f, 100f);
        ImpactEffectRunner twin = new();

        twin.Advance(
            [(0, spark.Tick, SeededDraw.Of(0, 0, salt: 4))],
            tick,
            timeline.IntervalPerTick,
            (_, random) => metal
                ? ImpactEffects.MetalSparks(at, Vector3.UnitZ, random)
                : ImpactEffects.Sparks(at, 2, 1, Vector3.UnitZ, Trace, random),
            Trace);

        (float fx, float fy, float fz) = AngleVectors.Forward(Camera.Angles.Pitch, Camera.Angles.Yaw);
        (float rx, float ry, float rz) = AngleVectors.Right(Camera.Angles.Pitch, Camera.Angles.Yaw, Camera.Angles.Roll);
        (float ux, float uy, float uz) = AngleVectors.Up(Camera.Angles.Pitch, Camera.Angles.Yaw, Camera.Angles.Roll);
        Dictionary<string, List<DetailSpriteVertex>> corners = new(StringComparer.OrdinalIgnoreCase);

        twin.Build(
            new Vector3(Camera.Origin.X, Camera.Origin.Y, Camera.Origin.Z),
            new Vector3(fx, fy, fz),
            new Vector3(rx, ry, rz),
            new Vector3(ux, uy, uz),
            static _ => 1f,
            corners);

        List<DetailSpriteVertex> quads = [.. corners.Values.SelectMany(static list => list)];

        quads.ShouldNotBeEmpty("the control: the effect is on screen at this tick");

        return quads;
    }

    [TestCase(false, TestName = "StepOver_AnElectricSpark_RunsSparksWithItsMagnitudeAndTrailFromItsIndexsSeed")]
    [TestCase(true, TestName = "StepOver_AMetalSpark_RunsMetalSparksFromItsIndexsSeed")]
    public void StepOver_ASparkTempEntity_RunsItsEffectAndDrawsItsQuads(bool metal)
    {
        DemoTimeline timeline = Timeline(Spark(100, metal));
        int tick = timeline.Sparks.All[0].Tick + 3;
        (ImpactParticles impacts, _) = Presenter();
        Map map = new();

        impacts.StepOver(Frame(timeline, tick), map.Data());

        impacts.Running.ShouldBe((0, 1));
        Quads(impacts.WithOver([], Frame(timeline, tick), map.Data())).ShouldBe(SparkQuads(timeline, tick, metal));
    }

    [TestCase(660, 1, TestName = "StepOver_ASparkTenSecondsOld_IsStillOffered")]
    [TestCase(661, 0, TestName = "StepOver_ASparkPastItsWindow_IsNotOffered")]
    public void StepOver_AroundASparksWindow_OffersItForTenSeconds(int after, int expected)
    {
        DemoTimeline timeline = Timeline(Spark(100));
        (ImpactParticles impacts, _) = Presenter();

        impacts.StepOver(Frame(timeline, timeline.Sparks.All[0].Tick + after), new Map().Data());

        impacts.Running.Sparks.ShouldBe(expected);
    }

    /// <summary>A bullet along +x, two hundred units to the side of the victim unless a test puts it on him.</summary>
    private static ShotImpact Bullet(int tick, int texinfo = 0, float y = 200f) =>
        new(0, 0, tick, Shooter, Red, (0f, y, 40f), (500f, y, 40f), (1000f, y, 40f), texinfo, Normal: (-1f, 0f, 0f));

    /// <summary>
    /// What a bullet fired at tick 100 has on screen two ticks on. The runner's count is no reading of this: it counts a
    /// bullet whose effect was refused as well as one that runs.
    /// </summary>
    private static List<DetailSpriteVertex> DrawnTwoTicksOn(ImpactParticles impacts, ShotImpact bullet)
    {
        DemoTimeline timeline = Timeline();
        Map map = new();

        // From its own tick, which is when a player in its path is judged.
        for (int tick = 100; tick <= 102; tick++)
        {
            impacts.StepOver(Frame(timeline, tick), map.Data([bullet]));
        }

        return Quads(impacts.WithOver([], Frame(timeline, 102), map.Data([bullet])));
    }

    [Test]
    public void StepOver_ABulletTheWorldStopped_RunsItsImpactEffect()
    {
        (ImpactParticles impacts, _) = Presenter();

        DrawnTwoTicksOn(impacts, Bullet(100)).ShouldNotBeEmpty("concrete: PerformCustomEffects' dust and flecks");
        impacts.Running.ShouldBe((1, 0));
    }

    [Test]
    public void StepOver_ABulletAPlayerStopped_HasNoImpactEffectOnTheWorldBehindHim()
    {
        (ImpactParticles impacts, FakePosedEntities models) = Presenter();

        // The victim stands on the bullet's line, and his hitboxes stop it.
        models.Posed.Add(Victim);
        models.Fractions[Victim] = 0.29f;

        DrawnTwoTicksOn(impacts, Bullet(100, y: 0f)).ShouldBeEmpty("only where Impact returned true: a bullet with a decal on the world");
    }

    [Test]
    public void StepOver_ABulletIntoWater_HasNoImpactEffect()
    {
        (ImpactParticles impacts, _) = Presenter();

        DrawnTwoTicksOn(impacts, Bullet(100) with { WaterEntry = (250f, 200f, 40f) }).ShouldBeEmpty();
    }

    [Test]
    public void StepOver_ABulletIntoASurfaceThatTakesNoDecal_HasNoImpactEffect()
    {
        (ImpactParticles impacts, _) = Presenter();

        // Metal, whose decal has no material here; its sparks would draw were they asked for.
        DrawnTwoTicksOn(impacts, Bullet(100, texinfo: 1)).ShouldBeEmpty("no decal, so Impact returned false");
    }

    [TestCase(200, 1, TestName = "StepOver_AnImpactTwoHundredTicksOld_IsStillOffered")]
    [TestCase(201, 0, TestName = "StepOver_AnImpactPastItsWindow_IsNotOffered")]
    public void StepOver_AroundAnImpactsWindow_OffersItForTwoHundredTicks(int after, int expected)
    {
        (ImpactParticles impacts, _) = Presenter();

        impacts.StepOver(Frame(Timeline(), 100 + after), new Map().Data([Bullet(100)]));

        impacts.Running.Impacts.ShouldBe(expected);
    }

    [Test]
    public void ClearMap_WithEffectsRunning_ForgetsThemAndTheTracerLines()
    {
        DemoTimeline timeline = Timeline(TracerDispatch(100, flags: 2), Spark(100));
        int fired = timeline.Dispatches.All[0].Tick;
        (ImpactParticles impacts, FakePosedEntities models) = Presenter();
        Map map = new();

        models.Numbered[(Gun, 1)] = Content.Assets.ParticleControlPoint.Unoriented(new Vector3(0f, 40f, 64f));
        impacts.StepOver(Frame(timeline, fired), map.Data([Bullet(fired)]));
        impacts.WithOver([], Frame(timeline, fired), map.Data());
        impacts.Running.ShouldBe((1, 1));

        impacts.ClearMap();

        impacts.Running.ShouldBe((0, 0));

        // The line's start was fixed at the barrel; forgotten, it is read again where the barrel is now.
        models.Numbered[(Gun, 1)] = Content.Assets.ParticleControlPoint.Unoriented(new Vector3(0f, -90f, 64f));
        Quads(impacts.WithOver([], Frame(timeline, fired), map.Data())).ShouldBe(LineQuads(timeline, new Vector3(0f, -90f, 64f), 0));
    }
}
