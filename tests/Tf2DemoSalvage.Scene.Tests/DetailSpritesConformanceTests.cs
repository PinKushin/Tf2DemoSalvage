using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The quad the engine builds for one detail sprite, for one view (B360, B361).
/// </summary>
/// <remarks>
/// **Predicted from `CDetailModel::DrawTypeSprite`, `detailobjectsystem.cpp:1012`, not from the
/// code under test.** Valve's four corners are
///
/// <code>
///   AngleVectors( m_Angles, NULL, &amp;dx, &amp;dy );
///   VectorMA( m_Origin, ul.x, dx, vecOrigin );
///   VectorMA( vecOrigin, ul.y, dy, vecOrigin );
///   dx *= (lr.x - ul.x);
///   dy *= (lr.y - ul.y);
/// </code>
///
/// then <c>vecOrigin</c>, <c>+dy</c>, <c>+dy+dx</c>, <c>+dx</c>. Every expected value below was
/// worked out by hand from that and from the rectangle the fixture declares.
///
/// **The angles are chosen so a roll-blind basis disagrees.** A sprite at roll zero and one at roll
/// 90 have to land in different planes; the second case is the whole reason
/// <see cref="AngleVectors.Right(float, float, float)"/> exists, and it is what a reduced
/// roll-is-zero basis cannot produce.
///
/// **The eye is far enough away to be inside no fade band**, except where a test is about the fade,
/// so a geometry assertion cannot pass or fail for a lighting reason.
/// </remarks>
public sealed class DetailSpritesConformanceTests
{
    private const double Tolerance = 1e-5;

    /// <summary>A fade wide enough that nothing in these fixtures is dropped or dimmed.</summary>
    private static DetailFade Near => DetailFade.For(distance: 100_000f, fade: 1f);

    /// <summary>The eye, at the origin unless a test moves it.</summary>
    private static (float X, float Y, float Z) Eye => (0f, 0f, 0f);

    /// <remarks>
    /// At angles zero the basis is Valve's <c>right = (0, -1, 0)</c> and <c>up = (0, 0, 1)</c>, so
    /// the quad stands upright in the YZ plane. A 16×16 rectangle whose upper-left is
    /// <c>(-8, 0)</c> therefore begins at <c>(0, 8, 0)</c> and runs down in Y and up in Z.
    /// </remarks>
    [Test]
    public void Build_ASpriteAtAnglesZero_PutsItsFourCornersWhereVectorMaDoes()
    {
        List<DetailSpriteVertex> world = [];

        DetailSprites.Build([Prop(angles: (0f, 0f, 0f))], [Sprite()], Eye, Near, world);

        world.Count.ShouldBe(6);

        ShouldBeAt(world[0], (0f, 8f, 0f));
        ShouldBeAt(world[1], (0f, 8f, 16f));
        ShouldBeAt(world[2], (0f, -8f, 16f));

        // The second triangle repeats the first and third corners, then closes on the fourth.
        ShouldBeAt(world[3], (0f, 8f, 0f));
        ShouldBeAt(world[4], (0f, -8f, 16f));
        ShouldBeAt(world[5], (0f, -8f, 0f));
    }

    /// <remarks>
    /// **This is the test a roll-free basis fails.** With `sr = 1` and `cr = 0` Valve's own lines
    /// give `right = (0, 0, -1)` and `up = (0, -1, 0)` — the quad lies flat instead of standing —
    /// where the reduced form this project used before B360 returns `(0, -1, 0)` and `(0, 0, 1)`
    /// whatever the roll is. Measured on `koth_harvest_final`, 27,686 of 28,699 detail props carry
    /// a non-zero roll, so the reduced form is wrong for almost all of them.
    /// </remarks>
    [Test]
    public void Build_ASpriteWithRoll_TurnsTheQuadWithIt()
    {
        List<DetailSpriteVertex> world = [];

        DetailSprites.Build([Prop(angles: (0f, 0f, 90f))], [Sprite()], Eye, Near, world);

        ShouldBeAt(world[0], (0f, 0f, 8f));
        ShouldBeAt(world[1], (0f, -16f, 8f));
        ShouldBeAt(world[2], (0f, -16f, -8f));
        ShouldBeAt(world[5], (0f, 0f, -8f));
    }

    /// <remarks>
    /// **Valve swaps the horizontal texture coordinates when the sprite is NOT flipped**
    /// (<c>detailobjectsystem.cpp:1057</c>), which reads backwards and is what the engine does.
    /// </remarks>
    [Test]
    public void Build_AnUnflippedSprite_SwapsItsHorizontalTextureCoordinates()
    {
        List<DetailSpriteVertex> world = [];

        DetailSprites.Build([Prop(angles: (0f, 0f, 0f))], [Sprite()], Eye, Near, world);

        // TexUL is (0, 0) and TexLR is (0.5, 0.25); the swap sends the LR x to the first corner.
        (world[0].U, world[0].V).ShouldBe((0.5f, 0f));
        (world[1].U, world[1].V).ShouldBe((0.5f, 0.25f));
        (world[2].U, world[2].V).ShouldBe((0f, 0.25f));
        (world[5].U, world[5].V).ShouldBe((0f, 0f));
    }

    /// <remarks>
    /// **The control for the swap, and it is the case that once shipped wrong.** `m_bFlipped` is not
    /// a field of the lump — `UnserializeModels` toggles it at the top of every iteration
    /// (<c>detailobjectsystem.cpp:1771-1774</c>) — so every second detail object is mirrored, and a
    /// builder that swapped unconditionally would draw half a map's grass facing the wrong way.
    /// Without this case, "swaps" and "always swaps" are the same observation.
    /// </remarks>
    [Test]
    public void Build_AFlippedSprite_KeepsItsHorizontalTextureCoordinatesAsRead()
    {
        List<DetailSpriteVertex> world = [];

        DetailSprites.Build(
            [Prop(angles: (0f, 0f, 0f), flipped: true)], [Sprite()], Eye, Near, world);

        (world[0].U, world[0].V).ShouldBe((0f, 0f));
        (world[2].U, world[2].V).ShouldBe((0.5f, 0.25f));
    }

    /// <remarks>
    /// The scale multiplies the RECTANGLE, not the finished quad — `Vector2DMultiply( dict.m_UL,
    /// scale, ul )` runs before the origin is displaced — so doubling it doubles both the offset to
    /// the first corner and the extent from it.
    /// </remarks>
    [Test]
    public void Build_ASpriteAtDoubleScale_MultipliesTheRectangleBeforeThePlacement()
    {
        List<DetailSpriteVertex> world = [];

        DetailSprites.Build(
            [Prop(angles: (0f, 0f, 0f), scale: 2f)], [Sprite()], Eye, Near, world);

        ShouldBeAt(world[0], (0f, 16f, 0f));
        ShouldBeAt(world[2], (0f, -16f, 32f));
    }

    /// <remarks>
    /// **`DETAIL_PROP_ORIENT_SCREEN_ALIGNED_VERTICAL` turns about the vertical axis only.**
    /// `ComputeAngles` case 2 zeroes the direction's z BEFORE `VectorAngles`, so a sprite at the
    /// origin with the eye due east faces yaw 0 whatever height the eye is at — which is what keeps
    /// grass standing up rather than leaning back at a viewer on a hill.
    ///
    /// The prediction: at yaw 0 the basis is `right = (0, -1, 0)`, `up = (0, 0, 1)`, so the quad is
    /// the same one the angles-zero case produces. The eye is placed high as well as far, and the
    /// height must change nothing.
    /// </remarks>
    [Test]
    public void Build_AVerticallyScreenAlignedSprite_IgnoresTheEyeHeight()
    {
        List<DetailSpriteVertex> world = [];

        DetailSprites.Build(
            [Prop(angles: (33f, 44f, 55f), orientation: 2)],
            [Sprite()],
            (500f, 0f, 900f),
            Near,
            world);

        ShouldBeAt(world[0], (0f, 8f, 0f));
        ShouldBeAt(world[2], (0f, -8f, 16f));
    }

    /// <remarks>
    /// **The lump's own angles are discarded for a screen-aligned sprite**, which is the half of
    /// the rule a test using zero angles cannot see: the fixture declares (33, 44, 55) and the quad
    /// must land where the EYE puts it. Moving the eye to due north turns the quad by 90 degrees.
    /// </remarks>
    [Test]
    public void Build_AVerticallyScreenAlignedSprite_TurnsWithTheEyeRatherThanTheLump()
    {
        List<DetailSpriteVertex> world = [];

        DetailSprites.Build(
            [Prop(angles: (33f, 44f, 55f), orientation: 2)],
            [Sprite()],
            (0f, 500f, 0f),
            Near,
            world);

        // Yaw 90: right = (1, 0, 0), up = (0, 0, 1). The first corner is at -8 along right.
        ShouldBeAt(world[0], (-8f, 0f, 0f));
        ShouldBeAt(world[2], (8f, 0f, 16f));
    }

    /// <remarks>
    /// **`DETAIL_PROP_ORIENT_SCREEN_ALIGNED` keeps the z**, so an eye above the sprite tips it back.
    /// The control is the vertical case above, which does not — without both, "uses the eye" and
    /// "uses the eye correctly" are the same observation.
    ///
    /// With the eye straight up, `VectorAngles(0, 0, 900)` takes the vertical branch and returns
    /// pitch 270, yaw 0. `Right(270, 0, 0)` is `(0, -1, 0)` — pitch drops out of `right` at roll
    /// zero — and `Up(270, 0, 0)` is `(sin 270, 0, cos 270)` = `(-1, 0, 0)`. So the quad lies flat.
    /// </remarks>
    [Test]
    public void Build_AFullyScreenAlignedSprite_TipsBackForAnEyeOverhead()
    {
        List<DetailSpriteVertex> world = [];

        DetailSprites.Build(
            [Prop(angles: (0f, 0f, 0f), orientation: 1)],
            [Sprite()],
            (0f, 0f, 900f),
            Near,
            world);

        ShouldBeAt(world[0], (0f, 8f, 0f));

        // +dy, where dy is up scaled by the rectangle's height of 16: (-1, 0, 0) * 16.
        ShouldBeAt(world[1], (-16f, 8f, 0f));
    }

    /// <remarks>
    /// **A sprite beyond `cl_detaildist` is not built at all**, which is what the engine does with
    /// it: `SortSpritesBackToFront` skips `GetAlpha() == 0` before it ever reaches a mesh. The
    /// control is the near sprite in the same call, which must survive.
    /// </remarks>
    [Test]
    public void Build_ASpriteBeyondTheFadeDistance_IsDroppedAndCounted()
    {
        List<DetailSpriteVertex> world = [];

        DetailSprites.Frame frame = DetailSprites.Build(
            [Prop(angles: (0f, 0f, 0f)), Prop(angles: (0f, 0f, 0f), origin: (5_000f, 0f, 0f))],
            [Sprite()],
            Eye,
            DetailFade.For(distance: 1200f, fade: 400f),
            world);

        frame.Built.ShouldBe(1);
        frame.Faded.ShouldBe(1);
        world.Count.ShouldBe(6);
    }

    /// <remarks>
    /// **The fade reaches the vertex, and it is the same value on all four corners.** A sprite
    /// halfway across the band is 127 of 255 — see `DetailFadeConformanceTests` for that arithmetic
    /// — and this is the assertion that says the number is carried rather than computed and dropped.
    /// </remarks>
    [Test]
    public void Build_ASpriteInsideTheFadeBand_CarriesItsAlphaOnEveryCorner()
    {
        List<DetailSpriteVertex> world = [];

        // 1,040,000 squared units from the eye is halfway across the default band.
        DetailSprites.Build(
            [Prop(angles: (0f, 0f, 0f), origin: (1_019.803_9f, 0f, 0f))],
            [Sprite()],
            Eye,
            DetailFade.For(distance: 1200f, fade: 400f),
            world);

        world.ShouldAllBe(corner => corner.Alpha > 0.49f && corner.Alpha < 0.51f);
    }

    /// <remarks>
    /// **Farthest first, because a blended quad is combined with what is already drawn.** Three
    /// sprites at three distances, emitted in the wrong order in the list, must come out ordered by
    /// distance — the middle one between the other two, which an implementation that merely
    /// reversed the input would get wrong.
    /// </remarks>
    [Test]
    public void Build_SeveralSprites_EmitsThemFarthestFirst()
    {
        List<DetailSpriteVertex> world = [];

        DetailSprites.Build(
            [Prop(angles: (0f, 0f, 0f), origin: (300f, 0f, 0f)),
             Prop(angles: (0f, 0f, 0f), origin: (900f, 0f, 0f)),
             Prop(angles: (0f, 0f, 0f), origin: (600f, 0f, 0f))],
            [Sprite()],
            Eye,
            Near,
            world);

        world.Count.ShouldBe(18);

        world[0].X.ShouldBe(900f, Tolerance);
        world[6].X.ShouldBe(600f, Tolerance);
        world[12].X.ShouldBe(300f, Tolerance);
    }

    /// <remarks>
    /// A model detail prop indexes the MODEL dictionary, so reading it as a sprite would index the
    /// sprite dictionary with a model number. The engine draws those through `DrawTypeModel`; here
    /// they are left out rather than drawn wrongly, and the control is the sprite beside it.
    /// </remarks>
    [Test]
    public void Build_AModelDetailProp_IsLeftToTheModelPath()
    {
        List<DetailSpriteVertex> world = [];

        DetailSprites.Frame frame = DetailSprites.Build(
            [Prop(angles: (0f, 0f, 0f), type: DetailPropType.Model),
             Prop(angles: (0f, 0f, 0f))],
            [Sprite()],
            Eye,
            Near,
            world);

        frame.Built.ShouldBe(1);
        frame.Faded.ShouldBe(0);
    }

    /// <remarks>
    /// A map is untrusted input (D32). A sprite index outside the dictionary is dropped rather than
    /// throwing out of a map that is otherwise fine, which is what the engine's own
    /// `DetailSpriteDict` bounds check amounts to.
    /// </remarks>
    [Test]
    public void Build_ASpriteIndexOutsideTheDictionary_IsDropped()
    {
        List<DetailSpriteVertex> world = [];

        DetailSprites.Build([Prop(angles: (0f, 0f, 0f), sprite: 9)], [Sprite()], Eye, Near, world)
            .Built.ShouldBe(0);

        world.ShouldBeEmpty();
    }

    /// <remarks>
    /// **The eye standing exactly in the grass is an ordinary input, not an edge**, and it is where
    /// `VectorAngles` takes its no-`atan2` branch. It must produce a quad rather than a NaN.
    /// </remarks>
    [Test]
    public void Build_AnEyeAtTheSpritesOwnOrigin_StillProducesAQuad()
    {
        List<DetailSpriteVertex> world = [];

        DetailSprites.Build(
            [Prop(angles: (0f, 0f, 0f), orientation: 1)], [Sprite()], Eye, Near, world)
            .Built.ShouldBe(1);

        world.ShouldAllBe(corner => !float.IsNaN(corner.X) && !float.IsNaN(corner.Y)
            && !float.IsNaN(corner.Z));
    }

    /// <summary>One corner, to a tolerance, because the basis is built from sines and cosines.</summary>
    /// <remarks>
    /// **A tolerance rather than an exact tuple, and the reason is measurable rather than stylistic.**
    /// `MathF.SinCos(90°)` returns a cosine of −4.37e-8 rather than zero, so a corner Valve's own
    /// arithmetic puts at exactly 0 arrives at −3.5e-7 once it has been multiplied by a 16-unit
    /// rectangle. The predicted value is still exact; only the comparison is loosened.
    /// </remarks>
    private static void ShouldBeAt(DetailSpriteVertex vertex, (float X, float Y, float Z) expected)
    {
        vertex.X.ShouldBe(expected.X, Tolerance);
        vertex.Y.ShouldBe(expected.Y, Tolerance);
        vertex.Z.ShouldBe(expected.Z, Tolerance);
    }

    /// <summary>A 16×16 rectangle whose upper-left is left of the origin, on a quarter of the sheet.</summary>
    private static BspDetailSprite Sprite() =>
        new((-8f, 0f), (8f, 16f), (0f, 0f), (0.5f, 0.25f));

    private static BspDetailProp Prop(
        (float Pitch, float Yaw, float Roll) angles,
        int orientation = 0,
        float scale = 1f,
        int sprite = 0,
        DetailPropType type = DetailPropType.Sprite,
        (float Red, float Green, float Blue) lighting = default,
        bool flipped = false,
        (float X, float Y, float Z) origin = default) =>
        new(origin, angles, sprite, Leaf: 0, type, orientation,
            SwayAmount: 0, ShapeAngle: 0, ShapeSize: 0, scale, lighting, flipped);
}
