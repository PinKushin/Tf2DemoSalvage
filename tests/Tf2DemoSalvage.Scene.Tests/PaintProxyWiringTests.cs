using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// That an item's paint reaches the drawn instance, per entity (B330).
/// </summary>
/// <remarks>
/// **The hop that has shipped three no-ops in this project**: a value decoded, unit-tested, and read
/// by nothing. `ItemPaintConformanceTests` proves the arithmetic of `GetModifiedRGBValue` when a
/// test calls it; it says nothing about whether anything asks, or with what.
///
/// **The per-ENTITY property is the whole point and is what these assert.** A proxy is not a
/// material setting: two players wearing the same hat in different paints share one material and
/// must draw different colours. A design that folded the paint into the material at load would pass
/// every test in the conformance suite and give both players the same hat.
/// </remarks>
public sealed class PaintProxyWiringTests
{
    [Test]
    public void Instances_APaintedProp_CarryThatColourToTheDrawnInstance()
    {
        ModelInstance drawn = Drawn(paint: (0.9f, 0.4f, 0.1f));

        drawn.Paint.ShouldBe((0.9f, 0.4f, 0.1f));
    }

    /// <remarks>
    /// **The control, and without it the test above is satisfied by a constant.** Almost every item
    /// in a demo is unpainted — 12 of 51 econ items were painted in the match this was measured on —
    /// so a delegate whose result was ignored, or one that returned a colour unconditionally, would
    /// tint the entire game.
    /// </remarks>
    [Test]
    public void Instances_AnUnpaintedProp_CarryNoColour()
    {
        Drawn(paint: null).Paint.ShouldBeNull();
    }

    /// <remarks>
    /// **Two props, one material, two colours — which is what a proxy IS.** This is the assertion a
    /// material-level implementation cannot pass, and it is the reason the value travels on the
    /// instance rather than on the material state.
    /// </remarks>
    [Test]
    public void Instances_TwoPropsSharingAModel_KeepTheirOwnPaint()
    {
        SceneProp[] props =
        [
            Prop(entity: 1),
            Prop(entity: 2),
        ];

        EntityModelSet models = new()
        {
            // Keyed on the entity so the two differ, which a per-material answer could not do.
            Paint = prop => prop.EntityIndex == 1 ? (1f, 0f, 0f) : (0f, 0f, 1f),
        };

        List<ModelInstance> instances = [];

        models.Add(props, Frames);
        models.Instances(props, instances);

        instances.Count.ShouldBe(2);
        instances[0].Paint.ShouldBe((1f, 0f, 0f));
        instances[1].Paint.ShouldBe((0f, 0f, 1f));
    }

    /// <remarks>
    /// **No delegate means unpainted, not a crash and not a guess.** Every test that does not care
    /// leaves it null, and so does a viewer with no game install — where "this item is not painted"
    /// is the right answer rather than an invented one.
    /// </remarks>
    [Test]
    public void Instances_WithNoPaintDelegateAtAll_CarryNoColour()
    {
        SceneProp[] props = [Prop(entity: 1)];

        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        models.Add(props, Frames);
        models.Instances(props, instances);

        instances.ShouldHaveSingleItem().Paint.ShouldBeNull();
    }

    private static ModelInstance Drawn((float Red, float Green, float Blue)? paint)
    {
        SceneProp[] props = [Prop(entity: 1)];

        EntityModelSet models = new() { Paint = _ => paint };

        List<ModelInstance> instances = [];

        models.Add(props, Frames);
        models.Instances(props, instances);

        return instances.ShouldHaveSingleItem();
    }

    private static SceneProp Prop(int entity) =>
        new(
            entity,
            "models/player/items/scout/summer_shades.mdl",
            SceneModelKind.Studio,
            new ScenePose { X = 100f, Y = 0f, Z = 0f, Scale = 1f },
            null);

    private static PropModels.ModelFrames Frames(string path) =>
        new(
            [[
                new PropVertex(0f, 0f, 0f, 0f, 0f, 0),
                new PropVertex(1f, 0f, 0f, 1f, 0f, 0),
                new PropVertex(1f, 1f, 0f, 1f, 1f, 0),
            ]],
            new Dictionary<int, (int, int, float)>(),
            [0, 1],
            [false, false]);
}
