using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>A scheme's `Borders` block, as `vgui2.dll`'s `CScheme_LoadBorders` (0x18000dc40) builds it.</summary>
/// <remarks>
/// **Closed code, read from the disassembly** (`D:\ghidra-proj\tf2vgui2`):
/// <list type="bullet">
/// <item>Block entries first, in file order. A name the scheme already has is re-applied with the later block — through
/// `ApplySchemeSettings( scheme, block )`, per the registers at 0x18000dcc8 — and keeps the type it was made with. A new
/// name is made by `bordertype`: `image` (0x180003200), `scalable_image` (0x18000bf70), anything else or nothing a line
/// border (0x1800024f0).</item>
/// <item>Then string entries: each is appended as an alias for whatever `GetBorder` answers for its value, with no check
/// for a name already present.</item>
/// <item>`BaseBorder` is looked up last. The exact lookup (0x18000cf30) compares KeyValues symbols — case-insensitive — and
/// the first entry wins; `GetBorder` (0x18000cfa0) answers `BaseBorder` when it misses. On a first load `BaseBorder` is
/// still null while the entries are read, so an alias of an unknown name is null.</item>
/// </list>
/// </remarks>
public sealed class VguiBorders
{
    private readonly List<(string Name, VguiBorder? Border)> _entries = [];
    private VguiBorder? _baseBorder;

    private VguiBorders()
    {
    }

    /// <summary>Builds the borders of a loaded scheme file.</summary>
    /// <param name="root">The scheme file's root.</param>
    /// <param name="scheme">The scheme, for colours.</param>
    /// <param name="screenTall">The screen tall, for `GetProportionalScaledValue`.</param>
    /// <returns>The borders.</returns>
    public static VguiBorders Load(KeyValuesTree root, VguiScheme scheme, int screenTall)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(scheme);

        VguiBorders borders = new();
        KeyValuesTree block = root.FindOrCreate("Borders");

        foreach (KeyValuesTree entry in block.Children)
        {
            if (entry.Value is not null)
            {
                continue;
            }

            if (borders.Get(entry.Name) is not { } border)
            {
                border = Create(entry.Find("bordertype")?.Value);
                border.Name = entry.Name;
                borders._entries.Add((entry.Name, border));
            }

            border.Apply(entry, scheme, screenTall);
        }

        foreach (KeyValuesTree entry in block.Children)
        {
            if (entry.Value is { } target)
            {
                borders._entries.Add((entry.Name, borders.Get(target)));
            }
        }

        borders._baseBorder = borders.Get("BaseBorder");

        return borders;
    }

    /// <summary>`IScheme::GetBorder`: the first border of that name, case-insensitive, else `BaseBorder`.</summary>
    /// <param name="name">The border's name.</param>
    /// <returns>The border; null when neither it nor `BaseBorder` exists.</returns>
    public VguiBorder? Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        foreach ((string entry, VguiBorder? border) in _entries)
        {
            if (string.Equals(entry, name, StringComparison.OrdinalIgnoreCase))
            {
                return border;
            }
        }

        return _baseBorder;
    }

    private static VguiBorder Create(string? type)
    {
        if (string.Equals(type, "image", StringComparison.OrdinalIgnoreCase))
        {
            return new VguiImageBorder();
        }

        return string.Equals(type, "scalable_image", StringComparison.OrdinalIgnoreCase)
            ? new VguiScalableImageBorder()
            : new VguiLineBorder();
    }
}

/// <summary>An `IBorder`, as `ApplySchemeSettings` fills it from its block.</summary>
public abstract class VguiBorder
{
    /// <summary>`GetName`: the name it was registered under.</summary>
    public string Name { get; internal set; } = string.Empty;

    /// <summary>`backgroundtype`: `IBorder::backgroundtype_e`.</summary>
    public int BackgroundType { get; private protected set; }

    /// <summary>`ApplySchemeSettings`.</summary>
    internal abstract void Apply(KeyValuesTree block, VguiScheme scheme, int screenTall);

    /// <summary>`KeyValues::GetInt` on a string value: `atoi`.</summary>
    private protected static int Int(KeyValuesTree block, string key, int fallback) =>
        block.Find(key)?.Value is { } value ? PanelLayout.Atoi(value) : fallback;

    /// <summary>`"vgui/%s"`, or null for an empty name — the old one is freed either way.</summary>
    private protected static string? ImagePath(KeyValuesTree block) =>
        block.Find("image")?.Value is { Length: > 0 } image ? "vgui/" + image : null;
}

/// <summary>One line of a line border's side.</summary>
/// <param name="Color">`color`, resolved through the scheme; transparent black when unresolved.</param>
/// <param name="OffsetX">`offset`'s first number.</param>
/// <param name="OffsetY">`offset`'s second number.</param>
public readonly record struct VguiBorderLine((byte Red, byte Green, byte Blue, byte Alpha) Color, int OffsetX, int OffsetY);

/// <summary>`vgui::Border` — lines per side (`ApplySchemeSettings` at 0x1800025e0).</summary>
public sealed class VguiLineBorder : VguiBorder
{
    /// <summary>`SIDE_LEFT`.</summary>
    public const int Left = 0;

    /// <summary>`SIDE_TOP`.</summary>
    public const int Top = 1;

    /// <summary>`SIDE_RIGHT`.</summary>
    public const int Right = 2;

    /// <summary>`SIDE_BOTTOM`.</summary>
    public const int Bottom = 3;

    private static readonly string[] SideNames = ["Left", "Top", "Right", "Bottom"];

    private readonly int[] _inset = new int[4];
    private readonly VguiBorderLine[][] _sides = [[], [], [], []];

    /// <summary>`inset`: left, top, right, bottom.</summary>
    public IReadOnlyList<int> Inset => _inset;

    /// <summary>`proportional_scalar`.</summary>
    public float ProportionalScalar { get; private set; } = 1f;

    /// <summary>A side's lines, outermost first.</summary>
    /// <param name="side"><see cref="Left"/>, <see cref="Top"/>, <see cref="Right"/> or <see cref="Bottom"/>.</param>
    /// <returns>The lines.</returns>
    public IReadOnlyList<VguiBorderLine> Side(int side) => _sides[side];

    /// <inheritdoc/>
    internal override void Apply(KeyValuesTree block, VguiScheme scheme, int screenTall)
    {
        ProportionalScalar = block.Find("proportional_scalar")?.Value is { } scalar ? PanelLayout.Atof(scalar) : 1f;

        // `sscanf( inset, "%d %d %d %d" )` into what `GetInset` returned: an unread number keeps its old value.
        PanelLayout.ScanInts(block.Find("inset")?.Value ?? "0 0 0 0", _inset);

        for (int side = 0; side < SideNames.Length; side++)
        {
            // A side the block omits keeps its lines.
            if (block.Find(SideNames[side]) is { } lines)
            {
                _sides[side] = [.. ReadLines(lines, scheme)];
            }
        }

        BackgroundType = Int(block, "backgroundtype", 0);
    }

    private static IEnumerable<VguiBorderLine> ReadLines(KeyValuesTree lines, VguiScheme scheme)
    {
        foreach (KeyValuesTree line in lines.Children)
        {
            // `GetColor( name, Color() )`. A line without `color` passes a null name, which is not modelled: it is
            // taken as unresolved.
            (byte, byte, byte, byte) color = line.Find("color")?.Value is { } name ? scheme.GetColor(name, default) : default;
            Span<int> offset = stackalloc int[2];

            PanelLayout.ScanInts(line.Find("offset")?.Value, offset);

            yield return new VguiBorderLine(color, offset[0], offset[1]);
        }
    }
}

/// <summary>`vgui::ImageBorder` — one texture (`ApplySchemeSettings` at 0x1800032d0). Reads no inset.</summary>
public sealed class VguiImageBorder : VguiBorder
{
    /// <summary>`image`, as `"vgui/%s"`; null when empty.</summary>
    public string? Image { get; private set; }

    /// <summary>`tiled`.</summary>
    public bool Tiled { get; private set; }

    /// <summary>`paintfirst`, default on.</summary>
    public bool PaintFirst { get; private set; }

    /// <inheritdoc/>
    internal override void Apply(KeyValuesTree block, VguiScheme scheme, int screenTall)
    {
        BackgroundType = Int(block, "backgroundtype", 0);
        Tiled = Int(block, "tiled", 0) != 0;
        Image = ImagePath(block);
        PaintFirst = Int(block, "paintfirst", 1) != 0;
    }
}

/// <summary>`vgui::ScalableImageBorder` — a nine-slice texture (`ApplySchemeSettings` at 0x18000c050).</summary>
/// <remarks>
/// The drawn corners go through `ISchemeManager::GetProportionalScaledValue` (`VGUI_Scheme010` slot 10, 0x18000d590) — the
/// screen's scale whatever the panel. The source corners stay texels; the engine divides them by the texture's size, which
/// is the renderer's to do.
/// </remarks>
public sealed class VguiScalableImageBorder : VguiBorder
{
    /// <summary>`src_corner_height`, in texels.</summary>
    public int SourceCornerHeight { get; private set; }

    /// <summary>`src_corner_width`, in texels.</summary>
    public int SourceCornerWidth { get; private set; }

    /// <summary>`draw_corner_height`, proportionally scaled.</summary>
    public int DrawCornerHeight { get; private set; }

    /// <summary>`draw_corner_width`, proportionally scaled.</summary>
    public int DrawCornerWidth { get; private set; }

    /// <summary>`image`, as `"vgui/%s"`; null when empty.</summary>
    public string? Image { get; private set; }

    /// <summary>`paintfirst`, default on.</summary>
    public bool PaintFirst { get; private set; }

    /// <summary>`color`, resolved through the scheme; opaque white when absent or empty.</summary>
    public (byte Red, byte Green, byte Blue, byte Alpha) Color { get; private set; }

    /// <inheritdoc/>
    internal override void Apply(KeyValuesTree block, VguiScheme scheme, int screenTall)
    {
        (byte, byte, byte, byte) white = (255, 255, 255, 255);

        BackgroundType = Int(block, "backgroundtype", 0);
        SourceCornerHeight = Int(block, "src_corner_height", 0);
        SourceCornerWidth = Int(block, "src_corner_width", 0);
        DrawCornerHeight = PanelLayout.ProportionalScaled(Int(block, "draw_corner_height", 0), screenTall);
        DrawCornerWidth = PanelLayout.ProportionalScaled(Int(block, "draw_corner_width", 0), screenTall);
        Image = ImagePath(block);
        PaintFirst = Int(block, "paintfirst", 1) != 0;
        Color = block.Find("color")?.Value is { Length: > 0 } name ? scheme.GetColor(name, white) : white;
    }
}
