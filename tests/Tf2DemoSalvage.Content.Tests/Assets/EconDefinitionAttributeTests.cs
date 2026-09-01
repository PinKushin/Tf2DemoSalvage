using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The attributes an item DEFINITION carries — <c>IterateAttributes</c>' branch 4.
/// </summary>
/// <remarks>
/// **Two shipped forms, counted in the real file:** the named block
/// (<c>"attributes" { "kill eater score type" { "attribute_class" … "value" "64" } }</c>) and the
/// flat pair (<c>"static_attrs" { "cosmetic_allow_inspect" "1" }</c>, 840 of them). Both feed the
/// same iterator in the engine, so both land in one list here.
///
/// **Both are keyed by attribute NAME, and the wire is keyed by INDEX** — the top-level
/// <c>attributes</c> section is the bridge, mapping <c>"2053" { "name" "is_festivized" }</c>. A
/// name the section does not know resolves to nothing rather than to a guessed index.
///
/// **The value string's meaning depends on the definition's <c>stored_as_integer</c>.** The union
/// holds 32 raw bits; a float attribute's <c>"1.2"</c> becomes the float's bits, an integer
/// attribute's <c>"64"</c> becomes the integer itself — and reading one as the other produces a
/// denormal, not a number, which is why <see cref="EconAttributeValue"/> stores bits.
/// </remarks>
public sealed class EconDefinitionAttributeTests
{
    private const string Schema = """
        "items_game"
        {
            "attributes"
            {
                "2"
                {
                    "name" "damage bonus"
                }
                "100"
                {
                    "name" "kill eater score type"
                    "stored_as_integer" "1"
                }
                "2053"
                {
                    "name" "is_festivized"
                }
            }
            "prefabs"
            {
                "weapon_base"
                {
                    "attributes"
                    {
                        "damage bonus"
                        {
                            "attribute_class" "mult_dmg"
                            "value" "1.1"
                        }
                    }
                }
            }
            "items"
            {
                "500"
                {
                    "prefab" "weapon_base"
                    "attributes"
                    {
                        "kill eater score type"
                        {
                            "attribute_class" "kill_eater_score_type"
                            "value" "64"
                        }
                    }
                    "static_attrs"
                    {
                        "is_festivized" "1"
                    }
                }
                "501"
                {
                    "prefab" "weapon_base"
                    "attributes"
                    {
                        "damage bonus"
                        {
                            "attribute_class" "mult_dmg"
                            "value" "1.5"
                        }
                    }
                }
                "502"
                {
                    "static_attrs"
                    {
                        "no such attribute" "7"
                    }
                }
            }
        }
        """;

    [Test]
    public void AttributeDefinitionIndex_ForANamedAttribute_AnswersTheSectionsIndex()
    {
        // The bridge itself: `GetAttributeDefinitionByName`. Everything else here rides on it, and
        // hardcoding 2053 at a consumer would survive a schema that renumbers.
        Read().AttributeDefinitionIndex("is_festivized").ShouldBe(2053);
        Read().AttributeDefinitionIndex("no such attribute").ShouldBeNull();
    }

    [Test]
    public void DefinitionAttributes_BothForms_LandInOneList()
    {
        // Item 500: a named block (integer-stored), a static_attrs pair, and an inherited float.
        System.Collections.Generic.IReadOnlyList<EconAttributeValue> found =
            Read().DefinitionAttributesFor(500);

        found.Count.ShouldBe(3);

        // stored_as_integer: the bits ARE the integer.
        found.Single(attribute => attribute.DefinitionIndex == 100).RawBits.ShouldBe(64);

        // A float attribute: the bits are the float's.
        found.Single(attribute => attribute.DefinitionIndex == 2053).Value.ShouldBe(1f);

        // Inherited through the prefab, like every other item field.
        found.Single(attribute => attribute.DefinitionIndex == 2).Value.ShouldBe(1.1f, 0.0001f);
    }

    [Test]
    public void DefinitionAttributes_AnItemRestatingAPrefabsAttribute_Overrides()
    {
        // KeyValues prefab merging is per-key with the item nearest: item 501 restates
        // `damage bonus` and its 1.5 must beat the prefab's 1.1 — one entry, not two.
        Read().DefinitionAttributesFor(501)
            .Single(attribute => attribute.DefinitionIndex == 2)
            .Value.ShouldBe(1.5f, 0.0001f);
    }

    [Test]
    public void DefinitionAttributes_ANameTheSectionDoesNotKnow_IsSkipped()
    {
        // The honest failure for a name with no index: nothing, rather than a guessed index that
        // would collide with a real attribute somewhere.
        Read().DefinitionAttributesFor(502).ShouldBeEmpty();
    }

    private static ItemSchema Read() => ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));
}
