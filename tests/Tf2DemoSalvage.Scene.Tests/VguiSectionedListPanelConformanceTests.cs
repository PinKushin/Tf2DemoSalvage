using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`vgui::SectionedListPanel` (vgui2/vgui_controls/SectionedListPanel.cpp): sections, columns, items and paint.</summary>
/// <remarks>
/// Against <see cref="TextRecorder"/>: every glyph 10 wide ('.' 4), a font 12 tall. `GetSectionTall` is the header font's
/// height plus <c>BUTTON_HEIGHT_SPACER</c> (7) — 19 here — and the default row height is <c>DEFAULT_LINE_SPACING</c> (20).
/// </remarks>
public sealed class VguiSectionedListPanelConformanceTests
{
    [Test]
    public void Paint_OneSectionOneItem_DrawsTheHeaderThenTheRow()
    {
        VguiSectionedListPanel list = Build(out TextRecorder surface);

        list.AddSection(1, "players");
        list.AddColumnToSection(1, "name", "Name", 0, 60);
        int itemId = list.AddItem(1, new Dictionary<string, string> { ["name"] = "ab" });

        list.Paint(surface, null!);

        // Header at y=5, tall 19: text centred at (19-12)/2 = 3, so y = 8. Row at y = 5+19 = 24, tall 20: (20-12)/2 = 4, so y = 28.
        string.Concat(surface.Glyphs.Select(glyph => glyph[0])).ShouldBe("Nameab");
        surface.Glyphs[0].ShouldBe("N@5,8");
        surface.Glyphs[^2].ShouldBe("a@11,28");
        list.GetItemData(itemId)?["name"].ShouldBe("ab");
    }

    [Test]
    public void Paint_TwoColumns_OffsetsTheSecondByTheFirstsWidth()
    {
        VguiSectionedListPanel list = Build(out TextRecorder surface);

        list.AddSection(1, "s");
        list.AddColumnToSection(1, "a", "A", 0, 40);
        list.AddColumnToSection(1, "b", "B", 0, 40);
        list.AddItem(1, new Dictionary<string, string> { ["a"] = "x", ["b"] = "y" });

        list.Paint(surface, null!);

        // Row cells: column 0 gets the first-column indent (6); column 1 starts flush at xpos 40.
        surface.Glyphs.Where(g => g[0] is 'x' or 'y').ShouldBe(["x@11,28", "y@45,28"]);
    }

    [Test]
    public void SetItemFgColor_Overrides_DrawsInThatColour()
    {
        VguiSectionedListPanel list = Build(out TextRecorder surface);

        list.AddSection(1, "s");
        list.AddColumnToSection(1, "a", "A", 0, 40);
        int itemId = list.AddItem(1, new Dictionary<string, string> { ["a"] = "x" });
        list.SetItemFgColor(itemId, (10, 20, 30, 255));

        list.Paint(surface, null!);

        surface.Calls.ShouldContain("text color 10 20 30 255");
    }

    [Test]
    public void SetItemFont_Overrides_PaintsTheRowInThatFont()
    {
        VguiSectionedListPanel list = Build(out TextRecorder surface);
        VguiFontAmalgam itemFont = new();

        list.AddSection(1, "s");
        list.AddColumnToSection(1, "a", "A", 0, 40);
        int itemId = list.AddItem(1, new Dictionary<string, string> { ["a"] = "x" });
        list.SetItemFont(itemId, itemFont);

        list.Paint(surface, null!);

        // The header paints first (list.HeaderFont), then the row: the last font set is the item's own.
        surface.FontsUsed[^1].ShouldBeSameAs(itemFont);
    }

    [Test]
    public void SetSelectedItem_Selected_PaintsTheOutOfFocusSelectedBackgroundInsteadOfTheItemsOwn()
    {
        // `CItemButton::PaintBackground` (SectionedListPanel.cpp:511-533): selected and not in edit mode always
        // overrides the row's own background — `m_ArmedBgColor` while focused, `m_SelectionBG2Color` otherwise.
        // The scoreboard never has real vgui focus in this viewer (see remarks), so the out-of-focus colour is the
        // only branch that ever paints here.
        VguiSectionedListPanel list = Build(out TextRecorder surface);
        list.OutOfFocusSelectedBgColor = (9, 8, 7, 255);

        list.AddSection(1, "s");
        list.AddColumnToSection(1, "a", "A", 0, 40);
        int itemId = list.AddItem(1, new Dictionary<string, string> { ["a"] = "x" });
        list.SetItemBgColor(itemId, (1, 2, 3, 80));
        list.SetSelectedItem(itemId);

        list.Paint(surface, null!);

        surface.Calls.ShouldContain("color 9 8 7 255");
        surface.Calls.ShouldNotContain("color 1 2 3 80");
    }

    [Test]
    public void AddColumnToSection_ColumnRight_RightAlignsTheText()
    {
        VguiSectionedListPanel list = Build(out TextRecorder surface);

        // The first column always takes the indent branch in Valve's CItemButton::PerformLayout (:450), so COLUMN_RIGHT
        // needs a second column to show its effect.
        list.AddSection(1, "s");
        list.AddColumnToSection(1, "a", "A", 0, 40);
        list.AddColumnToSection(1, "b", "B", SectionedListColumn.ColumnRight, 40);
        list.AddItem(1, new Dictionary<string, string> { ["a"] = "-", ["b"] = "x" });

        list.Paint(surface, null!);

        // Column b starts at xpos 40, wide 40, text width 10 ('x'): right edge is panel x (5) + 40 + 40 - 10 = 75.
        surface.Glyphs.Single(g => g[0] == 'x').ShouldBe("x@75,28");
    }

    [Test]
    public void Paint_ASecondSection_DrawsItBelowTheFirst()
    {
        VguiSectionedListPanel list = Build(out TextRecorder surface);

        list.AddSection(1, "top");
        list.AddColumnToSection(1, "a", "Top", 0, 40);
        list.AddItem(1, new Dictionary<string, string> { ["a"] = "p" });

        list.AddSection(2, "bottom");
        list.AddColumnToSection(2, "a", "Bot", 0, 40);
        list.AddItem(2, new Dictionary<string, string> { ["a"] = "q" });

        list.Paint(surface, null!);

        // Section 1: header y=5, row y=24 (5+19). Section 2 header: previous row bottom (24+20=44) + section gap (8) = 52;
        // its row at 52+19=71.
        surface.Glyphs.Single(g => g[0] == 'q').ShouldBe("q@11,75");
    }

    [Test]
    public void SetSectionAlwaysVisible_WithNoItems_StillDrawsTheHeader()
    {
        VguiSectionedListPanel list = Build(out TextRecorder surface);

        list.AddSection(1, "empty");
        list.AddColumnToSection(1, "a", "Empty", 0, 60);
        list.SetSectionAlwaysVisible(1, true);

        list.Paint(surface, null!);

        string.Concat(surface.Glyphs.Select(glyph => glyph[0])).ShouldBe("Empty");
    }

    [Test]
    public void RemoveAllSections_ThenPaint_DrawsNothing()
    {
        VguiSectionedListPanel list = Build(out TextRecorder surface);

        list.AddSection(1, "s");
        list.AddColumnToSection(1, "a", "A", 0, 40);
        list.AddItem(1, new Dictionary<string, string> { ["a"] = "x" });
        list.RemoveAllSections();

        list.Paint(surface, null!);

        surface.Glyphs.ShouldBeEmpty();
    }

    [Test]
    public void ClearItems_ThenPaint_DrawsOnlyTheHeader()
    {
        VguiSectionedListPanel list = Build(out TextRecorder surface);

        // A section with no items draws no header unless it is always-visible (LayoutPanels :976): clearing the items
        // means Paint has nothing left to show unless that is set first.
        list.AddSection(1, "s");
        list.AddColumnToSection(1, "a", "A", 0, 40);
        list.SetSectionAlwaysVisible(1, true);
        list.AddItem(1, new Dictionary<string, string> { ["a"] = "x" });
        list.ClearItems();

        list.Paint(surface, null!);

        string.Concat(surface.Glyphs.Select(glyph => glyph[0])).ShouldBe("A");
    }

    [Test]
    public void AddSection_WithSortFunc_PaintsItemsInSortedOrder()
    {
        VguiSectionedListPanel list = Build(out TextRecorder surface);

        list.AddSection(1, "s", (panel, id1, id2) =>
            string.CompareOrdinal(panel.GetItemData(id1)!["a"], panel.GetItemData(id2)!["a"]) < 0);
        list.AddColumnToSection(1, "a", "A", 0, 40);

        list.AddItem(1, new Dictionary<string, string> { ["a"] = "30" });
        list.AddItem(1, new Dictionary<string, string> { ["a"] = "10" });
        list.AddItem(1, new Dictionary<string, string> { ["a"] = "20" });

        list.Paint(surface, null!);

        // Ascending by "a" (10, 20, 30), not insertion order (30, 10, 20) — skip the single-glyph "A" header.
        string.Concat(surface.Glyphs.Skip(1).Select(glyph => glyph[0])).ShouldBe("102030");
    }

    [Test]
    public void AddSection_WithNoSortFunc_PaintsItemsInInsertionOrder()
    {
        VguiSectionedListPanel list = Build(out TextRecorder surface);

        list.AddSection(1, "s");
        list.AddColumnToSection(1, "a", "A", 0, 40);

        list.AddItem(1, new Dictionary<string, string> { ["a"] = "30" });
        list.AddItem(1, new Dictionary<string, string> { ["a"] = "10" });
        list.AddItem(1, new Dictionary<string, string> { ["a"] = "20" });

        list.Paint(surface, null!);

        string.Concat(surface.Glyphs.Skip(1).Select(glyph => glyph[0])).ShouldBe("301020");
    }

    [Test]
    public void Paint_ACellWiderThanItsColumn_EndsInAnEllipsis()
    {
        VguiSectionedListPanel list = Build(out TextRecorder surface);

        list.AddSection(1, "s");
        list.AddColumnToSection(1, "a", "A", 0, 10);
        list.AddColumnToSection(1, "b", "B", 0, 40);
        list.AddItem(1, new Dictionary<string, string> { ["a"] = "-", ["b"] = "abcdefghij" });

        list.Paint(surface, null!);

        // Column b: width 40 less the 2px gap is a draw width of 38. "ab" (20 wide) fits; adding "c" plus the ellipsis
        // (10 + 10 + 12 = 32 <= 38, but the next check at "c" itself: 20 + 10 + 12 = 42 > 38) and the remaining text
        // still doesn't fit beside it, so VguiTextImage's RecalculateEllipsesPosition truncates after "ab".
        surface.Glyphs[^5..].ShouldBe(["a@15,28", "b@25,28", ".@35,28", ".@39,28", ".@43,28"]);
    }

    [Test]
    public void Paint_ACentredCellNearlyItsColumnsWidth_LosesTheOffsetFromItsDrawWidth()
    {
        VguiSectionedListPanel list = Build(out TextRecorder surface);

        list.AddSection(1, "s");
        list.AddColumnToSection(1, "a", "A", 0, 40);
        list.AddColumnToSection(1, "b", "B", SectionedListColumn.ColumnCenter, 100);
        list.AddItem(1, new Dictionary<string, string> { ["a"] = "-", ["b"] = "aaaaaaaaa.." });

        list.Paint(surface, null!);

        // 98 wide: offset (100 / 2) - (98 / 2) = 1, bound 100 - 1 - 2 = 97 (CItemButton::PerformLayout :459-:460), so the
        // text no longer fits and truncates, starting at x = 5 + 40 + 1.
        List<string> cell = [.. surface.Glyphs.SkipWhile(g => g[0] != '-').Skip(1)];
        string.Concat(cell.Select(glyph => glyph[0])).ShouldBe("aaaaaaaa...");
        cell[0].ShouldBe("a@46,28");
    }

    [Test]
    public void Paint_AHeaderBesideABlankOne_DrawsOverIt()
    {
        VguiSectionedListPanel list = Build(out TextRecorder surface);

        // "Name" is 40 wide in a 10-wide column; the next header is empty, so its 60 joins the bound (:163-:180): 68.
        list.AddSection(1, "s");
        list.AddColumnToSection(1, "a", "Name", 0, 10);
        list.AddColumnToSection(1, "b", string.Empty, 0, 60);
        list.SetSectionAlwaysVisible(1, true);

        list.Paint(surface, null!);

        string.Concat(surface.Glyphs.Select(glyph => glyph[0])).ShouldBe("Name");
    }

    private static VguiSectionedListPanel Build(out TextRecorder surface)
    {
        TextRecorder recorder = new();
        surface = recorder;

        VguiSectionedListPanel list = new(null, "t") { Wide = 200, Tall = 200 };
        VguiFontAmalgam font = new();

        list.HeaderFont = font;
        list.RowFont = font;
        list.HeaderTextColor = (255, 255, 255, 255);
        list.RowTextColor = (255, 255, 255, 255);

        return list;
    }
}
