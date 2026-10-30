using System;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The attribute questions the scene asks of every placed prop every frame — paint, festive, the walk — answered as before
/// and with nothing allocated (B599).
/// </summary>
/// <remarks>
/// **Why bytes are asserted at all:** `PaintFor` and `IsFestivized` run through the model set's Paint and Attachments
/// delegates once per placed prop per frame. Each built a list, a set and (paint) a dictionary, and the definition's own
/// attributes were rebuilt through the prefab chain on every call. The engine allocates nothing here: the definition's
/// attributes are parsed once (`m_vecStaticAttributes`, econ_item_schema.cpp:3330-3359) and `IterateAttributes` streams
/// (econ_item_view.cpp:523-600).
/// </remarks>
public sealed class WeaponModelsAttributeTests
{
    private const int PaintIndex = 142;
    private const int FestiveIndex = 2053;
    private const int UnpaintedItem = 30;
    private const int PaintedItem = 31;
    private const int TwoTonePaint = 15185723; // 0xE7B73B
    private const int TwoToneSecond = 0x3B68E7;

    private const string Schema = """
        "items_game"
        {
            "attributes"
            {
                "142" { "name" "set item tint rgb" }
                "261" { "name" "set item tint rgb 2" }
                "2053" { "name" "is_festivized" }
            }
            "prefabs"
            {
                "festive" { "static_attrs" { "is_festivized" "1" } }
            }
            "items"
            {
                "30" { "prefab" "festive" }
                "31" { "static_attrs" { "set item tint rgb" "15185723" "set item tint rgb 2" "3893479" } }
            }
        }
        """;

    [Test]
    public void PaintFor_AnUnpaintedItem_IsNull()
    {
        Weapons().PaintFor(Prop(UnpaintedItem), team: SceneTeams.Red).ShouldBeNull();
    }

    /// <remarks>The control for the test above: the early return for an unpainted item must not swallow a painted one.</remarks>
    [Test]
    public void PaintFor_APaintedItem_IsItsColourOnRedAndItsSecondOnBlu()
    {
        WeaponModels weapons = Weapons();

        weapons.PaintFor(Prop(PaintedItem), team: SceneTeams.Red).ShouldBe(Channels(TwoTonePaint));
        weapons.PaintFor(Prop(PaintedItem), team: SceneTeams.Blu).ShouldBe(Channels(TwoToneSecond));
    }

    /// <remarks>
    /// **A wire value overrides the definition's** — branch 1 — so an unpainted definition painted on the wire is painted,
    /// which is the path the early return reads before anything else.
    /// </remarks>
    [Test]
    public void PaintFor_AnUnpaintedDefinitionPaintedOnTheWire_IsTheWiresColour()
    {
        SceneProp painted = Prop(UnpaintedItem) with
        {
            Econ = new EconAttributeWire(
                [new EconAttributeValue(PaintIndex, BitConverter.SingleToInt32Bits(TwoTonePaint))], [], HasValidItemId: false),
        };

        Weapons().PaintFor(painted, team: SceneTeams.Red).ShouldBe(Channels(TwoTonePaint));
    }

    [Test]
    public void IsFestivized_ThroughTheItemsPrefab_IsTrueAndOtherwiseFalse()
    {
        WeaponModels weapons = Weapons();

        weapons.IsFestivized(Prop(UnpaintedItem)).ShouldBeTrue();
        weapons.IsFestivized(Prop(PaintedItem)).ShouldBeFalse();
    }

    /// <remarks>
    /// **The decision this branch made, tested as the decision:** the per-prop questions allocate nothing once warm.
    /// Each is asked once first (the schema load and the definition's first build are one-time), then all three again.
    /// </remarks>
    [Test]
    public void AttributeQueries_AskedWarmForAnUnpaintedItem_AllocateNothing()
    {
        WeaponModels weapons = Weapons();
        SceneProp prop = Prop(UnpaintedItem) with
        {
            Econ = new EconAttributeWire([new EconAttributeValue(FestiveIndex, 0)], [new EconAttributeValue(7, 1)], HasValidItemId: true),
        };

        int Ask()
        {
            int seen = 0;

            foreach (EconAttributeValue attribute in weapons.AttributesFor(prop))
            {
                seen += attribute.DefinitionIndex;
            }

            return seen + (weapons.IsFestivized(prop) ? 1 : 0) + (weapons.PaintFor(prop, SceneTeams.Red) is null ? 0 : 1);
        }

        int first = Ask();
        long before = GC.GetAllocatedBytesForCurrentThread();
        int again = Ask();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        // The wire's local 0 overrides the prefab's festive 1; the demos list (valid id) forecloses the definition.
        first.ShouldBe(FestiveIndex + 7);
        again.ShouldBe(first);
        bytes.ShouldBe(0L);
    }

    private static WeaponModels Weapons() => new(_ => Encoding.UTF8.GetBytes(Schema), NullLogger.Instance);

    private static SceneProp Prop(int item) =>
        new SceneProp(9, "models/player/items/all_class/hat.mdl", SceneModelKind.Studio, new ScenePose()) with
        {
            ItemDefinitionIndex = item,
        };

    private static (float, float, float) Channels(int packed) =>
        (((packed >> 16) & 0xFF) / 255f, ((packed >> 8) & 0xFF) / 255f, (packed & 0xFF) / 255f);
}
