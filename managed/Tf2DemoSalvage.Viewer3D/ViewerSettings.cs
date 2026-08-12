using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>How the viewer fills the screen.</summary>
internal enum FullScreenMode
{
    /// <summary>A borderless window sized to the display.</summary>
    /// <remarks>
    /// What TF2 itself calls borderless, and it covers the taskbar. Alt-tab is instant because
    /// nothing changes about the display, and other windows can still appear over it.
    /// </remarks>
    Borderless = 0,

    /// <summary>The swap chain takes the display.</summary>
    /// <remarks>
    /// A real mode change: presentation skips the desktop compositor, which is the lower-latency
    /// path, at the cost of a slower alt-tab and a display that must be handed back.
    /// </remarks>
    Exclusive = 1,
}

/// <summary>How much texture detail to load.</summary>
/// <remarks>
/// **This picks a mip level out of the game's own chain rather than resampling anything.** Valve
/// generated every size when the texture was made, so asking for the 256-pixel version of a
/// 2048-pixel texture is a smaller read and a smaller upload — not a downscale of something already
/// paid for.
///
/// It is a setting because the right answer depends on the machine and on the view: an overhead
/// shot of a whole map cannot show 2048-pixel detail on anything, while a camera behind a player's
/// eyes can.
/// </remarks>
internal enum TextureQuality
{
    /// <summary>Full size, as the game loads it.</summary>
    Full = 0,

    /// <summary>Cap the longest edge at 1024 pixels.</summary>
    High = 1024,

    /// <summary>Cap the longest edge at 512 pixels.</summary>
    Medium = 512,

    /// <summary>Cap the longest edge at 256 pixels.</summary>
    Low = 256,
}

/// <summary>
/// Settings the viewer remembers between runs, in a Source-style <c>.cfg</c>.
/// </summary>
/// <remarks>
/// **The same shape as TF2's own <c>config.cfg</c>**: one command per line, a value after a space,
/// <c>//</c> for comments. That is a deliberate match rather than a flourish — someone who has
/// edited a Source config already knows how to edit this one, and it can be read and diffed
/// without a JSON viewer.
///
/// <code>
///   // TF2 Demo Salvage settings
///   fullscreen_mode 1
///   texture_quality 512
/// </code>
///
/// Under LocalApplicationData rather than beside the executable: the program may sit in a
/// read-only folder, and settings are per-user in any case.
///
/// **Every failure to read is silent and yields defaults.** A settings file is a convenience;
/// refusing to start because one is missing, corrupt or locked would make a nicety into a
/// dependency. Failures to *write* are reported, because a preference that silently does not stick
/// is worse than one that says it did not.
///
/// **An unknown command is ignored rather than rejected**, which is exactly how Source treats a
/// cvar it does not have: a config written by a later version must not stop an earlier one
/// starting.
/// </remarks>
internal sealed record ViewerSettings
{
    /// <summary>Command name for the full-screen mode.</summary>
    public const string FullScreenModeCommand = "fullscreen_mode";

    /// <summary>Command name for the texture detail.</summary>
    public const string TextureQualityCommand = "texture_quality";

    /// <summary>How full screen is entered.</summary>
    /// <remarks>
    /// Borderless is the default because it always works. Exclusive can be refused by DXGI — when
    /// another application holds the output, or on a WARP device — and a default that sometimes
    /// cannot be honoured is a default that produces support questions.
    /// </remarks>
    public FullScreenMode FullScreenMode { get; init; } = FullScreenMode.Borderless;

    /// <summary>How much texture detail to load.</summary>
    /// <remarks>
    /// Medium by default. A whole map at 512 is 208 textures for cp_process_final, which decodes
    /// in about a second and is more detail than an overhead view can show; Full exists for a close
    /// camera and for hardware that does not care.
    /// </remarks>
    public TextureQuality TextureQuality { get; init; } = TextureQuality.Medium;

    /// <summary>Where the settings file lives.</summary>
    public static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tf2DemoSalvage",
        "settings.cfg");

    /// <summary>Reads the settings, or returns defaults.</summary>
    /// <param name="path">File to read; defaults to <see cref="Path"/>.</param>
    /// <returns>The settings.</returns>
    public static ViewerSettings Load(string? path = null)
    {
        string file = path ?? Path;

        try
        {
            return File.Exists(file) ? Parse(File.ReadAllText(file)) : new ViewerSettings();
        }
        catch (Exception failure) when (
            failure is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Deliberately silent. See the remarks on the type: a settings file that cannot be
            // read must not stop the viewer opening a demo.
            return new ViewerSettings();
        }
    }

    /// <summary>Parses config text.</summary>
    /// <param name="text">The file's contents.</param>
    /// <returns>The settings, with defaults for anything absent or unreadable.</returns>
    /// <remarks>
    /// A value that is not a number keeps the default rather than failing the whole file, for the
    /// same reason an unknown command is ignored: one bad line must not cost every other setting.
    /// </remarks>
    public static ViewerSettings Parse(string? text)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

        foreach (string line in (text ?? string.Empty).Split('\n'))
        {
            ReadOnlySpan<char> content = line.AsSpan();
            int comment = content.IndexOf("//", StringComparison.Ordinal);

            if (comment >= 0)
            {
                content = content[..comment];
            }

            content = content.Trim();

            if (content.IsEmpty)
            {
                continue;
            }

            int space = content.IndexOfAny(' ', '\t');

            if (space <= 0)
            {
                continue;
            }

            // Quotes around a value are accepted and stripped, as Source accepts them.
            values[content[..space].ToString()] = content[(space + 1)..].Trim().Trim('"').ToString();
        }

        ViewerSettings settings = new();

        if (Read(values, FullScreenModeCommand) is { } mode && Enum.IsDefined((FullScreenMode)mode))
        {
            settings = settings with { FullScreenMode = (FullScreenMode)mode };
        }

        if (Read(values, TextureQualityCommand) is { } quality &&
            Enum.IsDefined((TextureQuality)quality))
        {
            settings = settings with { TextureQuality = (TextureQuality)quality };
        }

        return settings;
    }

    /// <summary>Writes the settings.</summary>
    /// <param name="path">File to write; defaults to <see cref="Path"/>.</param>
    /// <returns>An error to report, or null on success.</returns>
    /// <remarks>
    /// Returns the failure rather than throwing or swallowing it. The caller has a status line and
    /// is the only place that knows whether anyone is watching.
    /// </remarks>
    public string? Save(string? path = null)
    {
        string file = path ?? Path;

        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            File.WriteAllText(file, Write(), Encoding.UTF8);
            return null;
        }
        catch (Exception failure) when (
            failure is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return failure.Message;
        }
    }

    /// <summary>Renders the settings as config text.</summary>
    /// <returns>The file's contents.</returns>
    /// <remarks>
    /// Commented, because a config nobody can read without the source is a config nobody edits.
    /// </remarks>
    public string Write()
    {
        StringBuilder text = new();

        text.AppendLine("// TF2 Demo Salvage settings");
        text.AppendLine("// Edit by hand if you like; unknown commands are ignored.");
        text.AppendLine();
        text.AppendLine("// 0 = borderless (covers the taskbar), 1 = exclusive (a real mode change)");
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture, $"{FullScreenModeCommand} {(int)FullScreenMode}"));
        text.AppendLine();
        text.AppendLine("// Largest texture edge to load, in pixels. 0 loads them at full size.");
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture, $"{TextureQualityCommand} {(int)TextureQuality}"));

        return text.ToString();
    }

    private static int? Read(Dictionary<string, string> values, string command) =>
        values.TryGetValue(command, out string? value) &&
        int.TryParse(value, CultureInfo.InvariantCulture, out int number)
            ? number
            : null;
}
