using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary><c>C_RopeKeyframe</c>'s think, draw and <c>BuildRope</c>, and the rope manager's two passes (`c_rope.cpp`, B477).</summary>
/// <remarks>
/// **One rope throughout, a viaduct cable's shape**: five nodes between ends at (0, 0, 100) and (100, 0, 100), length
/// 100 and slack 150 — so each spring rests at <c>( 100 + 150 − 100 ) / 4</c> = 37 — width 2, texture scale 1, both ends
/// locked, <c>ROPE_SIMULATE | ROPE_INITIAL_HANG | ROPE_NO_WIND</c>. The materials are the shipped <c>cable/cable</c>
/// (opaque <c>Cable</c>) and <c>cable/cable_back</c> (translucent), each 1 texel tall.
/// </remarks>
public sealed class EntityRopesConformanceTests
{
    private const string Solid = "cable/cable.vmt";

    private const string Back = "cable/cable_back.vmt";

    private const int StartHandle = (1 << 11) | 120;

    private const int EndHandle = (1 << 11) | 121;

    private static readonly Vector3 StartPoint = new(0f, 0f, 100f);

    private static readonly Vector3 EndPoint = new(100f, 0f, 100f);

    /// <remarks>
    /// **The ends are locked to their entities, and the middle has sagged.** <c>InitRopePhysics</c> lays the nodes on the
    /// line and simulates five seconds of gravity (`c_rope.cpp:1896-1909`); the constraints lock node 0 and the last
    /// node to the end points every spring pass (`:1004-1030`), so the first and last strip points are the ends exactly.
    /// With 148 units of rope across 100 — four springs of 37, the integer quotient — the middle hangs.
    /// </remarks>
    [Test]
    public void Build_AHangingCable_IsLockedAtItsEndsAndSagsBetween()
    {
        EntityRopes ropes = new();

        List<DetailSpriteVertex> solid = SolidPass(Build(ropes, Cable(), fakeAntialiasing: false));

        ropes.Drawn.ShouldBe(1);

        Vector3 start = StartOf(solid);
        Vector3 end = EndOf(solid);

        Vector3.Distance(start, StartPoint).ShouldBeLessThan(0.001f);
        Vector3.Distance(end, EndPoint).ShouldBeLessThan(0.001f);

        solid.Min(corner => corner.Z).ShouldBeLessThan(90f, "the middle hangs well below the ends");
    }

    /// <remarks>
    /// **Each pair of nodes gains <c>rope_subdiv</c> points** (`c_rope.cpp:1722-1759`): an <c>m_Subdiv</c> of 255 is the
    /// cvar's 2, so five nodes become 5 + 4 · 2 = 13 strip points and twelve quads. A <c>ROPE_BARBED</c> rope takes the
    /// three barbed points instead (`:2024-2028`), and a subdivision of 9 is clamped to <c>MAX_ROPE_SUBDIVS</c> − 1 = 7.
    /// </remarks>
    [TestCase(255, 0, 13)]
    [TestCase(2, 0, 13)]
    [TestCase(9, 0, 33)]
    [TestCase(2, EntityRopes.BarbedFlag, 17)]
    public void Build_ASubdivisionSetting_GivesItsNumberOfStripPoints(int subdiv, int extraFlags, int points)
    {
        EntityRopes ropes = new();
        SceneRope rope = Cable() with { Subdiv = subdiv, Flags = Cable().Flags | extraFlags };

        SolidPass(Build(ropes, rope, fakeAntialiasing: false)).Count.ShouldBe((points - 1) * 6);
    }

    /// <remarks>
    /// **The texture advances by <c>4 / m_TextureScale · ( length + slack − 100 ) / ( ( nodes − 1 ) · subdiv + 1 )</c>
    /// per strip point, divided by the material's height** (`c_rope.cpp:1765-1768`): 600 / 9 here — counted over nine
    /// points while the strip has thirteen, which is the engine's arithmetic and runs the texture past its end.
    /// </remarks>
    [Test]
    public void Build_TheTextureCoordinate_AdvancesByTheEnginesIncrement()
    {
        List<DetailSpriteVertex> solid = SolidPass(Build(new EntityRopes(), Cable(), fakeAntialiasing: false));

        float increment = 600f / 9f;

        solid[0].V.ShouldBe(0f);
        solid[2].V.ShouldBe(increment, 0.001f);
        solid[^1].V.ShouldBe(12f * increment, 0.01f);
    }

    /// <remarks>
    /// **No <c>ROPE_SIMULATE</c>, no rope** — <c>ShouldDraw</c> and <c>InitRopePhysics</c> both refuse
    /// (`c_rope.cpp:1480`, `:1870`). This is the last keyframe of every chain in the corpus.
    /// </remarks>
    [Test]
    public void Build_ARopeWithoutSimulate_DrawsNothing()
    {
        EntityRopes ropes = new();

        Build(ropes, Cable() with { Flags = EntityRopes.InitialHangFlag | EntityRopes.NoWindFlag }, fakeAntialiasing: true)
            .ShouldBeEmpty();

        (ropes.Drawn, ropes.Skipped).ShouldBe((0, 1));
    }

    /// <remarks>
    /// **Near, the fake anti-aliasing draws a translucent rope under a slightly narrower solid one** (`c_rope.cpp:
    /// 1771-1827`, `:719-761`). A hundred units in front of a 1000-pixel view, the 2-unit rope is 10 pixels wide: the
    /// back pass is its full width at alpha 0.5, and the solid pass is <c>2 − 100 · 1.4 / 1000</c> = 1.86 wide, its
    /// alpha <c>RemapVal( 1.86, 0.3, 1, 0, 1 )</c> clamped to one.
    /// </remarks>
    [Test]
    public void Build_ACableNearTheCamera_DrawsAHalfAlphaBackPassUnderANarrowerSolidOne()
    {
        IReadOnlyList<ParticleBatch> batches = Build(new EntityRopes(), Cable(), fakeAntialiasing: true, distance: 100f);

        batches.Count.ShouldBe(2);
        batches[0].Material.Blend.ShouldBe(SpriteBlend.Translucent, "the back pass is drawn first");

        DetailSpriteVertex back = batches[0].Corners[0];
        back.Alpha.ShouldBe(BeamSegDraw.Packed(0.5f));
        Width(batches[0].Corners).ShouldBe(2f, 0.001f);

        Width(batches[1].Corners).ShouldBe(1.86f, 0.001f);
        batches[1].Corners[0].Alpha.ShouldBe(1f);
    }

    /// <remarks>
    /// **Far, the back pass widens to <c>rope_smooth_minwidth</c> pixels and the solid one is skipped** — under 0.3
    /// pixels the back width is zero, and "if it's all going to be 0 alpha" the solid rope is not drawn
    /// (`c_rope.cpp:735-737`, `:1795-1800`). At 10,000 units the rope is a tenth of a pixel: the back pass is 6 units wide.
    /// </remarks>
    [Test]
    public void Build_ACableFarFromTheCamera_DrawsOnlyAMinimumWidthBackPass()
    {
        IReadOnlyList<ParticleBatch> batches = Build(new EntityRopes(), Cable(), fakeAntialiasing: true, distance: 10000f);

        batches.Count.ShouldBe(1);
        batches[0].Material.Blend.ShouldBe(SpriteBlend.Translucent);
        batches[0].Corners[0].Alpha.ShouldBe(BeamSegDraw.Packed(0.2f));
    }

    /// <remarks>
    /// **The node's light is the vertex colour**, read once at creation (`c_rope.cpp:1911`, `:2046-2075`), and the
    /// <c>Cable</c> shader multiplies its texture by it in linear space and writes sRGB — so a linear quarter is drawn as
    /// <c>0.25^(1/2.2)</c>.
    /// </remarks>
    [Test]
    public void Build_UnderAQuarterLight_ColoursTheStripByItsGamma()
    {
        List<DetailSpriteVertex> solid = SolidPass(
            Build(new EntityRopes(), Cable(), fakeAntialiasing: false, light: new Vector3(0.25f)));

        solid[0].Red.ShouldBe(BeamSegDraw.Packed(MathF.Pow(0.25f, 1f / 2.2f)));
    }

    /// <remarks>
    /// **An end whose entity cannot be found keeps its cached position, which starts at zero** — <c>GetEndPointPos</c>
    /// answers true whatever <c>CalculateEndPointAttachment</c> found (`c_rope.cpp:1969-1982`), so "Must have both
    /// entities to work" can never refuse, and the rope hangs to the world origin. The engine's, and kept.
    /// </remarks>
    [Test]
    public void Build_AnEndWithNoEntity_HangsToTheWorldOrigin()
    {
        List<DetailSpriteVertex> solid = SolidPass(
            Build(new EntityRopes(), Cable() with { EndPoint = (1 << 21) - 1 }, fakeAntialiasing: false));

        EndOf(solid).Length().ShouldBeLessThan(0.001f);
    }

    /// <remarks>
    /// **The sprite pass leaves a rope alone**: <c>C_RopeKeyframe::DrawModel</c> is the rope's whole draw, and its
    /// material path is only a carrier for the timeline.
    /// </remarks>
    [Test]
    public void SpriteBatches_ARopeProp_IsLeftToTheRopePass()
    {
        EntitySpriteBatches sprites = new();

        sprites.Build(
            [Prop(Cable())],
            eye: new Vector3(50f, -100f, 100f),
            viewRight: Vector3.UnitX,
            viewUp: Vector3.UnitZ,
            viewForward: Vector3.UnitY,
            sprites: Sprites(fakeAntialiasing: true),
            visible: _ => true);

        sprites.Drawn.ShouldBe(0);
    }

    private static SceneRope Cable() => new(
        StartPoint: StartHandle,
        EndPoint: EndHandle,
        StartAttachment: 0,
        EndAttachment: 0,
        Slack: 150,
        Length: 100,
        LockedPoints: EntityRopes.LockStartPoint | EntityRopes.LockEndPoint,
        Flags: EntityRopes.SimulateFlag | EntityRopes.InitialHangFlag | EntityRopes.NoWindFlag,
        Segments: 5,
        ConstrainBetweenEndpoints: false,
        Subdiv: 255,
        TextureScale: 1f,
        Width: 2f,
        ScrollSpeed: 0f);

    private static IReadOnlyList<ParticleBatch> Build(
        EntityRopes ropes, SceneRope rope, bool fakeAntialiasing, float distance = 100f, Vector3? light = null) =>
        ropes.Build(
            [Prop(rope)],
            new RopeView(new Vector3(50f, -distance, 100f), Vector3.UnitY, 1000f),
            Sprites(fakeAntialiasing),
            (handle, _, _) => handle switch
            {
                StartHandle => new RopeEndPoint(StartPoint, Vector3.UnitX),
                EndHandle => new RopeEndPoint(EndPoint, Vector3.UnitX),
                _ => null,
            },
            _ => light ?? Vector3.One,
            (_, _) => new RopeHit(1f, Vector3.Zero, 0f, false),
            frameTime: 0.015f);

    private static SceneProp Prop(SceneRope rope) => new(
        EntityIndex: 120,
        ModelPath: Solid,
        Kind: SceneModelKind.Sprite,
        Pose: new ScenePose { Rope = rope },
        ClassName: "CRopeKeyframe");

    private static Dictionary<string, EngineSprite> Sprites(bool fakeAntialiasing)
    {
        Dictionary<string, EngineSprite> sprites = new(StringComparer.OrdinalIgnoreCase)
        {
            [Solid] = CableMaterial(SpriteBlend.Opaque),
        };

        if (fakeAntialiasing)
        {
            sprites[Back] = CableMaterial(SpriteBlend.Translucent);
        }

        return sprites;
    }

    private static EngineSprite CableMaterial(SpriteBlend blend) => new(
        new ParticleMaterial(new MapTexture(1, 1, 1, 1, TextureImage.None, IsTransparent: false), [], blend),
        1,
        1,
        SpriteOrientation.Parallel,
        SpriteExtents.Of(1, 1, origin: null),
        (1f, 1f, 1f, 1f),
        IgnoresVertexColors: true,
        Shader: "Cable",
        Modulation: (1f, 1f, 1f, 1f),
        VertexColour: true,
        VertexAlpha: true);

    /// <summary>The opaque pass's corners — the solid rope.</summary>
    private static List<DetailSpriteVertex> SolidPass(IReadOnlyList<ParticleBatch> batches) =>
        [.. batches.Where(batch => batch.Material.Blend == SpriteBlend.Opaque).SelectMany(batch => batch.Corners)];

    /// <summary>The strip's width at its first point, across its two edges.</summary>
    private static float Width(IReadOnlyList<DetailSpriteVertex> corners) =>
        Vector3.Distance(At(corners[0]), At(corners[1]));

    private static Vector3 At(DetailSpriteVertex corner) => new(corner.X, corner.Y, corner.Z);

    /// <summary>The strip's first point: the first quad's <c>lastOne</c> and <c>lastTwo</c>.</summary>
    private static Vector3 StartOf(List<DetailSpriteVertex> corners) => (At(corners[0]) + At(corners[1])) / 2f;

    /// <summary>Its last point: the last quad's <c>nextOne</c> and <c>nextTwo</c>, its third and sixth corners.</summary>
    private static Vector3 EndOf(List<DetailSpriteVertex> corners) => (At(corners[^4]) + At(corners[^1])) / 2f;
}
