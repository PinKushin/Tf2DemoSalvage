using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary><c>CSpriteTrail</c>'s <c>UpdateTrail</c> and <c>DrawModel</c>, as `SpriteTrail.cpp:379-531` writes them (B475).</summary>
/// <remarks>
/// **One geometry throughout, chosen so every answer is a round number.** The head moves along +X on the ground and
/// the camera looks down from (50, 0, 500), so each strip's side vector is exactly −Y and a segment's corners sit at
/// y = ∓width/2. The material is <c>UnlitGeneric</c> with <c>$vertexcolor</c> and <c>$vertexalpha</c> and a white
/// modulation, the shape of every baseball and arrow trail, so a corner's colour is exactly the vertex colour
/// <see cref="BeamSegDraw"/> packed. Times are quarters of a second, exact in a float.
/// </remarks>
public sealed class EntityTrailsConformanceTests
{
    private const string Material = "effects/baseballtrail_red.vmt";

    private static readonly Vector3 Camera = new(50f, 0f, 500f);

    /// <remarks>
    /// **A point per frame the head moved, then the head itself.** Frame one, at t = 10, samples (0, 0, 0); frame two,
    /// at 10.25, samples (100, 0, 0) — so <c>DrawModel</c> strings three segments: the two points and the current head
    /// (`:443-449`, `:459-462`). The oldest has 0.75 of its one-second life left, so its alpha is 0.75
    /// (<c>m_flAlpha = brightness / 255 · flAlphaFade</c>, `:490`); the texture coordinate runs at
    /// <c>m_flTextureRes</c> per unit, 0 then 1 then 1 (`:404`, `:448`); and with <c>m_flEndWidth</c> −1 every width
    /// is the start width, ±5 for a width of ten (`:498-505`).
    /// </remarks>
    [Test]
    public void Build_AHeadThatMovedOnce_DrawsTwoPointsAndTheHead()
    {
        EntityTrails trails = new();

        Frame(trails, 10f, 0f);
        IReadOnlyList<ParticleBatch> batches = Frame(trails, 10.25f, 100f);

        List<DetailSpriteVertex> corners = Corners(batches);

        corners.Count.ShouldBe(12, "three segments, two quads");
        trails.Drawn.ShouldBe(1);

        // lastOne, lastTwo, nextOne / nextOne, lastTwo, nextTwo — the first quad's first pair is the oldest point.
        At(corners[0]).ShouldBe(new Vector3(0f, -5f, 0f));
        At(corners[1]).ShouldBe(new Vector3(0f, 5f, 0f));
        corners[0].Alpha.ShouldBe(BeamSegDraw.Packed(0.75f));
        corners[2].Alpha.ShouldBe(1f, "the newest point is a full life from death");

        (corners[0].V, corners[2].V, corners[11].V).ShouldBe((0f, 1f, 1f));
    }

    /// <remarks>
    /// **A non-negative end width is lerped by life**: <c>Lerp( flLifePerc, m_flEndWidth, m_flStartWidth )</c>
    /// (`:500`). The oldest point at 0.75 of its life, from 10 down to 2, is 2 + 8 · 0.75 = 8 wide — ±4.
    /// </remarks>
    [Test]
    public void Build_AnEndWidth_IsLerpedByThePointsRemainingLife()
    {
        EntityTrails trails = new();
        SceneSpriteTrail tapering = Ball() with { EndWidth = 2f };

        Frame(trails, 10f, 0f, tapering);
        List<DetailSpriteVertex> corners = Corners(Frame(trails, 10.25f, 100f, tapering));

        At(corners[0]).ShouldBe(new Vector3(0f, -4f, 0f));
    }

    /// <remarks>
    /// **The tail fades over <c>m_flMinFadeLength</c> however young it is** (`:472-489`). With 200 units of it, the
    /// oldest point is at the full distance, so its fade is <c>Lerp( 0, 0, 1 )</c> = 0; the next is 100 units on, so
    /// 0.5 — below its own life of one, which loses. The head, 0 further, is still 100 short: 0.5 as well.
    /// </remarks>
    [Test]
    public void Build_AMinimumFadeLength_FadesTheTailByDistanceFromIt()
    {
        EntityTrails trails = new();
        SceneSpriteTrail fading = Ball() with { MinFadeLength = 200f };

        Frame(trails, 10f, 0f, fading);
        List<DetailSpriteVertex> corners = Corners(Frame(trails, 10.25f, 100f, fading));

        (corners[0].Alpha, corners[2].Alpha, corners[11].Alpha).ShouldBe((0f, BeamSegDraw.Packed(0.5f), BeamSegDraw.Packed(0.5f)));
    }

    /// <remarks>
    /// **A dead point is drawn once more, at zero, and then pushed off the ring** (`:516-523`). At t = 11 the first
    /// point's death time has come: it is still one of the three segments, at alpha 0, and the next frame has two.
    /// </remarks>
    [Test]
    public void Build_APointWhoseDeathTimeHasCome_IsDrawnAtZeroThenRemoved()
    {
        EntityTrails trails = new();

        Frame(trails, 10f, 0f);
        Frame(trails, 10.25f, 100f);

        List<DetailSpriteVertex> dying = Corners(Frame(trails, 11f, 100f));

        dying.Count.ShouldBe(12);
        dying[0].Alpha.ShouldBe(0f);

        Corners(Frame(trails, 11.25f, 100f)).Count.ShouldBe(6, "the dead point is gone");
    }

    /// <remarks>
    /// **Two units is not a move** — a point is added only when <c>DistToSqr( … ) &gt; 4.0f</c> (`:388`). A step of
    /// exactly two adds nothing; one of 2.5 adds a point.
    /// </remarks>
    [TestCase(2f, 6)]
    [TestCase(2.5f, 12)]
    public void Build_AStepOfAGivenLength_AddsAPointOnlyBeyondTwoUnits(float step, int corners)
    {
        EntityTrails trails = new();

        Frame(trails, 10f, 0f);

        Corners(Frame(trails, 10.25f, step)).Count.ShouldBe(corners);
    }

    /// <remarks>
    /// **Sampled no faster than a 256th of the lifetime** — <c>m_flUpdateTime = curtime + m_flLifeTime / 256</c>
    /// (`:415`), and <c>UpdateTrail</c> returns while it is in the future (`:382`). With a 2.56-second life the
    /// interval is a hundredth: a move half a hundredth later adds nothing, one a full hundredth later adds a point.
    /// </remarks>
    [TestCase(0.005f, 6)]
    [TestCase(0.015625f, 12)]
    public void Build_AMoveSoonerThanTheSampleInterval_AddsNoPoint(float later, int corners)
    {
        EntityTrails trails = new();
        SceneSpriteTrail slow = Ball() with { LifeTime = 2.56f };

        Frame(trails, 10f, 0f, slow);

        Corners(Frame(trails, 10f + later, 100f, slow)).Count.ShouldBe(corners);
    }

    /// <remarks>
    /// **The entity's colour and the sprite's brightness reach the vertex** — <c>m_clrRender / 255</c> and
    /// <c>GetRenderBrightness() / 255</c> (`:468-470`, `:490`) — and the trail's <c>UnlitGeneric</c> takes both
    /// through <c>$vertexcolor</c> and <c>$vertexalpha</c>.
    /// </remarks>
    [Test]
    public void Build_ATintedHalfBrightTrail_CarriesBothIntoTheVertex()
    {
        EntityTrails trails = new();

        Frame(trails, 10f, 0f, colour: (255, 128, 0), brightness: 128);
        DetailSpriteVertex newest = Corners(Frame(trails, 10.25f, 100f, colour: (255, 128, 0), brightness: 128))[2];

        (newest.Red, newest.Green, newest.Blue).ShouldBe((1f, 128f / 255f, 0f));
        newest.Alpha.ShouldBe(BeamSegDraw.Packed(128f / 255f));
    }

    /// <remarks>
    /// **The ring holds 256 and steals the oldest past that** (`:391-395`) — and the sample runs from
    /// <c>ClientThink</c>, which does not care whether the entity draws. So a trail sampled 300 times while its
    /// material had not loaded holds 256 points when it can draw: 257 segments with the head, 256 quads, where a list
    /// that only grew would hold 300.
    /// </remarks>
    [Test]
    public void Build_ThreeHundredSamplesWhileUndrawn_HoldsTheRingsTwoHundredFiftySix()
    {
        EntityTrails trails = new();
        SceneSpriteTrail lasting = Ball() with { LifeTime = 1000f };

        for (int frame = 0; frame < 300; frame++)
        {
            Frame(trails, 10f + (frame * 4f), frame * 10f, lasting, loaded: false);
            trails.Skipped.ShouldBe(1, "sampled and not drawn");
        }

        Corners(Frame(trails, 10f + (300 * 4f), 3000f, lasting)).Count.ShouldBe(EntityTrails.MaximumPoints * 6);
    }

    /// <remarks>
    /// **A trail the scene stops offering is forgotten** — the viewer's equivalent of the entity being deleted. Offered
    /// again, it starts from one point and the head: a single quad.
    /// </remarks>
    [Test]
    public void Build_ATrailAbsentForAFrame_StartsAgainEmpty()
    {
        EntityTrails trails = new();

        Frame(trails, 10f, 0f);
        Frame(trails, 10.25f, 100f);
        trails.Build([], Camera, Sprites(loaded: true), prop => Position(prop), 10.5f);

        Corners(Frame(trails, 10.75f, 300f)).Count.ShouldBe(6);
    }

    /// <remarks>
    /// **A clock that runs backwards is a seek, and the ring starts again** — the viewer's rule (see
    /// <see cref="EntityTrails"/>): the engine's demo player never runs a trail's clock backwards, so its points are
    /// always within a lifetime of now.
    /// </remarks>
    [Test]
    public void Build_AClockThatRanBackwards_StartsTheRingAgain()
    {
        EntityTrails trails = new();

        Frame(trails, 10f, 0f);
        Frame(trails, 10.25f, 100f);

        Corners(Frame(trails, 5f, 300f)).Count.ShouldBe(6);
    }

    /// <remarks>
    /// **A head whose entity is gone stays where it was.** The client still holds the projectile a trail names after
    /// its track ends here — dormant, or unlinked from its children at its last place — so <c>GetRenderOrigin</c> keeps
    /// answering that place. Asked at t = 10.5 with nothing to hang from, the head is still at x = 100: no new point,
    /// three segments, the last pair at 100. A trail that has never been placed has nothing to hold and is skipped.
    /// </remarks>
    [Test]
    public void Build_AHeadWithNothingToHangFrom_StaysWhereItWas()
    {
        EntityTrails trails = new();

        Frame(trails, 10f, 0f);
        Frame(trails, 10.25f, 100f);

        List<DetailSpriteVertex> corners = Corners(Frame(trails, 10.5f, 900f, orphaned: true));

        corners.Count.ShouldBe(12);
        corners[^1].X.ShouldBe(100f);

        EntityTrails unplaced = new();

        Frame(unplaced, 10f, 0f, orphaned: true).ShouldBeEmpty();
        unplaced.Skipped.ShouldBe(1);
    }

    /// <remarks>
    /// **`kRenderNone` draws nothing** — <c>DrawModel</c> returns at <c>!IsVisible()</c> (`:431`) — and a
    /// <c>Refract</c> material, which needs the frame behind it, is one this pass cannot draw (B476). Both are counted.
    /// </remarks>
    [TestCase(RenderModes.None, "UnlitGeneric")]
    [TestCase(RenderModes.TransAlpha, "Refract")]
    public void Build_AnUndrawableTrail_IsSkipped(int renderMode, string shader)
    {
        EntityTrails trails = new();

        Frame(trails, 10f, 0f, renderMode: renderMode, shader: shader);

        Frame(trails, 10.25f, 100f, renderMode: renderMode, shader: shader).ShouldBeEmpty();
        (trails.Drawn, trails.Skipped).ShouldBe((0, 1));
    }

    /// <remarks>
    /// **The sprite pass does not draw a trail as a quad at its head** — <c>CSpriteTrail::DrawModel</c> overrides
    /// <c>CSprite::DrawModel</c> (`:422`). The control is the same prop without its trail record, which is a sprite and
    /// draws.
    /// </remarks>
    [TestCase(true, 0)]
    [TestCase(false, 1)]
    public void SpriteBatches_ATrailProp_IsLeftToTheTrailPass(bool trail, int drawn)
    {
        EntitySpriteBatches sprites = new();
        SceneProp prop = Prop(0f, Ball(), (255, 255, 255), 255, RenderModes.TransAlpha);

        sprites.Build(
            [trail ? prop : prop with { Pose = prop.Pose with { SpriteTrail = null } }],
            eye: Camera,
            viewRight: Vector3.UnitX,
            viewUp: Vector3.UnitY,
            viewForward: -Vector3.UnitZ,
            sprites: Sprites(loaded: true),
            visible: _ => true);

        sprites.Drawn.ShouldBe(drawn);
    }

    /// <summary>A Sandman ball's trail: life 1, width 10, no end width, a hundredth of the texture per unit.</summary>
    private static SceneSpriteTrail Ball() => new(
        LifeTime: 1f,
        StartWidth: 10f,
        EndWidth: -1f,
        StartWidthVariance: 0f,
        TextureRes: 0.01f,
        MinFadeLength: 0f,
        SkyboxOrigin: (0f, 0f, 0f),
        SkyboxScale: 1f,
        AttachedTo: (1 << 21) - 1,
        Attachment: 0);

    private static IReadOnlyList<ParticleBatch> Frame(
        EntityTrails trails,
        float time,
        float x,
        SceneSpriteTrail? parameters = null,
        (byte, byte, byte)? colour = null,
        int brightness = 255,
        int renderMode = RenderModes.TransAlpha,
        string shader = "UnlitGeneric",
        bool loaded = true,
        bool orphaned = false) =>
        trails.Build(
            [Prop(x, parameters ?? Ball(), colour ?? ((byte)255, (byte)255, (byte)255), brightness, renderMode)],
            Camera,
            Sprites(loaded, shader),
            prop => orphaned ? null : Position(prop),
            time);

    private static SceneProp Prop(
        float x, SceneSpriteTrail parameters, (byte, byte, byte) colour, int brightness, int renderMode) => new(
        EntityIndex: 301,
        ModelPath: Material,
        Kind: SceneModelKind.Sprite,
        Pose: new ScenePose
        {
            X = x,
            RenderMode = renderMode,
            RenderColor = colour,
            Sprite = SceneSprite.Default with { Brightness = brightness },
            SpriteTrail = parameters,
        },
        ClassName: "CSpriteTrail");

    private static Dictionary<string, EngineSprite> Sprites(bool loaded, string shader = "UnlitGeneric")
    {
        Dictionary<string, EngineSprite> sprites = new(System.StringComparer.OrdinalIgnoreCase);

        if (loaded)
        {
            sprites[Material] = new EngineSprite(
                new ParticleMaterial(
                    new MapTexture(64, 64, 64, 64, TextureImage.None, IsTransparent: true), [], SpriteBlend.Translucent),
                64,
                64,
                SpriteOrientation.Parallel,
                SpriteExtents.Of(64, 64, origin: null),
                (1f, 1f, 1f, 1f),
                IgnoresVertexColors: true,
                Shader: shader,
                Modulation: (1f, 1f, 1f, 1f),
                VertexColour: true,
                VertexAlpha: true);
        }

        return sprites;
    }

    private static Vector3 Position(SceneProp prop) => new(prop.Pose.X, prop.Pose.Y, prop.Pose.Z);

    private static List<DetailSpriteVertex> Corners(IReadOnlyList<ParticleBatch> batches) =>
        [.. batches.SelectMany(batch => batch.Corners)];

    private static Vector3 At(DetailSpriteVertex corner) => new(corner.X, corner.Y, corner.Z);
}
