using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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

/// <summary>
/// Settings the viewer remembers between runs.
/// </summary>
/// <remarks>
/// **A file rather than the registry**, and under LocalApplicationData rather than beside the
/// executable: the program may sit in a read-only folder, and settings are per-user in any case.
///
/// **Every failure to read is silent and yields defaults.** A settings file is a convenience;
/// refusing to start because one is missing, corrupt, or locked would make a nicety into a
/// dependency. Failures to *write* are reported, because a preference that silently does not stick
/// is worse than one that says it did not.
/// </remarks>
internal sealed record ViewerSettings
{
    /// <summary>How full screen is entered.</summary>
    /// <remarks>
    /// Borderless is the default because it always works. Exclusive can be refused by DXGI — when
    /// another application holds the output, or on a WARP device — and a default that sometimes
    /// cannot be honoured is a default that produces support questions.
    /// </remarks>
    [JsonPropertyName("fullScreenMode")]
    public FullScreenMode FullScreenMode { get; init; } = FullScreenMode.Borderless;

    /// <summary>Where the settings file lives.</summary>
    public static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tf2DemoSalvage",
        "settings.json");

    /// <summary>Reads the settings, or returns defaults.</summary>
    /// <param name="path">File to read; defaults to <see cref="Path"/>.</param>
    /// <returns>The settings.</returns>
    public static ViewerSettings Load(string? path = null)
    {
        string file = path ?? Path;

        try
        {
            if (!File.Exists(file))
            {
                return new ViewerSettings();
            }

            return JsonSerializer.Deserialize<ViewerSettings>(File.ReadAllText(file))
                ?? new ViewerSettings();
        }
        catch (Exception failure) when (
            failure is IOException or JsonException or UnauthorizedAccessException
                or ArgumentException)
        {
            // Deliberately silent. See the remarks on the type: a settings file that cannot be
            // read must not stop the viewer opening a demo.
            return new ViewerSettings();
        }
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
            File.WriteAllText(file, JsonSerializer.Serialize(this, SerializerOptions));
            return null;
        }
        catch (Exception failure) when (
            failure is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return failure.Message;
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };
}
