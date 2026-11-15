using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>The HUD's <c>CEconItemView</c>: an item's attributes as <c>FindAttribute</c> answers them.</summary>
public sealed class TfItemViewConformanceTests
{
    private const string Schema = """
        "items_game"
        {
            "attributes" { "142" { "name" "set item tint rgb" } }
            "items" { "40" { "name" "hat" } }
        }
        """;

    /// <remarks>
    /// **A definition named twice takes the first** (B612 review): the walk lets it through twice, as the engine's
    /// `IterateAttributes` does, and `FindAttribute`'s typed iterator stops at the first match
    /// (<c>econ_item_interface.h:320-337</c>). A last-wins map answered the second.
    /// </remarks>
    [Test]
    public void From_AnAttributeNamedTwiceOnTheWire_AnswersTheFirst()
    {
        ItemSchema schema = ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));
        SceneItem item = new(
            535,
            "CTFWearable",
            40,
            new EconAttributeWire([new EconAttributeValue(142, 111), new EconAttributeValue(142, 222)], [], false),
            IsWeapon: false);

        TfItemView.From(item, schema).ShouldNotBeNull().Attribute(schema, "set item tint rgb")
            .ShouldBe(new EconAttributeValue(142, 111));
    }
}
