using System;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>A VGUI scheme — `ClientScheme.res` — as `vgui2.dll`'s `CScheme` holds and resolves it.</summary>
/// <remarks>
/// **The scheme is closed code**, so this is read from the disassembly (functions renamed in `D:\ghidra-proj\tf2vgui2`):
/// <list type="bullet">
/// <item>`CScheme_LoadFromFile` (0x18000e570): takes `BaseSettings` and `Colors` (both created when absent), then seeds the
/// 117 base settings of the table at 0x18007b760 that the file omits — from the fallback setting's resolved value when the
/// row names one, else from the row's literal default.</item>
/// <item>`CScheme_LookupSchemeSetting` (0x18000e8c0): a name that already scans as three or more numbers is returned as it
/// is; else its `Colors` value as it stands; else its `BaseSettings` value, resolved again; else the name itself.</item>
/// <item>`GetColor` (vtable slot 5, 0x18000d000): the lookup, `sscanf( "%d %d %d %d" )` into zeroed ints, three or more
/// taken and each truncated to a byte; otherwise the caller's default.</item>
/// </list>
/// **The table is copied as it ships, bugs included**: a missing comma in Valve's source joins `"Border.Dark"
/// "BorderDark"` into one name, and `"Border.Selection" "BorderSelection"` likewise, so neither `Border.Dark` nor
/// `Border.Selection` is ever seeded — which is what TF2 does.
/// </remarks>
public sealed class VguiScheme
{
    private readonly KeyValuesTree _baseSettings;
    private readonly KeyValuesTree _colors;

    private VguiScheme(KeyValuesTree baseSettings, KeyValuesTree colors)
    {
        _baseSettings = baseSettings;
        _colors = colors;
    }

    /// <summary>Builds a scheme from its loaded file.</summary>
    /// <param name="root">The scheme file's root, as <see cref="KeyValuesTree"/> loads it (resolution keys applied).</param>
    /// <returns>The scheme.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is null.</exception>
    public static VguiScheme Load(KeyValuesTree root)
    {
        ArgumentNullException.ThrowIfNull(root);

        VguiScheme scheme = new(root.FindOrCreate("BaseSettings"), root.FindOrCreate("Colors"));

        foreach ((string name, string? fallback, string? literal) in BaseSettingDefaults)
        {
            if (scheme._baseSettings.Find(name) is null)
            {
                scheme._baseSettings.SetString(name, fallback is null ? literal : scheme.Lookup(fallback));
            }
        }

        return scheme;
    }

    /// <summary>`CScheme_LookupSchemeSetting`: what a setting name resolves to.</summary>
    /// <param name="name">A colour or base setting name, or a colour written out.</param>
    /// <returns>The resolved string; the name itself when nothing resolves it.</returns>
    public string Lookup(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (ScanColour(name).Count >= 3)
        {
            return name;
        }

        if (_colors.Find(name)?.Value is { } colour)
        {
            return colour;
        }

        return _baseSettings.Find(name)?.Value is { } setting ? Lookup(setting) : name;
    }

    /// <summary>`IScheme::GetColor`.</summary>
    /// <param name="name">A colour or base setting name.</param>
    /// <param name="fallback">What an unresolved name answers.</param>
    /// <returns>The colour, red first.</returns>
    public (byte Red, byte Green, byte Blue, byte Alpha) GetColor(string name, (byte Red, byte Green, byte Blue, byte Alpha) fallback)
    {
        (int count, int red, int green, int blue, int alpha) = ScanColour(Lookup(name));

        return count >= 3 ? ((byte)red, (byte)green, (byte)blue, (byte)alpha) : fallback;
    }

    /// <summary>`sscanf( text, "%d %d %d %d" )` into zeroed ints: how many were read, and the four.</summary>
    private static (int Count, int Red, int Green, int Blue, int Alpha) ScanColour(string text)
    {
        int[] values = new int[4];
        int count = 0;
        ReadOnlySpan<char> rest = text;

        while (count < 4)
        {
            rest = rest.TrimStart();

            int length = 0;

            if (length < rest.Length && rest[length] is '-' or '+')
            {
                length++;
            }

            int digits = length;

            while (length < rest.Length && char.IsAsciiDigit(rest[length]))
            {
                length++;
            }

            if (length == digits)
            {
                break;
            }

            values[count++] = PanelLayout.Atoi(rest[..length]);
            rest = rest[length..];

            // The format's literal space matches any run of white space, including none.
        }

        return (count, values[0], values[1], values[2], values[3]);
    }

    /// <summary>The base settings `CScheme_LoadFromFile` seeds, as the table at 0x18007b760 holds them: name, fallback, default.</summary>
    private static readonly (string Name, string? Fallback, string? Default)[] BaseSettingDefaults =
    [
        ("Border.Bright", "BorderBright", "200 200 200 196"),
        ("Border.DarkBorderDark", "40 40 40 196", null),
        ("Border.SelectionBorderSelection", "0 0 0 196", null),
        ("Button.TextColor", "ControlFG", "White"),
        ("Button.BgColor", "ControlBG", "Blank"),
        ("Button.ArmedTextColor", "ControlFG", null),
        ("Button.ArmedBgColor", "ControlBG", null),
        ("Button.DepressedTextColor", "ControlFG", null),
        ("Button.DepressedBgColor", "ControlBG", null),
        ("Button.FocusBorderColor", "0 0 0 255", null),
        ("CheckButton.TextColor", "BaseText", null),
        ("CheckButton.SelectedTextColor", "BrightControlText", null),
        ("CheckButton.BgColor", "CheckBgColor", null),
        ("CheckButton.Border1", "CheckButtonBorder1", null),
        ("CheckButton.Border2", "CheckButtonBorder2", null),
        ("CheckButton.Check", "CheckButtonCheck", null),
        ("ComboBoxButton.ArrowColor", "LabelDimText", null),
        ("ComboBoxButton.ArmedArrowColor", "MenuButton/ArmedArrowColor", null),
        ("ComboBoxButton.BgColor", "MenuButton/ButtonBgColor", null),
        ("ComboBoxButton.DisabledBgColor", "ControlBG", null),
        ("Frame.TitleTextInsetX", null, "32"),
        ("Frame.ClientInsetX", null, "8"),
        ("Frame.ClientInsetY", null, "6"),
        ("Frame.BgColor", "BgColor", null),
        ("Frame.OutOfFocusBgColor", "BgColor", null),
        ("Frame.FocusTransitionEffectTime", null, "0"),
        ("Frame.TransitionEffectTime", null, "0"),
        ("Frame.AutoSnapRange", null, "8"),
        ("FrameGrip.Color1", "BorderBright", null),
        ("FrameGrip.Color2", "BorderSelection", null),
        ("FrameTitleButton.FgColor", "TitleButtonFgColor", null),
        ("FrameTitleButton.BgColor", "TitleButtonBgColor", null),
        ("FrameTitleButton.DisabledFgColor", "TitleButtonDisabledFgColor", null),
        ("FrameTitleButton.DisabledBgColor", "TitleButtonDisabledBgColor", null),
        ("FrameSystemButton.FgColor", "TitleBarBgColor", null),
        ("FrameSystemButton.BgColor", "TitleBarBgColor", null),
        ("FrameSystemButton.Icon", "TitleBarIcon", null),
        ("FrameSystemButton.DisabledIcon", "TitleBarDisabledIcon", null),
        ("FrameTitleBar.Font", null, "Default"),
        ("FrameTitleBar.TextColor", "TitleBarFgColor", null),
        ("FrameTitleBar.BgColor", "TitleBarBgColor", null),
        ("FrameTitleBar.DisabledTextColor", "TitleBarDisabledFgColor", null),
        ("FrameTitleBar.DisabledBgColor", "TitleBarDisabledBgColor", null),
        ("GraphPanel.FgColor", "BrightControlText", null),
        ("GraphPanel.BgColor", "WindowBgColor", null),
        ("Label.TextDullColor", "LabelDimText", null),
        ("Label.TextColor", "BaseText", null),
        ("Label.TextBrightColor", "BrightControlText", null),
        ("Label.SelectedTextColor", "BrightControlText", null),
        ("Label.BgColor", "LabelBgColor", null),
        ("Label.DisabledFgColor1", "DisabledFgColor1", null),
        ("Label.DisabledFgColor2", "DisabledFgColor2", null),
        ("ListPanel.TextColor", "WindowFgColor", null),
        ("ListPanel.TextBgColor", "Menu/ArmedBgColor", null),
        ("ListPanel.BgColor", "ListBgColor", null),
        ("ListPanel.SelectedTextColor", "ListSelectionFgColor", null),
        ("ListPanel.SelectedBgColor", "Menu/ArmedBgColor", null),
        ("ListPanel.SelectedOutOfFocusBgColor", "SelectionBG2", null),
        ("ListPanel.EmptyListInfoTextColor", "LabelDimText", null),
        ("ListPanel.DisabledTextColor", "LabelDimText", null),
        ("ListPanel.DisabledSelectedTextColor", "ListBgColor", null),
        ("Menu.TextColor", "Menu/FgColor", null),
        ("Menu.BgColor", "Menu/BgColor", null),
        ("Menu.ArmedTextColor", "Menu/ArmedFgColor", null),
        ("Menu.ArmedBgColor", "Menu/ArmedBgColor", null),
        ("Menu.TextInset", null, "6"),
        ("Panel.FgColor", "FgColor", null),
        ("Panel.BgColor", "BgColor", null),
        ("ProgressBar.FgColor", "BrightControlText", null),
        ("ProgressBar.BgColor", "WindowBgColor", null),
        ("PropertySheet.TextColor", "FgColorDim", null),
        ("PropertySheet.SelectedTextColor", "BrightControlText", null),
        ("PropertySheet.TransitionEffectTime", null, "0"),
        ("RadioButton.TextColor", "FgColor", null),
        ("RadioButton.SelectedTextColor", "BrightControlText", null),
        ("RichText.TextColor", "WindowFgColor", null),
        ("RichText.BgColor", "WindowBgColor", null),
        ("RichText.SelectedTextColor", "SelectionFgColor", null),
        ("RichText.SelectedBgColor", "SelectionBgColor", null),
        ("ScrollBar.Wide", null, "19"),
        ("ScrollBarButton.FgColor", "DimBaseText", null),
        ("ScrollBarButton.BgColor", "ControlBG", null),
        ("ScrollBarButton.ArmedFgColor", "BaseText", null),
        ("ScrollBarButton.ArmedBgColor", "ControlBG", null),
        ("ScrollBarButton.DepressedFgColor", "BaseText", null),
        ("ScrollBarButton.DepressedBgColor", "ControlBG", null),
        ("ScrollBarSlider.FgColor", "ScrollBarSlider/ScrollBarSliderFgColor", null),
        ("ScrollBarSlider.BgColor", "ScrollBarSlider/ScrollBarSliderBgColor", null),
        ("SectionedListPanel.HeaderTextColor", "SectionTextColor", null),
        ("SectionedListPanel.HeaderBgColor", "BuddyListBgColor", null),
        ("SectionedListPanel.DividerColor", "SectionDividerColor", null),
        ("SectionedListPanel.TextColor", "BuddyButton/FgColor1", null),
        ("SectionedListPanel.BrightTextColor", "BuddyButton/ArmedFgColor1", null),
        ("SectionedListPanel.BgColor", "BuddyListBgColor", null),
        ("SectionedListPanel.SelectedTextColor", "BuddyButton/ArmedFgColor1", null),
        ("SectionedListPanel.SelectedBgColor", "BuddyButton/ArmedBgColor", null),
        ("SectionedListPanel.OutOfFocusSelectedTextColor", "BuddyButton/ArmedFgColor2", null),
        ("SectionedListPanel.OutOfFocusSelectedBgColor", "SelectionBG2", null),
        ("Slider.NobColor", "SliderTickColor", null),
        ("Slider.TextColor", "Slider/SliderFgColor", null),
        ("Slider.TrackColor", "SliderTrackColor", null),
        ("Slider.DisabledTextColor1", "DisabledFgColor1", null),
        ("Slider.DisabledTextColor2", "DisabledFgColor2", null),
        ("TextEntry.TextColor", "WindowFgColor", null),
        ("TextEntry.BgColor", "WindowBgColor", null),
        ("TextEntry.CursorColor", "TextCursorColor", null),
        ("TextEntry.DisabledTextColor", "WindowDisabledFgColor", null),
        ("TextEntry.DisabledBgColor", "ControlBG", null),
        ("TextEntry.SelectedTextColor", "SelectionFgColor", null),
        ("TextEntry.SelectedBgColor", "SelectionBgColor", null),
        ("TextEntry.OutOfFocusSelectedBgColor", "SelectionBG2", null),
        ("TextEntry.FocusEdgeColor", "BorderSelection", null),
        ("ToggleButton.SelectedTextColor", "BrightControlText", null),
        ("Tooltip.TextColor", "BorderSelection", null),
        ("Tooltip.BgColor", "SelectionBG", null),
        ("TreeView.BgColor", "ListBgColor", null),
        ("WizardSubPanel.BgColor", "SubPanelBgColor", null),
    ];
}
