using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The quad the engine builds for one detail sprite (B360).
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
/// </remarks>
public sealed class DetailSpritesConformanceTests
{
    private const int Material = 4;

    /// <remarks>
    /// At angles zero the basis is Valve's <c>right = (0, -1, 0)</c> and <c>up = (0, 0, 1)</c>, so
    /// the quad stands upright in the YZ plane. A 16×16 rectangle whose upper-left is
    /// <c>(-8, 0)</c> therefore begins at <c>(0, 8, 0)</c> and runs down in Y and up in Z.
    /// </remarks>
    [Test]
    public void Build_ASpriteAtAnglesZero_PutsItsFourCornersWhereVectorMaDoes()
    {
        List<PropVertex> world = [];

        DetailSprites.Build([Prop(angles: (0f, 0f, 0f))], [Sprite()], Material, world);

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
        List<PropVertex> world = [];

        DetailSprites.Build([Prop(angles: (0f, 0f, 90f))], [Sprite()], Material, world);

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
        List<PropVertex> world = [];

        DetailSprites.Build([Prop(angles: (0f, 0f, 0f))], [Sprite()], Material, world);

        // TexUL is (0, 0) and TexLR is (0.5, 0.25); the swap sends the LR x to the first corner.
        (world[0].U, world[0].V).ShouldBe((0.5f, 0f));
        (world[1].U, world[1].V).ShouldBe((0.5f, 0.25f));
        (world[2].U, world[2].V).ShouldBe((0f, 0.25f));
        (world[5].U, world[5].V).ShouldBe((0f, 0f));
    }

    /// <remarks>
    /// **The control for the swap, and it is the case that was shipped wrong.** `m_bFlipped` is not
    /// a field of the lump — `UnserializeModels` toggles it at the top of every iteration
    /// (<c>detailobjectsystem.cpp:1771-1774</c>) — so every second detail object is mirrored, and a
    /// builder that swapped unconditionally would draw half a map's grass facing the wrong way.
    /// Without this case, "swaps" and "always swaps" are the same observation.
    /// </remarks>
    [Test]
    public void Build_AFlippedSprite_KeepsItsHorizontalTextureCoordinatesAsRead()
    {
        List<PropVertex> world = [];

        DetailSprites.Build([Prop(angles: (0f, 0f, 0f), flipped: true)], [Sprite()], Material, world);

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
        List<PropVertex> world = [];

        DetailSprites.Build(
            [Prop(angles: (0f, 0f, 0f), scale: 2f)], [Sprite()], Material, world);

        ShouldBeAt(world[0], (0f, 16f, 0f));
        ShouldBeAt(world[2], (0f, -16f, 32f));
    }

    /// <remarks>
    /// **A screen-aligned sprite is counted and not built**, because
    /// `CDetailModel::ComputeAngles` recomputes its angles from `CurrentViewOrigin()` every frame
    /// (<c>detailobjectsystem.cpp:950</c>) and there is no one orientation to bake. The control is
    /// the fixed-orientation prop beside it, which must still be built — without it "built nothing"
    /// and "skipped the right one" are the same observation.
    /// </remarks>
    [Test]
    public void Build_AScreenAlignedSprite_IsCountedRatherThanBuilt()
    {
        List<PropVertex> world = [];

        (int built, int screenAligned) = DetailSprites.Build(
            [Prop(angles: (0f, 0f, 0f), orientation: 1),
             Prop(angles: (0f, 0f, 0f), orientation: 2),
             Prop(angles: (0f, 0f, 0f))],
            [Sprite()],
            Material,
            world);

        built.ShouldBe(1);
        screenAligned.ShouldBe(2);
        world.Count.ShouldBe(6);
    }

    /// <remarks>
    /// **`m_Lighting` is the only light a detail sprite has** — it is a loose quad with no lightmap
    /// — and `GetColorModulation` reads it through `TexLightToLinear`, which is
    /// <c>channel * 2^exponent</c>. This project's convention is that 255 at exponent 0 is full
    /// brightness (`BspLightmaps.Decode`), so the vertex colour is that quotient, clamped.
    /// </remarks>
    [Test]
    public void Build_ASpriteWithBakedLight_CarriesItAsTheVertexColour()
    {
        List<PropVertex> world = [];

        DetailSprites.Build(
            [Prop(angles: (0f, 0f, 0f), lighting: (255f, 127.5f, 0f))],
            [Sprite()],
            Material,
            world);

        world[0].Red.ShouldBe(1f);
        world[0].Green.ShouldBe(0.5f);
        world[0].Blue.ShouldBe(0f);
    }

    /// <remarks>
    /// Over-range light is what the exponent is FOR — a sample of 255 at exponent 1 is twice full
    /// brightness — and this renderer works in display space with no tone map, so the clamp is the
    /// same one <see cref="BspLightmaps"/> applies rather than a guard against bad data.
    /// </remarks>
    [Test]
    public void Build_ASpriteLitAboveWhite_ClampsRatherThanWrapping()
    {
        List<PropVertex> world = [];

        DetailSprites.Build(
            [Prop(angles: (0f, 0f, 0f), lighting: (510f, 510f, 510f))],
            [Sprite()],
            Material,
            world);

        world[0].Red.ShouldBe(1f);
    }

    /// <remarks>
    /// A model detail prop indexes the MODEL dictionary, so reading it as a sprite would index the
    /// sprite dictionary with a model number. The engine draws those through
    /// `DrawTypeModel`; here they are left out rather than drawn wrongly, and the control is the
    /// sprite beside it.
    /// </remarks>
    [Test]
    public void Build_AModelDetailProp_IsLeftToTheModelPath()
    {
        List<PropVertex> world = [];

        (int built, int screenAligned) = DetailSprites.Build(
            [Prop(angles: (0f, 0f, 0f), type: DetailPropType.Model),
             Prop(angles: (0f, 0f, 0f))],
            [Sprite()],
            Material,
            world);

        built.ShouldBe(1);
        screenAligned.ShouldBe(0);
    }

    /// <remarks>
    /// A map is untrusted input (D32). A sprite index outside the dictionary is dropped rather
    /// than throwing out of a map that is otherwise fine, which is what the engine's own
    /// `DetailSpriteDict` bounds check amounts to.
    /// </remarks>
    [Test]
    public void Build_ASpriteIndexOutsideTheDictionary_IsDropped()
    {
        List<PropVertex> world = [];

        DetailSprites.Build([Prop(angles: (0f, 0f, 0f), sprite: 9)], [Sprite()], Material, world)
            .Built.ShouldBe(0);

        world.ShouldBeEmpty();
    }

    /// <remarks>
    /// Every vertex carries the material the caller resolved, because the engine has exactly one:
    /// <c>#define DETAIL_SPRITE_MATERIAL "detail/detailsprites"</c>.
    /// </remarks>
    [Test]
    public void Build_EveryCorner_CarriesTheSharedDetailSpriteMaterial()
    {
        List<PropVertex> world = [];

        DetailSprites.Build([Prop(angles: (0f, 0f, 0f))], [Sprite()], Material, world);

        world.ShouldAllBe(corner => corner.MaterialIndex == Material);
    }

    /// <summary>One corner, to a tolerance, because the basis is built from sines and cosines.</summary>
    /// <remarks>
    /// **A tolerance rather than an exact tuple, and the reason is measurable rather than stylistic.**
    /// `MathF.SinCos(90°)` returns a cosine of −4.37e-8 rather than zero, so a corner Valve's own
    /// arithmetic puts at exactly 0 arrives at −3.5e-7 once it has been multiplied by a 16-unit
    /// rectangle. The predicted value is still exact; only the comparison is loosened.
    /// </remarks>
    private static void ShouldBeAt(PropVertex vertex, (float X, float Y, float Z) expected)
    {
        vertex.X.ShouldBe(expected.X, Tolerance);
        vertex.Y.ShouldBe(expected.Y, Tolerance);
        vertex.Z.ShouldBe(expected.Z, Tolerance);
    }

    private const double Tolerance = 1e-5;

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
        bool flipped = false) =>
        new((0f, 0f, 0f), angles, sprite, Leaf: 0, type, orientation,
            SwayAmount: 0, ShapeAngle: 0, ShapeSize: 0, scale, lighting, flipped);
}
