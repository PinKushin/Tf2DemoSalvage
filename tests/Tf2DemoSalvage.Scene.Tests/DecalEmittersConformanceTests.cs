using System.Collections.Generic;
using System.Text;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CDecalEmitterSystem` (`game/shared/decals.cpp`), over `scripts/decals_subrect.txt` (B415).</summary>
public sealed class DecalEmittersConformanceTests
{
    /// <summary>The shape TF2 ships: a translation block, then weighted groups.</summary>
    private const string Script = """
        "TranslationData"
        {
            "-"     ""  // don't decal this surface
            "C"     "Impact.Concrete"
            "M"     "Impact.Metal"
            "V"     "Impact.Metal"
            "Q"     "Impact.Nothing"
        }
        "Impact.Concrete"
        {
            "decals/concrete/shot1_subrect" "1"
            "decals/concrete/shot2_subrect" "1"
        }
        "Impact.Metal"
        {
            "decals/metal/shot1_subrect" "1"
            "decals/metal/shot2_subrect" "3"
        }
        """;

    private static DecalEmitters Emitters() => DecalEmitters.Parse(Encoding.UTF8.GetBytes(Script));

    [Test]
    public void Translate_Concrete_KeepsTheName()
    {
        // `if ( gamematerial == CHAR_TEX_CONCRETE ) return decalName;` — before the table is consulted.
        Emitters().Translate("Impact.Concrete", 'C').ShouldBe("Impact.Concrete");
    }

    [Test]
    public void Translate_ImpactConcreteOnMetal_IsTheTablesGroup()
    {
        Emitters().Translate("Impact.Concrete", 'V').ShouldBe("Impact.Metal");
    }

    [Test]
    public void Translate_AMaterialMarkedDash_IsNoDecal()
    {
        Emitters().Translate("Impact.Concrete", '-').ShouldBe(string.Empty);
    }

    [Test]
    public void Translate_AMaterialTheTableLacks_KeepsTheName()
    {
        Emitters().Translate("Impact.Concrete", 'W').ShouldBe("Impact.Concrete");
    }

    [Test]
    public void Translate_AnotherDecalName_IsNeverTranslated()
    {
        // Only "Impact.Concrete" goes through the table; any other name comes back as it was asked for.
        Emitters().Translate("Scorch", 'M').ShouldBe("Scorch");
    }

    [Test]
    public void Translate_AnEntryNamingAnUnknownGroup_IsDroppedAtLoad()
    {
        // `Msg( "...references unknown decal..." )` and no insert, so 'Q' falls through to the untranslated name.
        Emitters().Translate("Impact.Concrete", 'Q').ShouldBe("Impact.Concrete");
    }

    [Test]
    public void Pick_WhenEveryDrawIsBelowTheWeight_IsTheLastEntry()
    {
        // `if ( RandomFloat( 0, totalweight ) < item->weight ) slot = idx;` — a draw of zero replaces every time.
        Emitters().Pick("Impact.Metal", (_, _) => 0f).ShouldBe("decals/metal/shot2_subrect");
    }

    [Test]
    public void Pick_WhenNoLaterDrawReplaces_IsTheFirstEntry()
    {
        // The first entry is taken while the running total is still zero, whatever the draw.
        Emitters().Pick("Impact.Metal", (_, most) => most).ShouldBe("decals/metal/shot1_subrect");
    }

    [Test]
    public void Pick_ADrawEqualToTheWeight_DoesNotReplace()
    {
        // Strictly below: the second entry's weight is 3, and a draw of exactly 3 leaves the first in place.
        Emitters().Pick("Impact.Metal", (_, most) => most - 1f).ShouldBe("decals/metal/shot1_subrect");
    }

    [Test]
    public void Pick_DrawsOverTheRunningTotal()
    {
        List<(float Least, float Most)> draws = [];

        Emitters().Pick("Impact.Metal", (least, most) =>
        {
            draws.Add((least, most));

            return most;
        });

        draws.ShouldBe([(0f, 1f), (0f, 4f)]);
    }

    [Test]
    public void Pick_AnUnknownOrEmptyName_IsNone()
    {
        Emitters().Pick("Impact.Glass", (_, _) => 0f).ShouldBeNull();
        Emitters().Pick(string.Empty, (_, _) => 0f).ShouldBeNull();
    }
}
