using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CEconItemView::GetItemStyle` (econ_item_view.cpp:731-784) on a synthetic schema.</summary>
public sealed class TfItemViewStyleConformanceTests
{
    private const int StyleOverride = 542;
    private const int StrangeStyle = 2016;
    private const int KillEater = 214;
    private const int KillEaterType = 292;

    private const string Schema = """
        "items_game"
        {
            "attributes"
            {
                "542" { "name" "item style override" }
                "2016" { "name" "style changes on strange level" "stored_as_integer" "1" }
                "214" { "name" "kill eater" "stored_as_integer" "1" }
                "292" { "name" "kill eater score type" }
            }
            "item_levels"
            {
                "KillEaterRank" { "0" { "score" "10" } "1" { "score" "25" } "2" { "score" "45" } "3" { "score" "70" } }
                "Other" { "0" { "score" "1" } "1" { "score" "2" } }
            }
            "kill_eater_score_types" { "0" { } "7" { "level_data" "Other" } }
        }
        """;

    private static readonly ItemSchema Items = ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));

    private static TfItemView View(params EconAttributeValue[] attributes)
    {
        Dictionary<int, EconAttributeValue> byIndex = [];

        foreach (EconAttributeValue attribute in attributes)
        {
            byIndex[attribute.DefinitionIndex] = attribute;
        }

        return new TfItemView(1, 11, byIndex);
    }

    private static EconAttributeValue Float(int index, float value) => new(index, BitConverter.SingleToInt32Bits(value));

    [Test]
    public void Style_TheOverrideAttribute_WinsOverStrangeLevels()
    {
        View(Float(StyleOverride, 1f), new EconAttributeValue(StrangeStyle, 3)).Style(Items).ShouldBe(1);
    }

    [Test]
    public void Style_StrangeLevel_IsTheScoresLevelCappedAtTheAttribute()
    {
        // Score 30 in KillEaterRank: below 45, so level 2; capped at 3 it stays 2, capped at 1 it is 1 (:775).
        View(new EconAttributeValue(StrangeStyle, 3), new EconAttributeValue(KillEater, 30)).Style(Items).ShouldBe(2);
        View(new EconAttributeValue(StrangeStyle, 1), new EconAttributeValue(KillEater, 30)).Style(Items).ShouldBe(1);
    }

    [Test]
    public void Style_StrangeLevelOfAnotherScoreType_ReadsThatTypesLevels()
    {
        // Score type 7 levels by "Other": score 1 is below 2, level 1 (:760-771).
        View(new EconAttributeValue(StrangeStyle, 9), new EconAttributeValue(KillEater, 1), Float(KillEaterType, 7f))
            .Style(Items).ShouldBe(1);
    }

    [Test]
    public void Style_StrangeWithoutAScore_IsZero_AndNeitherAttributeIsNoStyle()
    {
        View(new EconAttributeValue(StrangeStyle, 3)).Style(Items).ShouldBe(0, "no kill eater score (:753-754)");
        View().Style(Items).ShouldBeNull("INVALID_STYLE_INDEX: no inventory in a demo (:779-783)");
    }
}
