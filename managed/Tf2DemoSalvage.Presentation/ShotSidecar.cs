using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging;

namespace Tf2DemoSalvage.Presentation;

/// <summary>A TF2-side sidecar named by <c>--like</c>: its file name and its sha256 (D226).</summary>
/// <param name="File">The sidecar's file name, never its folder.</param>
/// <param name="Sha256">Its bytes' sha256, lower-case hex.</param>
public sealed record ShotLikeRef(string File, string Sha256);

/// <summary>What one viewer capture depended on, each value carried from where the capture used it (D226, B243).</summary>
/// <param name="Captured">When the capture was taken.</param>
/// <param name="Demo">The demo's file name.</param>
/// <param name="DemoSha256">The demo's sha256.</param>
/// <param name="Map">The map the demo names.</param>
/// <param name="Tick">The tick the capture showed.</param>
/// <param name="Camera">The eye the last frame was drawn from, or null when no frame had a world.</param>
/// <param name="Viewport">The 3D view's size.</param>
/// <param name="Convars">The settings in force, by command name.</param>
/// <param name="GitSha">The commit built, or null when the build did not record one.</param>
/// <param name="Build">Debug or Release.</param>
/// <param name="Args">The command line, paths reduced to file names.</param>
/// <param name="Env">Every <c>TF2VIEW_*</c> set, paths reduced to file names.</param>
/// <param name="Like">The TF2 sidecar this shot matches, or null for "ours only".</param>
public sealed record ShotRecord(
    DateTimeOffset Captured,
    string? Demo,
    string? DemoSha256,
    string? Map,
    int Tick,
    (float X, float Y, float Z, float Pitch, float Yaw)? Camera,
    (int Width, int Height) Viewport,
    IReadOnlyDictionary<string, string> Convars,
    string? GitSha,
    string? Build,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string> Env,
    ShotLikeRef? Like)
{
    /// <summary>Free text: what the capture found worth saying, such as a demo that is not the TF2 side's.</summary>
    public string Notes { get; init; } = string.Empty;
}

/// <summary>What <c>--like</c> takes from a TF2-side sidecar.</summary>
/// <param name="Demo">The demo's file name.</param>
/// <param name="Tick">The tick TF2 actually showed.</param>
/// <param name="Camera">The camera TF2 read back.</param>
/// <param name="Viewport">TF2's view size.</param>
/// <param name="Convars">The ConVars TF2 read back.</param>
/// <param name="DemoSha256">The demo TF2 played, by hash; null when the sidecar does not say.</param>
public sealed record ShotLike(
    string Demo,
    int Tick,
    (float X, float Y, float Z, float Pitch, float Yaw) Camera,
    (int Width, int Height) Viewport,
    IReadOnlyDictionary<string, string> Convars,
    string? DemoSha256 = null);

/// <summary>The D226 sidecar: <c>&lt;shot&gt;.json</c> written beside every <c>--shot</c>, and read back by <c>--like</c>.</summary>
/// <remarks>
/// **The format is D226's and is stated there in full**; this is its writer and its reader. Schema 1. The reader keeps
/// no unknown field because it has no use for one; it refuses a sidecar missing anything `--like` needs.
/// </remarks>
public static class ShotSidecar
{
    /// <summary>The format's version.</summary>
    public const int Schema = 1;

    /// <summary>The option naming a TF2-side sidecar to reproduce.</summary>
    public const string LikeOption = "--like";

    /// <summary>The sidecar's path for a capture's.</summary>
    /// <param name="shot">The PNG.</param>
    /// <returns><c>&lt;shot&gt;.json</c>.</returns>
    public static string PathFor(string shot) => shot + ".json";

    /// <summary>A value holding any directory separator reduced to its file name: no user's folders go in a sidecar.</summary>
    /// <param name="value">An argument, an environment value or a ConVar's value.</param>
    /// <returns>The value, or its file name when it names a folder; a lone separator (a key name) is kept.</returns>
    /// <remarks>**Relative paths too**: `..\..\Users\x\demos\y.dem` names folders as surely as a rooted one does.</remarks>
    public static string Private(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        // **Both separators, by hand, on every platform**: `Path.GetFileName` knows only the running platform's, so on
        // Linux (where CI runs the tests) `..\..\Users\x\y.dem` would pass through whole.
        string name = value[(value.AsSpan().LastIndexOfAny('/', '\\') + 1)..];

        return name.Length > 0 ? name : value;
    }

    /// <summary>A file's sha256, lower-case hex.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The hash.</returns>
    public static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);

        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
    }

    /// <summary>The sidecar's text.</summary>
    /// <param name="shot">What the capture used.</param>
    /// <returns>Indented JSON.</returns>
    public static string Json(ShotRecord shot)
    {
        ArgumentNullException.ThrowIfNull(shot);

        JsonObject json = new()
        {
            ["schema"] = Schema,
            ["side"] = "viewer",
            ["captured"] = shot.Captured.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["demo"] = shot.Demo,
            ["demoSha256"] = shot.DemoSha256,
            ["map"] = shot.Map,
            ["tick"] = shot.Tick,
            ["camera"] = shot.Camera is { } c ? new JsonArray(c.X, c.Y, c.Z, c.Pitch, c.Yaw) : null,
            ["viewport"] = new JsonArray(shot.Viewport.Width, shot.Viewport.Height),
            ["convars"] = Map(shot.Convars),
            ["notes"] = shot.Notes,
            ["gitSha"] = shot.GitSha,
            ["build"] = shot.Build,
            ["args"] = new JsonArray(shot.Args.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray()),
            ["env"] = Map(shot.Env),
            ["like"] = shot.Like is { } like ? new JsonObject { ["file"] = like.File, ["sha256"] = like.Sha256 } : null,
        };

        return json.ToJsonString(Indented);
    }

    /// <summary>Indented, and `+mat_phong` written as typed rather than as `+` (a file read by people, not HTML).</summary>
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static JsonObject Map(IReadOnlyDictionary<string, string> values)
    {
        JsonObject map = [];

        foreach ((string name, string value) in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            map[name] = value;
        }

        return map;
    }

    /// <summary>Reads what <c>--like</c> needs from a sidecar.</summary>
    /// <param name="text">The sidecar.</param>
    /// <returns>Its demo, tick, camera, viewport and ConVars.</returns>
    /// <exception cref="FormatException">
    /// It is not JSON, or lacks a field (the message names every one missing), or its demo is a path rather than a file name.
    /// </exception>
    public static ShotLike Read(string text)
    {
        JsonObject json;

        try
        {
            json = JsonNode.Parse(text)?.AsObject() ?? throw new FormatException("the sidecar is empty");
        }
        catch (JsonException bad)
        {
            throw new FormatException($"the sidecar is not JSON: {bad.Message}", bad);
        }
        catch (InvalidOperationException bad)
        {
            throw new FormatException("the sidecar is not a JSON object", bad);
        }

        List<string> missing = [];
        string? demo = Field(json, "demo", missing, node => node.GetValue<string>());
        int tick = Field(json, "tick", missing, node => node.GetValue<int>());
        float[]? camera = Field(json, "camera", missing, node => Numbers(node, 5).Select(n => (float)n).ToArray());
        double[]? viewport = Field(json, "viewport", missing, node => Numbers(node, 2));

        if (missing.Count > 0)
        {
            throw new FormatException($"the sidecar lacks or misspells {string.Join(", ", missing)}");
        }

        // **A file name and nothing else**: `Expand` joins it to the sidecar's folder, and a rooted or UNC value would
        // replace that folder outright (an SMB path opened before anyone sees the name), and `..` would leave it.
        // Both separators and a drive colon are refused by hand: `Path.GetFileName` would let a Windows path through on Linux.
        if (demo.AsSpan().IndexOfAny('/', '\\', ':') >= 0 || demo is "." or "..")
        {
            throw new FormatException($"demo must be a file name, not \"{demo}\"");
        }

        Dictionary<string, string> convars = new(StringComparer.Ordinal);

        if (json["convars"] is JsonObject values)
        {
            foreach ((string name, JsonNode? value) in values)
            {
                convars[name] = value is JsonValue v && v.TryGetValue(out string? s) ? s : value?.ToJsonString() ?? string.Empty;
            }
        }

        return new ShotLike(
            demo!,
            tick,
            (camera![0], camera[1], camera[2], camera[3], camera[4]),
            ((int)viewport![0], (int)viewport[1]),
            convars,
            json["demoSha256"] is JsonValue hash && hash.TryGetValue(out string? sha) ? sha : null);
    }

    /// <summary>Whether a ConVar's value is not its declared default; numbers compare as numbers, so "1200.000000" is "1200".</summary>
    /// <param name="value">The value held.</param>
    /// <param name="declared">The declaration's default string.</param>
    /// <returns>True when the sidecar should record it.</returns>
    public static bool DiffersFromDefault(string value, string declared)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(declared);

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double held)
            && double.TryParse(declared, NumberStyles.Float, CultureInfo.InvariantCulture, out double fallback))
        {
            // A tolerance rather than `!=` (S1244); a ConVar's value is written to six places at most.
            return Math.Abs(held - fallback) > 1e-6;
        }

        return !string.Equals(value, declared, StringComparison.Ordinal);
    }

    /// <summary>Whether the git working tree holding a folder has uncommitted changes: <c>git status --porcelain</c> says anything.</summary>
    /// <param name="folder">A folder inside the tree; the viewer's own, so the tree is the one it was built from.</param>
    /// <returns>False when git is absent or the folder is in no tree, since then there is no tree to be dirty.</returns>
    /// <remarks>
    /// Fixed arguments, nothing from the user reaches the command line. Ignored files (bin/, obj/) do not count. **The tree
    /// asked is the one around the folder given**, <see cref="AppContext.BaseDirectory"/> in the viewer: a release copied
    /// into some unrelated dirty repository reports <c>-dirty</c> for that repository, not for the build. A git that has
    /// not answered in ten seconds is killed and the tree called clean.
    /// </remarks>
    public static bool WorkingTreeDirty(string folder)
    {
        // **Resolved to an absolute path first** (S4036): the PATH entry that holds git.exe, named outright, so the
        // process started is the one found and not whatever a working-directory lookup would add.
        string? found = (System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => Path.Combine(entry.Trim('"'), OperatingSystem.IsWindows() ? "git.exe" : "git"))
            .FirstOrDefault(candidate => Path.IsPathRooted(candidate) && File.Exists(candidate));

        if (found is null)
        {
            // No git on this machine: there is no tree to call dirty, and the sidecar keeps the bare commit.
            return false;
        }

        return SaysAnything(found, ["-C", folder, "status", "--porcelain"], GitTimeoutMilliseconds);
    }

    /// <summary>How long git is given before it is killed and the tree called clean.</summary>
    private const int GitTimeoutMilliseconds = 10_000;

    /// <summary>Whether a program exits 0 within the time given, having printed anything: the launch under <see cref="WorkingTreeDirty"/>.</summary>
    /// <param name="executable">The program, by absolute path.</param>
    /// <param name="arguments">Its fixed arguments.</param>
    /// <param name="timeoutMilliseconds">How long it is given before it is killed and the answer is false.</param>
    /// <returns>True when it exited 0 in time with output on stdout.</returns>
    /// <remarks>**Separate so a test can hand it a program that hangs**, a `Process` having no stand-in.</remarks>
    internal static bool SaysAnything(string executable, IEnumerable<string> arguments, int timeoutMilliseconds)
    {
        System.Diagnostics.ProcessStartInfo start = new(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using System.Diagnostics.Process git = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("git did not start");

            // **Both streams drained while it runs, and the wait is what is timed**: reading stdout to its end first left
            // the timeout dead behind it, and waiting before reading deadlocks once a dirty tree's list fills the pipe.
            // stderr stays redirected so git's "not a git repository" never lands on the viewer's own stderr.
            System.Threading.Tasks.Task<string> output = git.StandardOutput.ReadToEndAsync();
            _ = git.StandardError.ReadToEndAsync();

            if (!git.WaitForExit(timeoutMilliseconds))
            {
                git.Kill(entireProcessTree: true);

                return false;
            }

            // **The exit code is checked before the output is believed**: outside a tree git prints nothing and fails.
            return git.ExitCode == 0 && output.GetAwaiter().GetResult().Length > 0;
        }
        catch (System.ComponentModel.Win32Exception absent)
        {
            // The git found would not start: treated as no tree, and said where a debugger listens.
            System.Diagnostics.Trace.TraceWarning($"git at {executable} did not start for gitSha: {absent.Message}");

            return false;
        }
    }

    /// <summary>The commit as a sidecar records it: <c>-dirty</c> when the tree held uncommitted changes.</summary>
    /// <param name="sha">The commit the build recorded, or null.</param>
    /// <param name="dirty">Whether the working tree was dirty at the shot.</param>
    /// <returns>The value, or null when no commit was recorded.</returns>
    public static string? GitSha(string? sha, bool dirty)
    {
        if (sha is null || !dirty)
        {
            return sha;
        }

        return sha + "-dirty";
    }

    private static T? Field<T>(JsonObject json, string name, List<string> missing, Func<JsonNode, T> read)
    {
        try
        {
            if (json[name] is { } node)
            {
                return read(node);
            }
        }
        catch (Exception wrong) when (wrong is InvalidOperationException or FormatException)
        {
            // **Reported with the rest**, as a misspelling: a refusal that names one field at a time costs a run per field.
            missing.Add(name);

            return default;
        }

        missing.Add(name);

        return default;
    }

    private static double[] Numbers(JsonNode node, int count)
    {
        // **A null or a string is a misspelling like any other**, refused inside `Field`'s catch rather than as an NRE past it.
        double[] read = node.AsArray()
            .Select(n => n is JsonValue v && v.TryGetValue(out double d) ? d : throw new FormatException($"{n?.ToJsonString() ?? "null"} is not a number"))
            .ToArray();

        return read.Length == count ? read : throw new FormatException($"{count} numbers wanted, {read.Length} given");
    }

    /// <summary>
    /// Turns <c>--like &lt;sidecar&gt;</c> into what the viewer already obeys: <c>--tick</c>, a <c>+cvar value</c> per
    /// ConVar, <c>TF2VIEW_CAMERA</c> and <c>TF2VIEW_VIEWPORT_SIZE</c>, and the demo beside the sidecar when none is named.
    /// </summary>
    /// <param name="arguments">The command line.</param>
    /// <param name="readFile">Reads the sidecar.</param>
    /// <param name="setEnvironment">Sets a process variable, before anything reads it.</param>
    /// <returns>The arguments to run with; the same array when there is no <c>--like</c>.</returns>
    /// <exception cref="FormatException">The sidecar cannot be used; the message says why.</exception>
    /// <remarks>
    /// **Expanded into the existing switches rather than a second route to each value**, so the camera, the size and
    /// the tick are placed by exactly the code a hand-typed run uses. Prepended, so an option typed after it still
    /// wins. <c>--like</c> itself stays in the list, for <see cref="LaunchOptions.Like"/> to record.
    /// </remarks>
    public static string[] Expand(string[] arguments, Func<string, string> readFile, Action<string, string> setEnvironment)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(readFile);
        ArgumentNullException.ThrowIfNull(setEnvironment);

        int at = Array.IndexOf(arguments, LikeOption);

        if (at < 0 || at == arguments.Length - 1)
        {
            return arguments;
        }

        string path = arguments[at + 1];
        ShotLike like = Read(readFile(path));

        (float x, float y, float z, float pitch, float yaw) = like.Camera;
        setEnvironment(FreeCameraController.CameraVariable, string.Create(CultureInfo.InvariantCulture, $"{x} {y} {z} {pitch} {yaw}"));
        setEnvironment(ViewportSizeVariable, string.Create(CultureInfo.InvariantCulture, $"{like.Viewport.Width}x{like.Viewport.Height}"));

        List<string> expanded = ["--tick", like.Tick.ToString(CultureInfo.InvariantCulture)];

        foreach ((string name, string value) in like.Convars.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            expanded.Add("+" + name);
            expanded.Add(value);
        }

        expanded.AddRange(arguments);

        if (!arguments.Any(a => a.EndsWith(".dem", StringComparison.OrdinalIgnoreCase)))
        {
            expanded.Add(Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, like.Demo));
        }

        return [.. expanded];
    }

    /// <summary><see cref="Expand(string[], Func{string, string}, Action{string, string})"/>, with a sidecar it cannot use reported rather than thrown.</summary>
    /// <param name="arguments">The command line.</param>
    /// <param name="readFile">Reads the sidecar.</param>
    /// <param name="setEnvironment">Sets a process variable, before anything reads it.</param>
    /// <param name="host">The process: the refusal goes to its standard error and sets its exit code to 1.</param>
    /// <param name="log">Where the refusal is logged.</param>
    /// <returns>The arguments to run with, or null when the run must stop.</returns>
    public static string[]? Expand(string[] arguments, Func<string, string> readFile, Action<string, string> setEnvironment, ViewerHost host, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(log);

        try
        {
            return Expand(arguments, readFile, setEnvironment);
        }
        catch (Exception refused) when (refused is FormatException or IOException or UnauthorizedAccessException)
        {
            // **Said once to each place a user might be looking, and the run stops**: a capture taken without the sidecar's
            // values would be a picture of something else, labelled as the TF2 one.
            log.LogError(refused, "{Message}", $"--like: {refused.Message}");
            host.Error.WriteLine($"--like: {refused.Message}");
            host.SetExitCode(1);

            return null;
        }
    }

    /// <summary>The 3D view's size variable; the window reads it (its declaration is the view's).</summary>
    public const string ViewportSizeVariable = "TF2VIEW_VIEWPORT_SIZE";
}
