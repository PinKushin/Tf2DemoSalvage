using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`GenerateLocalizedFullItemName` (econ_item_description.cpp:609) for an item with no attributes.</summary>
/// <remarks>
/// Item 29 is the stock medigun, prefab-named and not proper; 35 "The Kritzkrieg" is proper; 411 has a token with no string.
/// The strings mimic english: a quality format that puts the quality before the name.
/// </remarks>
public sealed class TfItemNameConformanceTests
{
    [Test]
    public void Generate_UniqueProperName_PrependsTheArticle() =>
        Name(35, Unique).ShouldBe("The Kritzkrieg");

    [Test]
    public void Generate_UniqueNotProper_IsTheBareName() =>
        Name(29, Unique).ShouldBe("Medi Gun");

    [Test]
    public void Generate_Strange_PrefixesTheQualityAndSpacer() =>
        Name(35, 11).ShouldBe("[Strange Kritzkrieg");

    [Test]
    public void Generate_Vintage_UsesTheQualityFormat() =>
        Name(29, 3).ShouldBe("[Vintage Medi Gun");

    [Test]
    public void Generate_Normal_HasNoQualityAndNoArticle() =>
        Name(35, 0).ShouldBe("Kritzkrieg");

    [Test]
    public void Generate_ATokenWithNoString_UsesTheTokenRaw() =>
        Name(411, Unique).ShouldBe("#TF_Missing");

    [Test]
    public void Generate_NoDefinition_IsEmpty() =>
        Name(9999, Unique).ShouldBe(string.Empty);

    private const int Unique = 6;

    private static string Name(int definition, int quality)
    {
        ItemSchema schema = ItemSchema.Read(Encoding.UTF8.GetBytes("""
            "items_game"
            {
                "prefabs" { "medigun" { "item_name" "#TF_Weapon_Medigun" } }
                "items"
                {
                    "29" { "name" "TF_WEAPON_MEDIGUN" "prefab" "medigun" }
                    "35" { "name" "The Kritzkrieg" "item_name" "#TF_Unique_Kritzkrieg" "propername" "1" }
                    "411" { "name" "Missing" "item_name" "#TF_Missing" }
                }
            }
            """));
        Dictionary<string, string> strings = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["ItemNameFormat"] = "%s1%s2",
            ["ItemNameNormalOrUniqueQualityFormat"] = "%s1",
            ["ItemNameQualityFormat"] = "[%s1",
            ["TF_Unique_Prepend_Proper"] = "The ",
            ["Rarity_Spacer"] = " ",
            ["strange"] = "Strange",
            ["vintage"] = "Vintage",
            ["TF_Weapon_Medigun"] = "Medi Gun",
            ["TF_Unique_Kritzkrieg"] = "Kritzkrieg",
        };

        return TfItemName.Generate(schema, definition, quality, strings.GetValueOrDefault);
    }
}
