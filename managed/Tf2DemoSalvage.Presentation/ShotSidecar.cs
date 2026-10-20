using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

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
    ShotLikeRef? Like);

/// <summary>What <c>--like</c> takes from a TF2-side sidecar.</summary>
/// <param name="Demo">The demo's file name.</param>
/// <param name="Tick">The tick TF2 actually showed.</param>
/// <param name="Camera">The camera TF2 read back.</param>
/// <param name="Viewport">TF2's view size.</param>
/// <param name="Convars">The ConVars TF2 read back.</param>
public sealed record ShotLike(
    string Demo,
    int Tick,
    (float X, float Y, float Z, float Pitch, float Yaw) Camera,
    (int Width, int Height) Viewport,
    IReadOnlyDictionary<string, string> Convars);

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

    /// <summary>A value with any rooted path reduced to its file name: no user's folders go in a sidecar.</summary>
    /// <param name="value">An argument or an environment value.</param>
    /// <returns>The value, or its file name when it is a rooted path.</returns>
    public static string Private(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.IndexOfAny(Path.GetInvalidPathChars()) < 0 && Path.IsPathRooted(value) ? Path.GetFileName(value) : value;
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
            ["notes"] = string.Empty,
            ["gitSha"] = shot.GitSha,
            ["build"] = shot.Build,
            ["args"] = new JsonArray(shot.Args.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray()),
            ["env"] = Map(shot.Env),
            ["like"] = shot.Like is { } like ? new JsonObject { ["file"] = like.File, ["sha256"] = like.Sha256 } : null,
        };

        return json.ToJsonString(Indented);
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

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
    /// <exception cref="FormatException">It is not JSON, or lacks a field; the message names every one missing.</exception>
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
            convars);
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
        double[] read = node.AsArray().Select(n => n!.GetValue<double>()).ToArray();

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

    /// <summary>The 3D view's size variable; the window reads it (its declaration is the view's).</summary>
    public const string ViewportSizeVariable = "TF2VIEW_VIEWPORT_SIZE";
}
