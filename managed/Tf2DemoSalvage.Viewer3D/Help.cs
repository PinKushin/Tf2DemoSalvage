using System;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>What the viewer accepts, printed on <c>--help</c>.</summary>
/// <remarks>
/// **Written because an option that exists and cannot be discovered is an option that gets reported
/// as missing.** `--first-person` was parsed in `LaunchOptions` and a parity audit filed a finding
/// saying the viewer had no such flag and swallowed it silently — the search had run over the
/// Viewer3D project, and launch options live in Presentation. A `--help` answers that in one call
/// instead of a grep whose scope has to be guessed right.
///
/// **Held as text next to the parser's project rather than generated from it.** Generating it would
/// keep the two in step automatically and would also mean the list could only be read by running the
/// program, which is the thing that was too expensive. This is a page somebody can also just open.
///
/// **Env vars are listed beside the flags because they are not otherwise discoverable at all** —
/// each is one `Environment.GetEnvironmentVariable` in a file nobody greps for by name.
/// </remarks>
internal static class Help
{
    /// <summary>Whether the arguments ask for the list.</summary>
    /// <param name="arguments">The command line, as given.</param>
    /// <returns>True when the viewer should print and exit.</returns>
    /// <remarks>
    /// Read here rather than from <c>LaunchOptions</c> so the answer costs nothing: printing must
    /// happen before WinForms is initialised and before a settings file is read, and both of those
    /// come first in <c>LaunchOptionsReader.Read</c>. The parser records it too, for anything that
    /// wants the option in the ordinary way.
    /// </remarks>
    public static bool Wanted(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        foreach (string argument in arguments)
        {
            if (argument is "--help" or "-h" or "-?" or "/?")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The list.</summary>
    public const string Text = """
        tf2demoview - TF2 demo viewer

        USAGE
          tf2demoview <demo.dem> [more.dem ...] [options]

        OPTIONS
          --help, -h                 Print this and exit.
          --autoplay                 Start playing as soon as the demo is loaded.
          --first-person             Open in the recorder's view rather than the free camera.
          --third-person             Open over a player's shoulder, the chase camera.
          --tick <n>                 Seek here before drawing.
          --shot <path>              Save one frame to path, then exit.
          --spectate <entity>        Follow this entity rather than choosing one.
          --look-at <x> <y>          Point the overhead camera at a world position.
          --zoom <factor>            Overhead camera zoom.
          --surface-colours          Draw surface categories instead of textures.
          --measure <seconds>        Play for this many seconds OF PLAYBACK, print the mean frame
                                     cost, and exit. Not wall clock: loading a map takes about
                                     twenty seconds and does not count against it.
          +<cvar> <value>            Set a cvar for this run only, as Source does. Anything in
                                     settings.cfg works here: fps_max 0, developer 1, and so on.

        ENVIRONMENT
          TF2VIEW_AUTOPLAY           Same as --autoplay.
          TF2VIEW_CAMERA             "x y z pitch yaw" for a headless capture.
          TF2VIEW_CAPTURE_FOLDER     Where --shot and the screenshot key write.
          TF2VIEW_MODEL_CULL         Backface culling mode, for debugging inside-out models.
          TF2VIEW_WINDOW_POS         "x y" window position.
          TF2VIEW_WINDOW_SIZE        "width height" window size.

        NOTES
          The viewer takes the desktop, so run it under the machine-wide lock when anything else
          might be using the screen:

            pwsh run-exclusive.ps1 tf2demoview <demo> --measure 60

          Frame costs are also written to the log every second, but the log is BUFFERED - reading it
          while the viewer is still running shows only what has been flushed. --measure prints to
          stdout on exit, which is why it exists.

        """;
}
