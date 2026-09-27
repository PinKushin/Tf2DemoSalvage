using System;
using System.Collections.Generic;
using System.Linq;

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

/// <summary>`vgui::SectionedListPanel` (vgui2/vgui_controls/SectionedListPanel.cpp): sections of columned rows, such as the scoreboard.</summary>
/// <remarks>
/// The drawing half, ported the way <see cref="VguiRichText"/> ports `RichText`: `AddSection` (:1195, :1204, :1213),
/// `AddColumnToSection` (:1249, :1264), `ModifyColumn` (:1287), `AddItem`/`ModifyItem` (:1315, :1330), `RemoveItem` (:1465),
/// `DeleteAllItems` (:1884), `SetItemFgColor`/`SetItemBgColor` (:1346, :1362), `SetSectionFgColor`/`SetSectionDividerColor`/
/// `SetSectionDrawDividerBar`/`SetSectionAlwaysVisible`/`SetSectionMinimumHeight` (:1407-:1460), `SetImageList` (:2160),
/// `GetSectionTall` (:2184), and the layout and paint that `LayoutPanels` (:942) and `CItemButton`/`SectionedListPanelHeader`'s
/// own `PerformLayout`/`Paint` (:107, :81, :297, :536) do between them for a row-and-column list with no real child panels.
/// A section keeps its items in the order they were added — Valve's `m_SortedItems` re-sorts by each section's
/// `SectionSortFunc_t`; sorting is not modelled, so items paint in insertion order.
/// **Not modelled:** mouse and keyboard selection, the edit mode and its sub-panel, the context and edit menus, drag and
/// drop, the scroll bar's own drawing (only its value offsets `y`, as `LayoutPanels` reads `m_pScrollBar->GetValue()`),
/// per-section sort functions, actual images (a `COLUMN_IMAGE` cell tracks an image index but draws nothing unless an
/// image list is set and asked for a texture), the header's "draw over the next blank header" column merge, and Valve's
/// ellipsis truncation (`TextImage::ResizeImageToContentMaxWidth`) — a cell too wide for its column is clipped to whole
/// characters here with no ellipsis, which is an interpolation of the real behaviour, not a citation of it.
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
    private readonly List<Item> _sortedItems = [];
    private int _nextItemId;

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
        HeaderFont ??= context.GetFont("DefaultVerySmall", Proportional);

        if (RowFont is null && context.Scheme.GetResourceString("SectionedListPanel.Font") is { Length: > 0 } fontName)
        {
            RowFont = context.GetFont(fontName, Proportional);
        }
    }

    /// <summary>`AddSection`: a section keyed by <paramref name="sectionId"/>, holding its own columns.</summary>
    /// <param name="sectionId">The section's ID — not an index; sections are found by this value.</param>
    /// <param name="name">The header's name (Valve's `Panel` name; unused for drawing here).</param>
    public void AddSection(int sectionId, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        _sections.Add(new Section(sectionId));
    }

    /// <summary>`RemoveAllSections` (:1227): every section and item gone.</summary>
    public void RemoveAllSections()
    {
        _sections.Clear();
        _sortedItems.Clear();
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

    /// <summary>`AddItem` (:1315): a new item, appended to the paint order.</summary>
    /// <param name="sectionId">The section it belongs to.</param>
    /// <param name="data">Its cell data, keyed by column name.</param>
    /// <returns>The new item's ID.</returns>
    public int AddItem(int sectionId, IReadOnlyDictionary<string, string> data)
    {
        ArgumentNullException.ThrowIfNull(data);

        int itemId = _nextItemId++;
        Item item = new(itemId, sectionId, new Dictionary<string, string>(data, StringComparer.Ordinal));

        _sortedItems.Add(item);
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

        int index = _sortedItems.FindIndex(item => item.Id == itemId);

        if (index < 0)
        {
            return false;
        }

        _sortedItems[index] = _sortedItems[index] with { SectionId = sectionId, Data = new Dictionary<string, string>(data, StringComparer.Ordinal) };
        return true;
    }

    /// <summary>`RemoveItem` (:1465).</summary>
    /// <param name="itemId">The item.</param>
    /// <returns>Whether it was found and removed.</returns>
    public bool RemoveItem(int itemId) => _sortedItems.RemoveAll(item => item.Id == itemId) > 0;

    /// <summary>`DeleteAllItems` (:1884): every item gone; sections stay.</summary>
    public void ClearItems() => _sortedItems.Clear();

    /// <summary>`GetItemData` (:1954).</summary>
    /// <param name="itemId">The item.</param>
    /// <returns>Its data, or null when the item does not exist.</returns>
    public IReadOnlyDictionary<string, string>? GetItemData(int itemId) => _sortedItems.Find(item => item.Id == itemId)?.Data;

    /// <summary>`GetItemSection` (:1966).</summary>
    /// <param name="itemId">The item.</param>
    /// <returns>Its section ID, or -1 when the item does not exist.</returns>
    public int GetItemSection(int itemId) => _sortedItems.Find(item => item.Id == itemId)?.SectionId ?? -1;

    /// <summary>`IsItemIDValid` (:1977).</summary>
    /// <param name="itemId">The item.</param>
    public bool IsItemIdValid(int itemId) => _sortedItems.Exists(item => item.Id == itemId);

    /// <summary>`GetItemCount` (:1993).</summary>
    public int ItemCount => _sortedItems.Count;

    /// <summary>`SetItemFgColor` (:1346): overrides the row's default and bright colours for this item alone.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="color">The colour.</param>
    public void SetItemFgColor(int itemId, (byte Red, byte Green, byte Blue, byte Alpha) color) => SetItem(itemId, item => item with { FgColor = color });

    /// <summary>`SetItemBgColor` (:1362): enables the row's own background fill.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="color">The colour.</param>
    public void SetItemBgColor(int itemId, (byte Red, byte Green, byte Blue, byte Alpha) color) => SetItem(itemId, item => item with { BgColor = color });

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
    /// sub-panels exist here: a section's header (if any items are in it, or it is always visible), then its rows.
    /// </remarks>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (HeaderFont is null && RowFont is null)
        {
            return;
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

    private void PaintSectionHeader(IVguiSurface surface, Section section, int x, int y, int wide, int tall)
    {
        if (HeaderFont is not { } font)
        {
            return;
        }

        surface.DrawSetTextFont(font);

        (byte, byte, byte, byte) color = section.FgColor ?? HeaderTextColor;
        int textY = y + Math.Max(0, (tall - surface.GetFontTall(font)) / 2);
        int xpos = 0;

        foreach (Column column in section.Columns)
        {
            if (!column.Flags.HasFlag(SectionedListColumn.HeaderImage))
            {
                DrawCell(surface, column.Text, column.Flags, xpos, column.Width, isFirstColumn: false, x, textY, color);
            }

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
        if (RowFont is not { } font)
        {
            return;
        }

        if (item.BgColor is { } bgColor)
        {
            surface.DrawSetColor(bgColor);
            surface.DrawFilledRect(x, y, x + wide, y + tall);
        }

        surface.DrawSetTextFont(font);

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

            DrawCell(surface, text, column.Flags, xpos, column.Width, isFirstColumn: i == 0, x, textY, color);
            xpos += column.Width;
        }
    }

    /// <summary>The per-column placement `CItemButton::PerformLayout`/`SectionedListPanelHeader::PerformLayout` compute (:450-:470).</summary>
    private static void DrawCell(
        IVguiSurface surface, string text, SectionedListColumn flags, int columnX, int columnWidth, bool isFirstColumn, int panelX, int textY, (byte, byte, byte, byte) color)
    {
        int maxWidth = isFirstColumn ? columnWidth - (ColumnDataIndent + ColumnDataGap) : columnWidth - ColumnDataGap;

        text = Clip(surface, text, Math.Max(0, maxWidth));

        int textWidth = MeasureWidth(surface, text);
        int cellX;

        if (isFirstColumn)
        {
            cellX = columnX + ColumnDataIndent;
        }
        else if (flags.HasFlag(SectionedListColumn.ColumnCenter))
        {
            cellX = columnX + (columnWidth / 2) - (textWidth / 2);
        }
        else if (flags.HasFlag(SectionedListColumn.ColumnRight))
        {
            cellX = columnX + columnWidth - textWidth;
        }
        else
        {
            cellX = columnX;
        }

        surface.DrawSetTextColor(color);
        surface.DrawSetTextPos(panelX + cellX, textY);
        surface.DrawPrintText(text);
    }

    private static int MeasureWidth(IVguiSurface surface, string text)
    {
        int width = 0;

        foreach (char character in text)
        {
            width += surface.GetCharacterWidth(null!, character);
        }

        return width;
    }

    /// <summary>Not Valve's `TextImage::ResizeImageToContentMaxWidth` ellipsis — whole characters dropped from the end, no mark.</summary>
    private static string Clip(IVguiSurface surface, string text, int maxWidth)
    {
        if (maxWidth <= 0 || MeasureWidth(surface, text) <= maxWidth)
        {
            return text;
        }

        int end = text.Length;

        while (end > 0 && MeasureWidth(surface, text[..end]) > maxWidth)
        {
            end--;
        }

        return text[..end];
    }

    private Section? FindSection(int sectionId) => _sections.Find(section => section.Id == sectionId);

    private void SetItem(int itemId, Func<Item, Item> update)
    {
        int index = _sortedItems.FindIndex(item => item.Id == itemId);

        if (index >= 0)
        {
            _sortedItems[index] = update(_sortedItems[index]);
        }
    }

    /// <summary>`SectionedListPanel::column_t`.</summary>
    private readonly record struct Column(string Name, string Text, SectionedListColumn Flags, int Width);

    /// <summary>`SectionedListPanel::section_t`, minus its header sub-panel and sort function.</summary>
    private sealed class Section(int id)
    {
        public int Id { get; } = id;

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
    }
}
