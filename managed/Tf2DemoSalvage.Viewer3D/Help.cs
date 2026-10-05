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
          --spectate <who>           First-person a specific player: a NAME, a Steam id
                                    ([U:1:n], STEAM_0:y:z or 7656...), a user id or an
                                    entity index. A name matches case-insensitively, exactly
                                    first and then by part. A point-of-view demo ignores this
                                    and uses its own recorded camera.
          --look <x> <y>             Point the overhead camera at a world position.
          --zoom <factor>            Overhead camera zoom.
          --colours                  Draw surface categories instead of textures.
          --hud <folder or .vpk>     Draw this HUD, laid out as in tf/custom. Without it the
                                     HUD is TF2's stock one; tf/custom is not read for it.
          --measure <seconds>        Play for this many seconds OF PLAYBACK, print the mean frame
                                     cost, and exit. Not wall clock: loading a map takes about
                                     twenty seconds and does not count against it.
          --then-seek <tick>         With --measure: then pause, seek to this tick and measure the
                                     paused frame as long again (mean ms, phases, live particles).
          +<cvar> <value>            Set a cvar for this run only, as Source does. Anything in
                                     settings.cfg works here: fps_max 0, developer 1, and so on.

        SETTINGS
          Everything a user sets lives in %LOCALAPPDATA%\Tf2DemoSalvage\settings.cfg, written in
          TF2's own config syntax; lines it does not know are ignored. Among them:
          cl_game_folder <path>      Your TF2 tf folder, when Steam's records do not find it.
                                     File > TF2 folder sets it; the viewer asks at startup
                                     when TF2 is not found.
          cl_game_folder_ask 0       Stop asking for the tf folder at startup.
          cl_screenshot_folder <path> Where --shot and the screenshot key write.

        ENVIRONMENT (scripts, CI and debugging only; nothing a user needs)
          TF2_FOLDER                 A tf folder that beats both cl_game_folder and Steam.
          TF2VIEW_AUTOPLAY           Same as --autoplay.
          TF2VIEW_CAMERA             "x y z pitch yaw" for a headless capture.
          TF2VIEW_MODEL_CULL         Backface culling mode, for debugging inside-out models.
          TF2VIEW_SETTINGS           A settings file to use instead of the one above (tests).
          TF2VIEW_STEAM_ROOT         The only Steam folder discovery looks in (tests).
          TF2VIEW_PICK             "x y": log what lies under that pixel of a --shot.
          TF2VIEW_WARP               Draw through WARP, reproducing a machine with no GPU.
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
