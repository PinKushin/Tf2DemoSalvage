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

    /// <summary>Command name for the frame rate cap.</summary>
    public const string FrameRateLimitCommand = "frame_rate_limit";

    /// <summary>Command name for vertical sync.</summary>
    public const string VerticalSyncCommand = "vertical_sync";

    /// <summary>Command name for the viewmodel's field of view.</summary>
    /// <remarks>
    /// **TF2 lets a player change this, so this viewer does too** — the standing rule in
    /// <c>docs/findings/13-settings-parity.md</c>. It was very nearly shipped as a constant off the
    /// back of reading the SDK, which is the shape of miss that rule exists to catch: the number was
    /// right and the choice was taken away.
    ///
    /// Named as the game names it, so a frag-movie config can be pasted in.
    /// </remarks>
    public const string ViewmodelFieldOfViewCommand = "viewmodel_fov";

    /// <summary>Source's own ceiling, and this viewer's default.</summary>
    public const int SourceFrameRateLimit = 300;

    /// <summary>How full screen is entered.</summary>
    /// <remarks>
    /// Borderless is the default because it always works. Exclusive can be refused by DXGI — when
    /// another application holds the output, or on a WARP device — and a default that sometimes
    /// cannot be honoured is a default that produces support questions.
    /// </remarks>
    public FullScreenMode FullScreenMode { get; init; } = FullScreenMode.Borderless;

    /// <summary>How much texture detail to load.</summary>
    /// <remarks>
    /// **Full by default, which is the frag-movie baseline.** The recording configs people used
    /// for TF2 movies — Chris' maxquality, Lawena, mastercomfig's ultra — all set
    /// <c>mat_picmip -10</c>, meaning drop no mip levels at all. This viewer exists to look at
    /// demos closely, so it should start where those configs start rather than where a competitive
    /// FPS config does.
    ///
    /// **Measured, and it is nearly free.** Decoding every texture in cp_process_final:
    ///
    /// | cap | pixels | time |
    /// |---|---|---|
    /// | 256 | 40 MB | 0.25 s |
    /// | 512 | 120 MB | 0.34 s |
    /// | 1024 | 340 MB | 0.55 s |
    /// | full | 355 MB | 0.58 s |
    ///
    /// Full costs fifteen megabytes and three hundredths of a second over 1024, because very few
    /// TF2 world textures exceed 1024 pixels — the cap mostly binds on a handful of skyboxes. The
    /// lower settings exist for weaker hardware and for the overhead view, where 2048-pixel detail
    /// cannot be seen anyway.
    /// </remarks>
    public TextureQuality TextureQuality { get; init; } = TextureQuality.Full;

    /// <summary>Most frames a second to draw, or zero for no limit.</summary>
    /// <remarks>
    /// **Capped because the measurement said so.** The swap chain presents asking for vertical
    /// sync, and the viewer was still measured at about 600 frames a second — a driver forcing
    /// vsync off outranks the present call, so a program that wants a ceiling has to apply one
    /// itself. Six hundred frames a second is ten times what any display shows, and every one of
    /// them allocates.
    ///
    /// **300 is Source's own <c>fps_max</c> ceiling**, which is the number to match when the point
    /// of the project is to behave the way the game does.
    ///
    /// Lower values are for recording rather than for weak hardware: 24 gives film cadence, 30 and
    /// 60 are the ordinary video rates. A viewer that can only run flat out forces a capture tool
    /// to resample, which is where judder comes from.
    ///
    /// Zero is uncapped, kept expressible because measuring how fast the renderer can go is a real
    /// question — it is how the 600 was found.
    /// </remarks>
    public int FrameRateLimit { get; init; } = SourceFrameRateLimit;

    /// <summary>The field of view the first-person weapon is drawn with, in degrees.</summary>
    /// <remarks>
    /// **The game's own default and the game's own limits.** <c>viewmodel_fov</c> is declared in
    /// <c>view.cpp:111</c> with a default of 54 and, in the TF2 build, hard bounds of 54 and 70:
    ///
    /// <code>
    /// ConVar v_viewmodel_fov( "viewmodel_fov", "54", FCVAR_ARCHIVE, ..., true, 54, true, 70, NULL );
    /// </code>
    ///
    /// So a player can raise it and cannot lower it, and this reproduces both ends rather than
    /// picking its own. A value outside them is clamped rather than refused, which is what the
    /// engine's own ConVar bounds do.
    ///
    /// **The DEFAULT here is 70, the top of that range, and not the game's 54** (D43). The owner
    /// asked for it after trying to check the hands: "the 55 doesnt let me see the hands or arms to
    /// check those". This is a tool for looking at what a demo contains, and at 54 the arms sit
    /// mostly outside the frame.
    ///
    /// **It is a divergence from the game's default and not from the game's behaviour**, which is
    /// the distinction that makes it acceptable here. 70 is a value a TF2 player can set, so every
    /// frame drawn at it is a frame the engine could draw; nothing is being invented. The bounds are
    /// still the engine's, so a config asking for 90 gets 70 exactly as it would in game.
    ///
    /// **TF2 reads a different convar while a demo plays** — <c>viewmodel_fov_demo</c>, same
    /// default — and this viewer is always in that case. One setting covers both because their
    /// defaults agree; if a future TF2 separates them, this is the note that says which one applies.
    /// </remarks>
    public float ViewmodelFieldOfView { get; init; } = ViewmodelPass.LargestFieldOfView;

    /// <summary>Whether to present in step with the display's refresh.</summary>
    /// <remarks>
    /// **Off by default, deliberately.** It adds latency, and a machine whose driver disables it
    /// globally will ignore the request anyway — so a default of on would be a setting that
    /// silently does nothing on the machine this was built on. The frame limit above is the
    /// mechanism that actually holds.
    /// </remarks>
    public bool VerticalSync { get; init; }

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

        // **Negative is ignored rather than obeyed.** A hand-edited file can say anything, and a
        // negative budget makes every frame overdue - which is not "uncapped", it is a cap that
        // looks broken. Zero is the way to say uncapped and is accepted.
        if (Read(values, FrameRateLimitCommand) is { } limit && limit >= 0)
        {
            settings = settings with { FrameRateLimit = limit };
        }

        // **Clamped rather than refused, which is what a ConVar with bounds does.** TF2 declares
        // this one `true, 54, true, 70`, so a config asking for 90 gets 70 in the game and gets 70
        // here — refusing it instead would be this viewer disagreeing with the file it was handed.
        if (ReadNumber(values, ViewmodelFieldOfViewCommand) is { } viewmodelFov)
        {
            settings = settings with
            {
                ViewmodelFieldOfView = Math.Clamp(
                    viewmodelFov,
                    ViewmodelPass.SmallestFieldOfView,
                    ViewmodelPass.LargestFieldOfView),
            };
        }

        if (Read(values, VerticalSyncCommand) is { } sync)
        {
            settings = settings with { VerticalSync = sync != 0 };
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

    /// <summary>The values a setting takes when the file says nothing about it.</summary>
    private static ViewerSettings Defaults { get; } = new();

    /// <summary>Writes one setting, commented out when it still holds its default.</summary>
    /// <param name="text">The file being built.</param>
    /// <param name="command">The command name.</param>
    /// <param name="value">Its current value, already formatted.</param>
    /// <param name="isDefault">Whether that value is the default.</param>
    /// <remarks>
    /// **A default that is written into the file stops being a default.** Every setting used to be
    /// written on the first run, so a config recorded the program's opinions as though they were the
    /// user's — and changing a default afterwards reached nobody who had ever run the viewer. The
    /// owner hit exactly that: the viewmodel field of view was changed to 70 and their file, written
    /// months earlier, pinned 54. The change appeared to do nothing, and nothing could distinguish
    /// "I chose 54" from "54 was written for me before you changed it".
    ///
    /// So a default is written as a COMMENT. The file still documents every setting and its current
    /// default, which is what made writing them all attractive in the first place; uncommenting a
    /// line is how a choice is made; and a value the user never chose stays a default for ever,
    /// following the program.
    /// </remarks>
    private static void Setting(StringBuilder text, string command, string value, bool isDefault)
    {
        ArgumentNullException.ThrowIfNull(text);

        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture, $"{(isDefault ? "// " : string.Empty)}{command} {value}"));
    }

    /// <summary>Renders the settings as config text.</summary>
    /// <returns>The file's contents.</returns>
    /// <remarks>
    /// Commented, because a config nobody can read without the source is a config nobody edits.
    ///
    /// **Settings still at their default are written commented out**, so that changing a default in
    /// a later version reaches everybody who never chose otherwise. See <see cref="Setting"/>.
    /// </remarks>
    public string Write()
    {
        StringBuilder text = new();

        text.AppendLine("// TF2 Demo Salvage settings");
        text.AppendLine("// Edit by hand if you like; unknown commands are ignored.");
        text.AppendLine("//");
        text.AppendLine("// A line that is commented out is still at its default, and will follow");
        text.AppendLine("// that default if a later version changes it. Uncomment to pin a value.");
        text.AppendLine();
        text.AppendLine("// 0 = borderless (covers the taskbar), 1 = exclusive (a real mode change)");
        Setting(
            text,
            FullScreenModeCommand,
            ((int)FullScreenMode).ToString(CultureInfo.InvariantCulture),
            FullScreenMode == Defaults.FullScreenMode);
        text.AppendLine();
        text.AppendLine("// Largest texture edge to load, in pixels. 0 loads them at full size.");
        Setting(
            text,
            TextureQualityCommand,
            ((int)TextureQuality).ToString(CultureInfo.InvariantCulture),
            TextureQuality == Defaults.TextureQuality);
        text.AppendLine();
        text.AppendLine("// Most frames a second to draw. 0 is uncapped; 300 is Source's ceiling.");
        text.AppendLine("// 24 gives film cadence and 30 or 60 the ordinary video rates.");
        Setting(
            text,
            FrameRateLimitCommand,
            FrameRateLimit.ToString(CultureInfo.InvariantCulture),
            FrameRateLimit == Defaults.FrameRateLimit);
        text.AppendLine();
        text.AppendLine("// Field of view for the weapon in your hands, in degrees. TF2 allows 54");
        text.AppendLine("// to 70 and defaults to 54; anything outside that is clamped, as in game.");
        text.AppendLine("// This viewer defaults to 70 instead, because at 54 the arms are mostly");
        text.AppendLine("// out of frame and this is a tool for looking at them. Set 54 for parity.");
        Setting(
            text,
            ViewmodelFieldOfViewCommand,
            ViewmodelFieldOfView.ToString("0.##", CultureInfo.InvariantCulture),

            // Compared with a tolerance, because this one is a float and a config round-trips it
            // through two decimal places. An exact comparison would call 70 "chosen" after a save.
            Math.Abs(ViewmodelFieldOfView - Defaults.ViewmodelFieldOfView) < 0.005f);
        text.AppendLine();
        text.AppendLine("// 1 presents in step with the display. Off by default: it adds latency,");
        text.AppendLine("// and a driver that disables it globally ignores the request anyway.");
        Setting(
            text,
            VerticalSyncCommand,
            (VerticalSync ? 1 : 0).ToString(CultureInfo.InvariantCulture),
            VerticalSync == Defaults.VerticalSync);

        return text.ToString();
    }

    private static int? Read(Dictionary<string, string> values, string command) =>
        values.TryGetValue(command, out string? value) &&
        int.TryParse(value, CultureInfo.InvariantCulture, out int number)
            ? number
            : null;

    /// <summary>Reads a fractional setting, for the ones the game states as floats.</summary>
    private static float? ReadNumber(Dictionary<string, string> values, string command) =>
        values.TryGetValue(command, out string? value) &&
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)
            ? number
            : null;
}
