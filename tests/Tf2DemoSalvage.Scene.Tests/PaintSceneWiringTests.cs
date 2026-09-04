using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// That <c>MomentScene</c> supplies the paint delegate, driven the way the viewer drives it (B330).
/// </summary>
/// <remarks>
/// **The last hop, and the one nothing else watches.** `PaintProxyWiringTests` sets the delegate
/// itself and proves `EntityModelSet` calls it; `ItemPaintConformanceTests` proves the arithmetic.
/// Neither can say whether PRODUCTION ever assigns `_models.Paint` — and if it stopped, every test
/// above stays green while every painted item in every demo draws in its default colour.
///
/// **The same shape as `ClientSideAnimationWiringTests`**, and for the same reason recorded there: a
/// call that lives in `MomentScene.Build` has been lost in a move once already, and nothing in the
/// suite noticed because every other test drove the layer beneath it.
/// </remarks>
public sealed class PaintSceneWiringTests
{
    [Test]
    public void Build_APaintedItem_ReachesTheDrawnInstanceThroughTheScene()
    {
        EntityModelSet models = new() { Geometry = _ => Frames() };

        MomentScene scene = new(models, new ViewmodelScene(), new RecordingLogger())
        {
            Weapons = Weapons(),
        };

        SceneProp hat = PaintedHat();

        scene.Build([], [], default);

        List<ModelInstance> instances = [];

        models.Add([hat], _ => Frames());
        models.Instances([hat], instances);

        // The delegate is assigned during Build and read during Instances, which is the order the
        // viewer runs them in.
        instances.ShouldHaveSingleItem().Paint.ShouldNotBeNull(
            "MomentScene.Build must supply EntityModelSet.Paint; without it every painted item in "
            + "every demo draws in its default colour and nothing else in the suite notices");
    }

    /// <remarks>
    /// **The control.** With a schema that names no paint attribute — which is what a viewer with
    /// no game install has — the delegate must still be assigned and must answer null. A test that
    /// only checked for non-null would pass against a build that tinted everything.
    /// </remarks>
    [Test]
    public void Build_AnItemWithNoPaint_ReachesTheDrawnInstanceUntinted()
    {
        EntityModelSet models = new() { Geometry = _ => Frames() };

        MomentScene scene = new(models, new ViewmodelScene(), new RecordingLogger())
        {
            Weapons = Weapons(),
        };

        SceneProp hat = UnpaintedHat();

        scene.Build([], [], default);

        List<ModelInstance> instances = [];

        models.Add([hat], _ => Frames());
        models.Instances([hat], instances);

        instances.ShouldHaveSingleItem().Paint.ShouldBeNull();
    }

    /// <summary>The schema, which names the paint attributes the wire's indices refer to.</summary>
    private static WeaponModels Weapons() =>
        new(
            path => path.EndsWith("items_game.txt", System.StringComparison.OrdinalIgnoreCase)
                ? Encoding.UTF8.GetBytes(Schema)
                : null,
            new RecordingLogger());

    /// <summary>A hat carrying one econ attribute on the wire.</summary>
    /// <remarks>
    /// **The attribute travels as a FLOAT whose value is the packed colour**, which is the form
    /// `CEconItemView` reads with `(uint32)fRGB` — the fixture puts the wire's own shape in rather
    /// than the integer.
    /// </remarks>
    private static SceneProp Hat(int definition, int value) =>
        new(
            1,
            "models/player/items/scout/summer_shades.mdl",
            SceneModelKind.Studio,
            new ScenePose { X = 100f, Y = 0f, Z = 0f, Scale = 1f },
            null,
            ItemDefinitionIndex: 486,
            Econ: new EconAttributeWire(
                [new EconAttributeValue(definition, System.BitConverter.SingleToInt32Bits(value))],
                [],
                HasValidItemId: true));

    /// <summary>The same hat, painted.</summary>
    private static SceneProp PaintedHat() => Hat(PaintDefinition, 0xE7B53B);

    /// <summary>The same hat carrying an attribute that is not paint.</summary>
    private static SceneProp UnpaintedHat() => Hat(FestiveDefinition, 1);

    private const int PaintDefinition = 142;

    private const int FestiveDefinition = 2053;

    /// <summary>A schema naming the paint attribute at the index the fixture uses.</summary>
    private const string Schema = """
        "items_game"
        {
            "attributes"
            {
                "142"  { "name" "set item tint rgb" }
                "261"  { "name" "set item tint rgb 2" }
                "2053" { "name" "is_festivized"  "stored_as_integer" "1" }
            }
        }
        """;

    private static PropModels.ModelFrames Frames() =>
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
