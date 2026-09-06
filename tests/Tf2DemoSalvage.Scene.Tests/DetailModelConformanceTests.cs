using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The detail props a map scatters that are MODELS rather than sprites (B363).
/// </summary>
/// <remarks>
/// **Predicted from the engine, not from the code under test.** A `DETAIL_PROP_TYPE_MODEL` is an
/// ordinary studio model that reaches the render list through
/// `CClientLeafSystem::CollateRenderablesInLeaf` (`clientleafsystem.cpp:1718`) rather than through
/// the sprite path — `CDetailModel::DrawSprite` has no case for the type at all and would
/// `Assert(0)` on one.
///
/// **`cp_granary` places 324 of them** (`models/props_foliage/grass_02_detailmodel.mdl`), all at
/// orientation 0, against 19,189 sprites; `koth_harvest_final`, `cp_process_f12` and both badlands
/// place none. So the map that stresses this is not the map that stresses the sprites, which is why
/// the fixtures here use granary's shape.
/// </remarks>
public sealed class DetailModelConformanceTests
{
    /// <remarks>
    /// **A sprite's `m_DetailModel` indexes the SPRITE dictionary and a model's indexes the MODEL
    /// dictionary**, so the type is the only thing that separates the two populations. Reading them
    /// together would place a model at every sprite's index — 19,189 of them on granary, where the
    /// map has 324.
    /// </remarks>
    [Test]
    public void Build_WithSpritesAndModelsTogether_PlacesOnlyTheModels()
    {
        List<DetailModelInstance> placed = [];

        DetailModels.Frame frame = DetailModels.Build(
            [
                Prop(DetailPropType.Model, model: 0),
                Prop(DetailPropType.Sprite, model: 7),
                Prop(DetailPropType.Model, model: 1),
            ],
            Eye,
            DetailFade.For(1200f, 400f),
            placed);

        frame.Built.ShouldBe(2);
        placed.Count.ShouldBe(2);
        placed[0].Model.ShouldBe(0);
        placed[1].Model.ShouldBe(1, "the sprite between them must not shift the indices");
    }

    /// <remarks>
    /// **`EnumerateLeaf` sets the alpha for every `CDetailModel` in the leaf without asking its
    /// type** (`detailobjectsystem.cpp:2765`), so a model fades on exactly the same curve as a
    /// sprite. Sampled between the knots rather than at them.
    /// </remarks>
    [Test]
    public void Build_ForAModelMidFade_CarriesTheSameAlphaASpriteWould()
    {
        List<DetailModelInstance> placed = [];
        DetailFade fade = DetailFade.For(1200f, 400f);

        DetailModels.Build([Prop(DetailPropType.Model, x: 1000f)], Eye, fade, placed);

        placed[0].Alpha.ShouldBe(fade.Alpha(1000f * 1000f));
        placed[0].Alpha.ShouldBeGreaterThan((byte)0);
        placed[0].Alpha.ShouldBeLessThan((byte)255);
    }

    /// <remarks>
    /// **Zero alpha is not drawn, and the engine enforces it twice**: `DrawModel` returns 0 for it
    /// (`detailobjectsystem.cpp:696`) and `CollateRenderablesInLeaf` only adds a transparent
    /// renderable `if( pRenderable->GetFxBlend() > 0 )` (`clientleafsystem.cpp:1734`). So a
    /// faded-out model must not reach the list at all — emitting it at alpha 0 would be a draw call
    /// the engine never issues.
    /// </remarks>
    [Test]
    public void Build_ForAModelBeyondTheFadeDistance_PlacesNothing()
    {
        List<DetailModelInstance> placed = [];

        DetailModels.Frame frame = DetailModels.Build(
            [Prop(DetailPropType.Model, x: 5000f)], Eye, DetailFade.For(1200f, 400f), placed);

        placed.ShouldBeEmpty();
        frame.Built.ShouldBe(0);
        frame.Faded.ShouldBe(1, "it was dropped by the fade rather than by the type filter");
    }

    /// <remarks>
    /// **The control for the test above.** The same model inside the fade start is placed at full
    /// alpha — otherwise "drops the far one" and "drops everything" read the same.
    /// </remarks>
    [Test]
    public void Build_ForAModelInsideTheFadeStart_PlacesItFullyOpaque()
    {
        List<DetailModelInstance> placed = [];

        DetailModels.Frame frame = DetailModels.Build(
            [Prop(DetailPropType.Model, x: 100f)], Eye, DetailFade.For(1200f, 400f), placed);

        placed.Count.ShouldBe(1);
        placed[0].Alpha.ShouldBe((byte)255);
        frame.Translucent.ShouldBe(0, "at 255 it is an opaque renderable, not a translucent one");
    }

    /// <remarks>
    /// **`ComputeAngles` is called for MODELS too.** `EnumerateLeaf` invokes it inside the same
    /// in-range branch that sets the alpha, with no type test (`detailobjectsystem.cpp:2775`), so a
    /// screen-aligned detail model turns to face the eye exactly as a sprite does. The fixture puts
    /// the eye off to one side so a yaw that ignored it would be visibly wrong.
    /// </remarks>
    [Test]
    public void Build_ForAScreenAlignedModel_TurnsItTowardTheEye()
    {
        List<DetailModelInstance> placed = [];

        DetailModels.Frame frame = DetailModels.Build(
            [Prop(DetailPropType.Model, x: 100f, orientation: 2, angles: (0f, 123f, 0f))],
            Eye,
            DetailFade.For(1200f, 400f),
            placed);

        // The eye is at the origin and the model at +100 X, so the direction to the eye is −X:
        // yaw 180, and the model's own 123 is discarded.
        placed[0].Angles.Yaw.ShouldBe(180f, 0.01);
        placed[0].Angles.Pitch.ShouldBe(0f, 0.01);
        frame.Aligned.ShouldBe(1);
    }

    /// <remarks>
    /// **Orientation 0 keeps the map's own angles**, which is what every one of granary's 324 uses.
    /// Without this the screen-aligned case above would pass against code that turned everything.
    /// </remarks>
    [Test]
    public void Build_ForAFixedOrientationModel_KeepsTheMapsAngles()
    {
        List<DetailModelInstance> placed = [];

        DetailModels.Frame frame = DetailModels.Build(
            [Prop(DetailPropType.Model, x: 100f, orientation: 0, angles: (10f, 123f, 4f))],
            Eye,
            DetailFade.For(1200f, 400f),
            placed);

        placed[0].Angles.ShouldBe((10f, 123f, 4f));
        frame.Aligned.ShouldBe(0);
    }

    /// <remarks>
    /// **`cl_detail_multiplier` multiplies SPRITES and not models.** The sprite branches of
    /// `UnserializeModels` loop `SPRITE_MULTIPLIER` times and jitter each copy by
    /// `RandomVector( -50, 50 )`; the `DETAIL_PROP_TYPE_MODEL` branch
    /// (`detailobjectsystem.cpp:1829`) adds exactly one object and never reads the macro. One lump
    /// entry is therefore one model, always — asserted here because the setting is `FCVAR_CHEAT`
    /// and defaults to 1, so a viewer that multiplied both would look correct until somebody
    /// changed it.
    /// </remarks>
    [Test]
    public void Build_ForOneModelLump_PlacesExactlyOne()
    {
        List<DetailModelInstance> placed = [];

        DetailModels.Build([Prop(DetailPropType.Model)], Eye, DetailFade.For(1200f, 400f), placed);

        placed.Count.ShouldBe(1);
    }

    /// <summary>The eye, at the world origin, so a distance is the model's own X.</summary>
    private static readonly (float X, float Y, float Z) Eye = (0f, 0f, 0f);

    private static BspDetailProp Prop(
        DetailPropType type,
        int model = 0,
        float x = 100f,
        int orientation = 0,
        (float Pitch, float Yaw, float Roll) angles = default) =>
        new(
            (x, 0f, 0f),
            angles,
            model,
            Leaf: 0,
            type,
            orientation,
            SwayAmount: 0,
            ShapeAngle: 0,
            ShapeSize: 0,
            Scale: 1f);
}
