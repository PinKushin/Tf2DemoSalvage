using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// That every command this viewer answers to is Valve's name, or a declared exception.
/// </summary>
/// <remarks>
/// **D104, and the owner's reason for it**: *"we are going to go through all valves settings, see
/// which ones we need to import, and fill out our cvar list completely so we can stop forgetting to
/// make shit bindable the right way"*.
///
/// **The denominator is generated, not written down.** `tf/cvarlist.log` is the game's own dump —
/// 3,668 entries, 2,660 convars and 1,008 concommands — so this cannot go stale the way a hand-kept
/// list does (`docs/memory/a-walking-test-cannot-see-a-deletion.md`). What a hand-kept list CAN do
/// is say why a name is ours, which is what the exception table below is for.
///
/// **Why a wrong name is a real defect rather than a cosmetic one.** D69 is that a real TF2 config
/// must work wholesale, and D101 is that every control goes through the config. A viewer that calls
/// something `texture_quality` when the engine calls it `mat_picmip` silently ignores the line a
/// user actually pasted — and ignoring unknown commands is the feature, so nothing complains. The
/// failure is indistinguishable from the setting having no effect.
///
/// **This does not check behaviour.** A name matching Valve's says nothing about whether the value
/// means the same thing; `mat_picmip` counts mip levels dropped and this project's quality setting
/// does not. Those are separate questions and this answers only the first.
/// </remarks>
public sealed class CvarNameConformanceTests
{
    /// <summary>
    /// Names this viewer defines itself, each with the reason Valve's list has no equivalent.
    /// </summary>
    /// <remarks>
    /// **An entry here is a claim that was checked**, not a place to put anything inconvenient. Each
    /// was searched for in `cvarlist.log` before being listed, and the search is repeatable: the
    /// test fails if Valve turns out to ship the name after all, which is the case worth catching.
    /// </remarks>
    private static readonly Dictionary<string, string> Ours = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mat_fullscreen_mode"] =
            "Valve has no fullscreen convar at all — no `fullscreen` and no `videomode` entry " +
            "exists in the shipped list. `mat_setvideomode` is a command taking width, height and " +
            "windowed, which answers a different question.",

        ["mat_surfacecolours"] =
            "The category view is this project's own diagnostic; Valve ships nothing that colours " +
            "surfaces by what they are.",

        ["cl_screenshot_folder"] =
            "Valve has `cl_screenshotname` for a custom NAME and Steam's own folder for the file. " +
            "Neither names a directory.",

        ["resetcamera"] =
            "The free camera is this project's; Valve's spectator has no reset.",

        ["togglefullscreen"] =
            "Follows `mat_fullscreen_mode` above — there is nothing of Valve's to bind to.",

        ["opendemo"] =
            "D79 rule 2. `playdemo` is Valve's and takes a demo NAME; this action opens a picker, " +
            "which the engine has no command for. That `playdemo <name>` itself is unimplemented " +
            "is a separate gap and is recorded as one.",

        ["resetspeed"] =
            "`demo_timescale 1` would be the faithful spelling and was rejected deliberately: this " +
            "action also clears REVERSE, which the engine cannot express at all (D97). There is no " +
            "command of Valve's that means what this one means.",

        ["texture_quality"] =
            "PROVISIONAL — the one entry here that is a question rather than an answer. Valve ships " +
            "`mat_picmip` (default -1, archived) for texture quality, so unlike every other name " +
            "above there IS an engine convar in this area. It is not a rename: picmip counts mip " +
            "levels DROPPED and this setting is a quality enum, so adopting the name means adopting " +
            "the scale. Filed under D104 as a decision, not carried as a justification.",
    };

    [Test]
    public void EveryCommandThisViewerAnswersTo_IsValvesNameOrADeclaredException()
    {
        if (Cvars is not { } shipped)
        {
            Assert.Ignore("Team Fortress 2 is not installed, so the shipped cvar list is unreadable.");
            return;
        }

        // The control: a mistyped path or a changed format would leave this empty, and an empty
        // denominator passes every membership test below while checking nothing.
        shipped.Count.ShouldBeGreaterThan(
            2000, "the shipped list is around 3,668 entries; far fewer means it was not parsed");

        List<string> invented = [];

        foreach (string command in Named().Select(Verb).Distinct())
        {
            if (shipped.Contains(command) || Ours.ContainsKey(command))
            {
                continue;
            }

            invented.Add(command);
        }

        TestContext.Out.WriteLine(
            $"CVAR NAMES: {shipped.Count} shipped, {Named().Count} named by this viewer " +
            $"({KeyBindings.Commands.Count} bound, {Named().Count - KeyBindings.Commands.Count} " +
            $"settings), {Ours.Count} declared ours, {invented.Count} unaccounted" +
            $"{(invented.Count > 0 ? ": " + string.Join(", ", invented) : string.Empty)}");

        invented.ShouldBeEmpty(
            "a command the engine does not know, and that is not declared as this project's own, " +
            "is a name a pasted config will never reach (D69, D101)");
    }

    [Test]
    public void EveryNameDeclaredAsOurs_IsStillAbsentFromValvesList()
    {
        // **The exception table's own control.** An entry saying "Valve has no such name" stops
        // being true the moment Valve ships one, and nothing else would notice — the first test
        // would keep passing, because the table would keep excusing it.
        if (Cvars is not { } shipped)
        {
            Assert.Ignore("Team Fortress 2 is not installed, so the shipped cvar list is unreadable.");
            return;
        }

        List<string> claimed = [.. Ours.Keys.Where(shipped.Contains)];

        claimed.ShouldBeEmpty(
            "these are declared as this project's own because the engine has no equivalent; the " +
            "shipped list now says otherwise, so the declaration is stale and the name should move");
    }

    /// <summary>Every command name this viewer answers to, from BOTH surfaces.</summary>
    /// <remarks>
    /// **Two surfaces, and checking one of them was how `texture_quality` went unexamined.** A name
    /// reaches this viewer either as a bound action (`KeyBindings.Commands`) or as a settings
    /// command (`ViewerSettings`'s `*Command` constants), and a config line does not know or care
    /// which — so a check that reads only the first has a blind half.
    ///
    /// The settings half is read by reflection rather than from a list, so a constant added
    /// tomorrow is covered without anyone remembering to add it here
    /// (`docs/memory/a-walking-test-cannot-see-a-deletion.md`).
    /// </remarks>
    private static List<string> Named()
    {
        List<string> names = [.. KeyBindings.Commands.Values];

        names.AddRange(
            typeof(ViewerSettings)
                .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                .Where(field => field.Name.EndsWith("Command", StringComparison.Ordinal))
                .Select(field => (string?)field.GetRawConstantValue())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!));

        return names;
    }

    /// <summary>The command word, without an argument — `mat_fullbright 1` binds under its verb.</summary>
    private static string Verb(string command) =>
        command.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [string first, ..] ? first : command;

    /// <summary>Every convar and concommand the installed game reports, or null with no install.</summary>
    /// <remarks>
    /// **The game's own dump rather than a header** (`docs/findings/40`). Each line is
    /// <c>name : default : flags : help</c>, so the name is everything before the first colon.
    /// </remarks>
    private static HashSet<string>? Cvars
    {
        get
        {
            if (Tf2Install.Folder is not { } tf)
            {
                return null;
            }

            string path = Path.Combine(tf, "cvarlist.log");

            if (!File.Exists(path))
            {
                return null;
            }

            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

            foreach (string line in File.ReadLines(path))
            {
                int colon = line.IndexOf(':', StringComparison.Ordinal);

                if (colon <= 0)
                {
                    continue;
                }

                string name = line[..colon].Trim();

                if (name.Length > 0 && !name.Contains(' ', StringComparison.Ordinal))
                {
                    names.Add(name);
                }
            }

            return names;
        }
    }
}
