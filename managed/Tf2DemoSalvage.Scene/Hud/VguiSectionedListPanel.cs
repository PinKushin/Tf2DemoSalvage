using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`SectionedListPanel::column_t`'s `columnFlags` (SectionedListPanel.h:61).</summary>
[Flags]
public enum SectionedListColumn
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>`HEADER_IMAGE`: the header for this column is an image, not text.</summary>
    HeaderImage = 0x01,

    /// <summary>`COLUMN_IMAGE`: the cell is an image, looked up by index from the image list.</summary>
    ColumnImage = 0x02,

    /// <summary>`COLUMN_BRIGHT`: the cell text is the bright colour.</summary>
    ColumnBright = 0x04,

    /// <summary>`COLUMN_CENTER`: the cell is centred in the column.</summary>
    ColumnCenter = 0x08,

    /// <summary>`COLUMN_RIGHT`: the cell is right-aligned in the column.</summary>
    ColumnRight = 0x10,
}

/// <summary>`SectionSortFunc_t` (SectionedListPanel.h:30): `bool (*)(SectionedListPanel *list, int itemID1, int itemID2)`.</summary>
/// <param name="list">The panel — Valve's callback reads other items through it, e.g. <see cref="VguiSectionedListPanel.GetItemData"/>.</param>
/// <param name="itemId1">The item being inserted.</param>
/// <param name="itemId2">The item already placed that it is being compared against.</param>
/// <returns>Whether <paramref name="itemId1"/> belongs before <paramref name="itemId2"/>.</returns>
public delegate bool SectionedListSortFunc(VguiSectionedListPanel list, int itemId1, int itemId2);

/// <summary>`vgui::SectionedListPanel` (vgui2/vgui_controls/SectionedListPanel.cpp): sections of columned rows, such as the scoreboard.</summary>
/// <remarks>
/// The drawing half, ported the way <see cref="VguiRichText"/> ports `RichText`: `AddSection` (:1195, :1204, :1213),
/// `AddColumnToSection` (:1249, :1264), `ModifyColumn` (:1287), `AddItem`/`ModifyItem` (:1315, :1330), `RemoveItem` (:1465),
/// `DeleteAllItems` (:1884), `SetItemFgColor`/`SetItemBgColor` (:1346, :1362), `SetSectionFgColor`/`SetSectionDividerColor`/
/// `SetSectionDrawDividerBar`/`SetSectionAlwaysVisible`/`SetSectionMinimumHeight` (:1407-:1460), `SetImageList` (:2160),
/// `GetSectionTall` (:2184), and the layout and paint that `LayoutPanels` (:942) and `CItemButton`/`SectionedListPanelHeader`'s
/// own `PerformLayout`/`Paint` (:107, :81, :297, :536) do between them for a row-and-column list with no real child panels.
/// `ReSortList` (:843): a lazy, per-section stable insertion sort — every item, in the order Valve's `m_Items` holds them
/// (here, the order <see cref="AddItem"/> gave them, since removal never reuses an ID), is inserted into the sorted list
/// just before the first existing item in its section for which the section's `SectionSortFunc_t` answers true, or at
/// the section's end when none does. It reruns, not incrementally, whenever `m_bSortNeeded` — `AddItem`, `ModifyItem`,
/// `RemoveItem`, `DeleteAllItems`, or a colour override, all of which can change what a comparison reads — is set, the
/// same way `PerformLayout` reruns it before `LayoutPanels` (:895). A section with no `sortFunc` keeps insertion order.
/// Each cell draws through a <see cref="VguiTextImage"/>, the way `CItemButton`/`SectionedListPanelHeader` draw through a
/// `TextImage` (:297-:470, :107-:180): `SetText`, `SetDrawWidth` to the column's content width, `SetPos`, `Paint` — so a
/// cell too wide for its column truncates with `TextImage`'s own ellipsis (`RecalculateEllipsesPosition`), not an
/// approximation of it. The alignment measures the image's *full, untruncated* width, as `TextImage::GetContentSize` does —
/// the ellipsis shortens what is drawn, never what centring or right-alignment measures.
/// **Not modelled:** mouse and keyboard selection, the edit mode and its sub-panel, the context and edit menus, drag and
/// drop, the scroll bar's own drawing (only its value offsets `y`, as `LayoutPanels` reads `m_pScrollBar->GetValue()`),
/// actual images (a `COLUMN_IMAGE` cell tracks an image index but draws nothing unless an image list is set and asked
/// for a texture), and per-column fallback fonts and colour overrides.
/// </remarks>
public class VguiSectionedListPanel : VguiPanel
{
    private const int ButtonHeightDefault = 20;
    private const int ButtonHeightSpacer = 7;
    private const int DefaultLineSpacing = 20;
    private const int DefaultSectionGap = 8;
    private const int ColumnDataIndent = 6;
    private const int ColumnDataGap = 2;

    private readonly List<Section> _sections = [];
    private readonly Dictionary<int, Item> _items = [];
    private readonly List<int> _itemOrder = [];
    private readonly List<Item> _sortedItems = [];
    private int _nextItemId;
    private bool _sortNeeded;
    private int _selectedItemId = -1;

    /// <summary>`SectionedListPanel( parent, name )`.</summary>
    /// <param name="parent">The parent, or null.</param>
    /// <param name="name">The panel's name, or null.</param>
    public VguiSectionedListPanel(VguiPanel? parent, string? name)
        : base(parent, name)
    {
        LineSpacing = DefaultLineSpacing;
        SectionGap = DefaultSectionGap;
    }

    /// <inheritdoc/>
    public override string ClassName => "SectionedListPanel";

    /// <summary>`m_iLineSpacing`.</summary>
    public int LineSpacing { get; set; }

    /// <summary>`m_iLineGap`.</summary>
    public int LineGap { get; set; }

    /// <summary>`m_iSectionGap`.</summary>
    public int SectionGap { get; set; }

    /// <summary>`m_bDrawSectionHeaders`, on by default.</summary>
    public bool DrawSectionHeaders { get; set; } = true;

    /// <summary>`m_pScrollBar`'s value: `LayoutPanels` subtracts it from the starting `y`.</summary>
    public int ScrollValue { get; set; }

    /// <summary>`SetHeaderFont` / `GetHeaderFont`: overrides the scheme's `"DefaultVerySmall"`.</summary>
    public VguiFontAmalgam? HeaderFont { get; set; }

    /// <summary>`SetRowFont` / `GetRowFont`: overrides the scheme's `SectionedListPanel.Font`.</summary>
    public VguiFontAmalgam? RowFont { get; set; }

    /// <summary>`SectionedListPanel.HeaderTextColor`, unless a section overrides it with <see cref="SetSectionFgColor"/>.</summary>
    public (byte Red, byte Green, byte Blue, byte Alpha) HeaderTextColor { get; set; } = (255, 255, 255, 255);

    /// <summary>`SectionedListPanel.DividerColor`, unless a section overrides it with <see cref="SetSectionDividerColor"/>.</summary>
    public (byte Red, byte Green, byte Blue, byte Alpha) DividerColor { get; set; }

    /// <summary>`SectionedListPanel.TextColor`: a row's default cell colour.</summary>
    public (byte Red, byte Green, byte Blue, byte Alpha) RowTextColor { get; set; } = (255, 255, 255, 255);

    /// <summary>`SectionedListPanel.BrightTextColor`: a `COLUMN_BRIGHT` cell's colour, unless the item overrides it.</summary>
    public (byte Red, byte Green, byte Blue, byte Alpha) BrightTextColor { get; set; } = (255, 255, 255, 255);

    /// <summary>
    /// `SectionedListPanel.OutOfFocusSelectedBgColor` (`CItemButton::PaintBackground`, SectionedListPanel.cpp:526):
    /// the selected row's background whenever it (or a descendant) does not have real vgui focus.
    /// </summary>
    /// <remarks>
    /// **`m_ArmedBgColor` (the focused branch) is not modelled.** This viewer never gives the scoreboard panel real
    /// input focus — see <see cref="VguiSectionedListPanel"/>'s own remarks and
    /// <c>TfClientScoreBoardDialog</c>'s — so `HasFocus()` is always false and `CItemButton::PaintBackground`'s
    /// `IsSelected() &amp;&amp; HasFocus()` branch never runs; only this colour ever paints a selected row.
    /// </remarks>
    public (byte Red, byte Green, byte Blue, byte Alpha) OutOfFocusSelectedBgColor { get; set; }

    /// <summary>`SetImageList`: only tracked so a `COLUMN_IMAGE` cell can be asked for its index; nothing is drawn from it.</summary>
    public object? ImageList { get; set; }

    /// <inheritdoc/>
    /// <remarks>`SectionedListPanel::ApplySchemeSettings` (:1091): the background colour, and each header's fonts and colours.</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        base.ApplySchemeSettings(context);
        BgColor = context.Scheme.GetColor("SectionedListPanel.BgColor", BgColor);
        HeaderTextColor = context.Scheme.GetColor("SectionedListPanel.HeaderTextColor", HeaderTextColor);
        DividerColor = context.Scheme.GetColor("SectionedListPanel.DividerColor", DividerColor);
        RowTextColor = context.Scheme.GetColor("SectionedListPanel.TextColor", RowTextColor);
        BrightTextColor = context.Scheme.GetColor("SectionedListPanel.BrightTextColor", BrightTextColor);
        OutOfFocusSelectedBgColor = context.Scheme.GetColor("SectionedListPanel.OutOfFocusSelectedBgColor", OutOfFocusSelectedBgColor);
        HeaderFont ??= context.GetFont("DefaultVerySmall", Proportional);

        if (RowFont is null && context.Scheme.GetResourceString("SectionedListPanel.Font") is { Length: > 0 } fontName)
        {
            RowFont = context.GetFont(fontName, Proportional);
        }
    }

    /// <summary>`AddSection`: a section keyed by <paramref name="sectionId"/>, holding its own columns.</summary>
    /// <param name="sectionId">The section's ID — not an index; sections are found by this value.</param>
    /// <param name="name">The header's name (Valve's `Panel` name; unused for drawing here).</param>
    /// <param name="sortFunc">`SectionSortFunc_t`: orders this section's items; null keeps insertion order.</param>
    public void AddSection(int sectionId, string name, SectionedListSortFunc? sortFunc = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        _sections.Add(new Section(sectionId, sortFunc));
        _sortNeeded = true;
    }

    /// <summary>`RemoveAllSections` (:1227): every section gone; items stay, but paint nothing until re-sectioned.</summary>
    public void RemoveAllSections()
    {
        _sections.Clear();
        _sortNeeded = true;
    }

    /// <summary>`AddColumnToSection` (:1264).</summary>
    /// <param name="sectionId">The section.</param>
    /// <param name="columnName">The key this column reads from an item's data.</param>
    /// <param name="columnText">The header text.</param>
    /// <param name="columnFlags">`HEADER_IMAGE`/`COLUMN_IMAGE`/`COLUMN_BRIGHT`/`COLUMN_CENTER`/`COLUMN_RIGHT`.</param>
    /// <param name="width">The column's width in pixels.</param>
    /// <returns>Whether the section exists.</returns>
    public bool AddColumnToSection(int sectionId, string columnName, string columnText, SectionedListColumn columnFlags, int width)
    {
        ArgumentNullException.ThrowIfNull(columnName);
        ArgumentNullException.ThrowIfNull(columnText);

        if (FindSection(sectionId) is not { } section)
        {
            return false;
        }

        section.Columns.Add(new Column(columnName, columnText, columnFlags, width));
        return true;
    }

    /// <summary>`ModifyColumn` (:1287): the header text of an existing column, by name.</summary>
    /// <param name="sectionId">The section.</param>
    /// <param name="columnName">The column, matched case-insensitively as Valve's `stricmp` does.</param>
    /// <param name="columnText">The new header text.</param>
    /// <returns>Whether the column was found.</returns>
    public bool ModifyColumn(int sectionId, string columnName, string columnText)
    {
        ArgumentNullException.ThrowIfNull(columnName);
        ArgumentNullException.ThrowIfNull(columnText);

        if (FindSection(sectionId) is not { } section)
        {
            return false;
        }

        int index = section.Columns.FindIndex(column => string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            return false;
        }

        section.Columns[index] = section.Columns[index] with { Text = columnText };
        return true;
    }

    /// <summary>`AddItem` (:1315): a new item, added to `m_Items` — its place in paint order awaits the next sort.</summary>
    /// <param name="sectionId">The section it belongs to.</param>
    /// <param name="data">Its cell data, keyed by column name.</param>
    /// <returns>The new item's ID.</returns>
    public int AddItem(int sectionId, IReadOnlyDictionary<string, string> data)
    {
        ArgumentNullException.ThrowIfNull(data);

        int itemId = _nextItemId++;

        _items[itemId] = new Item(itemId, sectionId, new Dictionary<string, string>(data, StringComparer.Ordinal));
        _itemOrder.Add(itemId);
        _sortNeeded = true;
        return itemId;
    }

    /// <summary>`ModifyItem` (:1330): an existing item's section and data.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="sectionId">Its (possibly new) section.</param>
    /// <param name="data">Its (replaced) cell data.</param>
    /// <returns>Whether the item exists.</returns>
    public bool ModifyItem(int itemId, int sectionId, IReadOnlyDictionary<string, string> data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (!_items.ContainsKey(itemId))
        {
            return false;
        }

        _items[itemId] = new Item(itemId, sectionId, new Dictionary<string, string>(data, StringComparer.Ordinal));
        _sortNeeded = true;
        return true;
    }

    /// <summary>`RemoveItem` (:1465).</summary>
    /// <param name="itemId">The item.</param>
    /// <returns>Whether it was found and removed.</returns>
    public bool RemoveItem(int itemId)
    {
        if (!_items.Remove(itemId))
        {
            return false;
        }

        _itemOrder.Remove(itemId);
        _sortNeeded = true;
        return true;
    }

    /// <summary>`DeleteAllItems` (:1884): every item gone; sections stay.</summary>
    public void ClearItems()
    {
        _items.Clear();
        _itemOrder.Clear();
        _sortNeeded = true;
    }

    /// <summary>`GetItemData` (:1954).</summary>
    /// <param name="itemId">The item.</param>
    /// <returns>Its data, or null when the item does not exist.</returns>
    public IReadOnlyDictionary<string, string>? GetItemData(int itemId) => _items.TryGetValue(itemId, out Item? item) ? item.Data : null;

    /// <summary>`GetItemSection` (:1966).</summary>
    /// <param name="itemId">The item.</param>
    /// <returns>Its section ID, or -1 when the item does not exist.</returns>
    public int GetItemSection(int itemId) => _items.TryGetValue(itemId, out Item? item) ? item.SectionId : -1;

    /// <summary>`IsItemIDValid` (:1977).</summary>
    /// <param name="itemId">The item.</param>
    public bool IsItemIdValid(int itemId) => _items.ContainsKey(itemId);

    /// <summary>`GetItemCount` (:1993).</summary>
    public int ItemCount => _items.Count;

    /// <summary>`SetItemFgColor` (:1346): overrides the row's default and bright colours for this item alone.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="color">The colour.</param>
    public void SetItemFgColor(int itemId, (byte Red, byte Green, byte Blue, byte Alpha) color) => SetItem(itemId, item => item with { FgColor = color });

    /// <summary>The per-item colour override <see cref="SetItemFgColor"/> set, or null when the item uses the row default.</summary>
    /// <param name="itemId">The item.</param>
    public (byte Red, byte Green, byte Blue, byte Alpha)? GetItemFgColor(int itemId) =>
        _items.TryGetValue(itemId, out Item? item) ? item.FgColor : null;

    /// <summary>The row background <see cref="SetItemBgColor"/> set, or null when the row draws none.</summary>
    /// <param name="itemId">The item.</param>
    public (byte Red, byte Green, byte Blue, byte Alpha)? GetItemBgColor(int itemId) =>
        _items.TryGetValue(itemId, out Item? item) ? item.BgColor : null;

    /// <summary>The per-item font override <see cref="SetItemFont"/> set, or null when the item uses the row default.</summary>
    /// <param name="itemId">The item.</param>
    public VguiFontAmalgam? GetItemFont(int itemId) => _items.TryGetValue(itemId, out Item? item) ? item.Font : null;

    /// <summary>`SetItemBgColor` (:1362): enables the row's own background fill.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="color">The colour.</param>
    public void SetItemBgColor(int itemId, (byte Red, byte Green, byte Blue, byte Alpha) color) => SetItem(itemId, item => item with { BgColor = color });

    /// <summary>`SetItemFont` (:1383-1390): overrides the row's font for this item alone.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="font">The font.</param>
    public void SetItemFont(int itemId, VguiFontAmalgam font)
    {
        ArgumentNullException.ThrowIfNull(font);

        SetItem(itemId, item => item with { Font = font });
    }

    /// <summary>
    /// `SetSelectedItem( int itemID )` (:1943-1948): the previous selection (if any) is simply overwritten, as
    /// `SetSelectedItem( CItemButton* )` does — there is only ever one.
    /// </summary>
    /// <param name="itemId">The item to select.</param>
    public void SetSelectedItem(int itemId)
    {
        if (_items.ContainsKey(itemId))
        {
            _selectedItemId = itemId;
        }
    }

    /// <summary>`GetSelectedItem` (:1931-1936): the selected item's ID, or -1 when nothing is selected.</summary>
    public int SelectedItem => _selectedItemId;

    /// <summary>`ClearSelection` (`SetSelectedItem( (CItemButton *)NULL )`, :1674).</summary>
    public void ClearSelection() => _selectedItemId = -1;

    /// <summary>`SetSectionFgColor` (:1407): a section's header text colour.</summary>
    /// <param name="sectionId">The section.</param>
    /// <param name="color">The colour.</param>
    public void SetSectionFgColor(int sectionId, (byte Red, byte Green, byte Blue, byte Alpha) color)
    {
        if (FindSection(sectionId) is { } section)
        {
            section.FgColor = color;
        }
    }

    /// <summary>`SetSectionDividerColor` (:1417).</summary>
    /// <param name="sectionId">The section.</param>
    /// <param name="color">The colour.</param>
    public void SetSectionDividerColor(int sectionId, (byte Red, byte Green, byte Blue, byte Alpha) color)
    {
        if (FindSection(sectionId) is { } section)
        {
            section.DividerColor = color;
        }
    }

    /// <summary>`SetSectionDrawDividerBar` (:1428).</summary>
    /// <param name="sectionId">The section.</param>
    /// <param name="draw">Whether the line under the header draws.</param>
    public void SetSectionDrawDividerBar(int sectionId, bool draw)
    {
        if (FindSection(sectionId) is { } section)
        {
            section.DrawDividerBar = draw;
        }
    }

    /// <summary>`SetSectionAlwaysVisible` (:1439): the header (and any minimum height) draws with no items.</summary>
    /// <param name="sectionId">The section.</param>
    /// <param name="visible">Whether it stays visible when empty.</param>
    public void SetSectionAlwaysVisible(int sectionId, bool visible)
    {
        if (FindSection(sectionId) is { } section)
        {
            section.AlwaysVisible = visible;
        }
    }

    /// <summary>`SetSectionMinimumHeight` (:1453).</summary>
    /// <param name="sectionId">The section.</param>
    /// <param name="minimumHeight">The minimum space it and its rows take before the next section starts.</param>
    public void SetSectionMinimumHeight(int sectionId, int minimumHeight)
    {
        if (FindSection(sectionId) is { } section)
        {
            section.MinimumHeight = minimumHeight;
        }
    }

    /// <summary>`GetSectionTall` (:2184): the first section's header font height plus `BUTTON_HEIGHT_SPACER` (7).</summary>
    /// <param name="surface">The surface the header font measures against.</param>
    /// <returns>The header row's height.</returns>
    public int GetSectionTall(IVguiSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        return HeaderFont is { } font ? surface.GetFontTall(font) + ButtonHeightSpacer : ButtonHeightDefault;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// `LayoutPanels` (:942) and each header's/item's own `PerformLayout`/`Paint`, collapsed into one pass since no
    /// sub-panels exist here: `ReSortList` first if anything changed since the last paint (`PerformLayout` :895), then
    /// each section's header (if any items are in it, or it is always visible) followed by its rows.
    /// </remarks>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (HeaderFont is null && RowFont is null)
        {
            return;
        }

        if (_sortNeeded)
        {
            ReSortList();
        }

        int sectionTall = GetSectionTall(surface);
        int x = 5;
        int wide = Wide - 10;
        int y = 5 - ScrollValue;
        bool firstVisibleSection = true;

        foreach (Section section in _sections)
        {
            List<Item> items = _sortedItems.FindAll(item => item.SectionId == section.Id);

            if (items.Count == 0 && !section.AlwaysVisible)
            {
                continue;
            }

            if (firstVisibleSection)
            {
                firstVisibleSection = false;
            }
            else
            {
                y += SectionGap;
            }

            int minimumNextY = y + section.MinimumHeight;

            if (DrawSectionHeaders)
            {
                PaintSectionHeader(surface, section, x, y, wide, sectionTall);
                y += sectionTall;
            }

            foreach (Item item in items)
            {
                PaintRow(surface, section, item, x, y, wide, LineSpacing);
                y += LineSpacing + LineGap;
            }

            if (y < minimumNextY)
            {
                y = minimumNextY;
            }
        }
    }

    /// <summary>`ReSortList` (:843): rebuilt from scratch, each item stably insertion-sorted into its section.</summary>
    private void ReSortList()
    {
        _sortedItems.Clear();

        foreach (Section section in _sections)
        {
            int sectionStart = _sortedItems.Count;

            foreach (int itemId in _itemOrder)
            {
                if (!_items.TryGetValue(itemId, out Item? item) || item.SectionId != section.Id)
                {
                    continue;
                }

                if (section.SortFunc is { } sortFunc)
                {
                    int insertionPoint = sectionStart;

                    while (insertionPoint < _sortedItems.Count && !sortFunc(this, itemId, _sortedItems[insertionPoint].Id))
                    {
                        insertionPoint++;
                    }

                    _sortedItems.Insert(insertionPoint, item);
                }
                else
                {
                    _sortedItems.Add(item);
                }
            }
        }

        _sortNeeded = false;
    }

    private void PaintSectionHeader(IVguiSurface surface, Section section, int x, int y, int wide, int tall)
    {
        if (HeaderFont is not { } font)
        {
            return;
        }

        (byte, byte, byte, byte) color = section.FgColor ?? HeaderTextColor;
        int textY = y + Math.Max(0, (tall - surface.GetFontTall(font)) / 2);
        int xpos = 0;

        // `SectionedListPanelHeader::PerformLayout` (:142-:203): no first-column indent and no centring; a left-aligned
        // header may draw over each later column whose header is blank (a `HEADER_IMAGE`'s null image, or empty text).
        for (int i = 0; i < section.Columns.Count; i++)
        {
            Column column = section.Columns[i];

            if (column.Flags.HasFlag(SectionedListColumn.HeaderImage))
            {
                xpos += column.Width;
                continue;
            }

            VguiTextImage image = CellImage(font, column.Text, color);
            int contentWide = image.GetContentSize(surface).Wide;
            int maxWidth = column.Width;

            if (!column.Flags.HasFlag(SectionedListColumn.ColumnRight))
            {
                for (int j = i + 1; j < section.Columns.Count; j++)
                {
                    Column next = section.Columns[j];

                    if (next.Flags.HasFlag(SectionedListColumn.HeaderImage) || CellImage(font, next.Text, color).GetContentSize(surface).Wide == 0)
                    {
                        maxWidth += next.Width;
                    }
                }
            }

            int cellX = column.Flags.HasFlag(SectionedListColumn.ColumnRight) ? xpos + maxWidth - contentWide : xpos;

            Draw(surface, image, x + cellX, textY, maxWidth - ColumnDataGap);
            xpos += column.Width;
        }

        (byte, byte, byte, byte) divider = section.DividerColor ?? DividerColor;

        if (section.DrawDividerBar)
        {
            surface.DrawSetColor(divider);
            surface.DrawFilledRect(x + 1, y + tall - 2, x + wide - 1, y + tall - 1);
        }
    }

    private void PaintRow(IVguiSurface surface, Section section, Item item, int x, int y, int wide, int tall)
    {
        VguiFontAmalgam? rowFont = item.Font ?? RowFont;

        if (rowFont is not { } font)
        {
            return;
        }

        // `CItemButton::PaintBackground` (SectionedListPanel.cpp:511-533): a selected row always overrides its own
        // background, never blends with it — see `OutOfFocusSelectedBgColor`'s remarks for why only that branch runs.
        if (item.Id == _selectedItemId)
        {
            surface.DrawSetColor(OutOfFocusSelectedBgColor);
            surface.DrawFilledRect(x, y, x + wide, y + tall);
        }
        else if (item.BgColor is { } bgColor)
        {
            surface.DrawSetColor(bgColor);
            surface.DrawFilledRect(x, y, x + wide, y + tall);
        }

        int textY = y + Math.Max(0, (tall - surface.GetFontTall(font)) / 2);
        int xpos = 0;

        for (int i = 0; i < section.Columns.Count; i++)
        {
            Column column = section.Columns[i];

            if (column.Flags.HasFlag(SectionedListColumn.ColumnImage))
            {
                xpos += column.Width;
                continue;
            }

            string text = item.Data.TryGetValue(column.Name, out string? value) ? value : string.Empty;
            (byte, byte, byte, byte) color = item.FgColor
                ?? (column.Flags.HasFlag(SectionedListColumn.ColumnBright) ? BrightTextColor : RowTextColor);

            // `CItemButton::PerformLayout` (:426-:471): the image's width scaled to the row's height, then placed.
            VguiTextImage image = CellImage(font, text, color);
            (int contentWide, int contentTall) = image.GetContentSize(surface);
            int imageWide = contentTall != 0 ? Math.Min(contentTall, tall) * contentWide / contentTall : 0;
            int columnWidth = column.Width;
            int cellX = xpos;
            int drawWidth = columnWidth - ColumnDataGap;

            if (i == 0)
            {
                cellX = xpos + ColumnDataIndent;
                drawWidth = columnWidth - (ColumnDataIndent + ColumnDataGap);
            }
            else if (column.Flags.HasFlag(SectionedListColumn.ColumnCenter))
            {
                int offset = (columnWidth / 2) - (imageWide / 2);
                cellX = xpos + offset;
                drawWidth = columnWidth - offset - ColumnDataGap;
            }
            else if (column.Flags.HasFlag(SectionedListColumn.ColumnRight))
            {
                cellX = xpos + columnWidth - imageWide;
            }

            Draw(surface, image, x + cellX, textY, drawWidth);
            xpos += column.Width;
        }
    }

    /// <summary>A cell's `TextImage`: its font, colour and text.</summary>
    private static VguiTextImage CellImage(VguiFontAmalgam font, string text, (byte, byte, byte, byte) color)
    {
        VguiTextImage image = new(font) { Color = color };

        image.SetText(text, localize: null);
        return image;
    }

    /// <summary>`SetImageBounds` then the label's paint: a too-wide text truncates with `TextImage`'s own ellipsis.</summary>
    private static void Draw(IVguiSurface surface, VguiTextImage image, int x, int y, int drawWidth)
    {
        image.SetDrawWidth(Math.Max(0, drawWidth));
        image.SetPos(x, y);
        image.Paint(surface);
    }

    private Section? FindSection(int sectionId) => _sections.Find(section => section.Id == sectionId);

    private void SetItem(int itemId, Func<Item, Item> update)
    {
        if (_items.TryGetValue(itemId, out Item? item))
        {
            _items[itemId] = update(item);
            _sortNeeded = true;
        }
    }

    /// <summary>`SectionedListPanel::column_t`.</summary>
    private readonly record struct Column(string Name, string Text, SectionedListColumn Flags, int Width);

    /// <summary>`SectionedListPanel::section_t`, minus its header sub-panel.</summary>
    private sealed class Section(int id, SectionedListSortFunc? sortFunc)
    {
        public int Id { get; } = id;

        public SectionedListSortFunc? SortFunc { get; } = sortFunc;

        public List<Column> Columns { get; } = [];

        public bool AlwaysVisible { get; set; }

        public int MinimumHeight { get; set; }

        public bool DrawDividerBar { get; set; } = true;

        public (byte, byte, byte, byte)? FgColor { get; set; }

        public (byte, byte, byte, byte)? DividerColor { get; set; }
    }

    /// <summary>An item row: `CItemButton`'s ID, section, data and per-item colour overrides.</summary>
    private sealed record Item(int Id, int SectionId, Dictionary<string, string> Data)
    {
        public (byte, byte, byte, byte)? FgColor { get; init; }

        public (byte, byte, byte, byte)? BgColor { get; init; }

        public VguiFontAmalgam? Font { get; init; }
    }
}
