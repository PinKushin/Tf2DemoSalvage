using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The D226 sidecar a `--shot` writes beside its PNG, and `--like`, which reads a TF2-side one back
/// [no engine counterpart: the sidecar is this project's record of a capture, not anything Source writes].
/// </summary>
public sealed class ShotSidecarTests
{
    private const string Tf2Sidecar = """
        {
          "schema": 1,
          "side": "tf2",
          "captured": "2026-10-19T20:00:00Z",
          "demo": "f12.dem",
          "demoSha256": "77e7",
          "map": "cp_process_f12",
          "tick": 6520,
          "camera": [-2240.5, -3180, 790, 5, 90],
          "viewport": [1280, 720],
          "convars": { "mat_hdr_level": "2", "mat_phong": "0" },
          "notes": "",
          "client": "8835751",
          "unknownField": true
        }
        """;

    private static ShotRecord Record() => new(
        Captured: new DateTimeOffset(2026, 10, 19, 20, 0, 0, TimeSpan.Zero),
        Demo: "f12.dem",
        DemoSha256: "abc",
        Map: "cp_process_f12",
        Tick: 6520,
        Camera: (-2240f, -3180f, 790f, 5f, 90f),
        Viewport: (984, 551),
        Convars: new Dictionary<string, string> { ["mat_phong"] = "1" },
        GitSha: "a1489309",
        Build: "Debug",
        Args: ["--tick", "6520"],
        Env: new Dictionary<string, string> { ["TF2VIEW_CAMERA"] = "-2240 -3180 790 5 90" },
        Like: null);

    [Test]
    public void Json_OfAViewerShot_HasEveryD226FieldWithTheValuesGiven()
    {
        JsonObject json = JsonNode.Parse(ShotSidecar.Json(Record()))!.AsObject();

        json["schema"]!.GetValue<int>().ShouldBe(1);
        json["side"]!.GetValue<string>().ShouldBe("viewer");
        json["captured"]!.GetValue<string>().ShouldBe("2026-10-19T20:00:00Z");
        json["demo"]!.GetValue<string>().ShouldBe("f12.dem");
        json["demoSha256"]!.GetValue<string>().ShouldBe("abc");
        json["map"]!.GetValue<string>().ShouldBe("cp_process_f12");
        json["tick"]!.GetValue<int>().ShouldBe(6520);
        json["camera"]!.ToJsonString().ShouldBe("[-2240,-3180,790,5,90]");
        json["viewport"]!.ToJsonString().ShouldBe("[984,551]");
        json["convars"]!["mat_phong"]!.GetValue<string>().ShouldBe("1");
        json["notes"]!.GetValue<string>().ShouldBe(string.Empty);
        json["gitSha"]!.GetValue<string>().ShouldBe("a1489309");
        json["build"]!.GetValue<string>().ShouldBe("Debug");
        json["args"]!.ToJsonString().ShouldBe("""["--tick","6520"]""");
        json["env"]!["TF2VIEW_CAMERA"]!.GetValue<string>().ShouldBe("-2240 -3180 790 5 90");
        json.ContainsKey("like").ShouldBeTrue();
        json["like"].ShouldBeNull("ours only");
    }

    [Test]
    public void Json_MatchingATf2Sidecar_NamesItsFileAndHash()
    {
        JsonObject json = JsonNode.Parse(ShotSidecar.Json(Record() with { Like = new ShotLikeRef("door.png.json", "ff00") }))!.AsObject();

        json["like"]!["file"]!.GetValue<string>().ShouldBe("door.png.json");
        json["like"]!["sha256"]!.GetValue<string>().ShouldBe("ff00");
    }

    [Test]
    public void Private_ARootedPath_KeepsOnlyTheFileName()
    {
        string rooted = Path.Combine(Path.GetTempPath(), "someone", "out.png");

        ShotSidecar.Private(rooted).ShouldBe("out.png");
        ShotSidecar.Private("6520").ShouldBe("6520");
        ShotSidecar.Private("-2240 -3180 790 5 90").ShouldBe("-2240 -3180 790 5 90");
    }

    /// <remarks>A relative path names folders too; a sidecar is meant to be posted publicly (review).</remarks>
    [TestCase(@"..\..\Users\x\demos\y.dem", "y.dem")]
    [TestCase("sub/out.png", "out.png")]
    [TestCase("/", "/")]
    [TestCase(@"\", @"\")]
    public void Private_AValueWithADirectorySeparator_KeepsOnlyTheFileName(string value, string expected)
    {
        ShotSidecar.Private(value).ShouldBe(expected);
    }

    [Test]
    public void Read_ATf2Sidecar_TakesDemoTickCameraViewportAndConvars()
    {
        ShotLike like = ShotSidecar.Read(Tf2Sidecar);

        like.Demo.ShouldBe("f12.dem");
        like.Tick.ShouldBe(6520);
        like.Camera.ShouldBe((-2240.5f, -3180f, 790f, 5f, 90f));
        like.Viewport.ShouldBe((1280, 720));
        like.Convars.ShouldBe(new Dictionary<string, string> { ["mat_hdr_level"] = "2", ["mat_phong"] = "0" });
    }

    [Test]
    public void Read_ATf2Sidecar_TakesItsDemoHash()
    {
        ShotSidecar.Read(Tf2Sidecar).DemoSha256.ShouldBe("77e7");
    }

    [TestCase("1200.000000", "1200", false)]
    [TestCase("0", "1", true)]
    [TestCase("1", "1", false)]
    [TestCase("tf", "tf", false)]
    [TestCase("custom", "", true)]
    [TestCase("1200 x", "1200", false)]
    [TestCase("0x10", "16", false)]
    [TestCase("nan", "0", true)]
    [TestCase("abc", "0", false)]
    [TestCase("1e39", "1e39", false)]
    [TestCase("Infinity", "Infinity", false)]
    [TestCase("inf", "Infinity", false)]
    [TestCase("16", "0x10", false)]
    [TestCase("nan", "nan", false)]
    [TestCase("-inf", "inf", true)]
    public void DiffersFromDefault_AValueAndItsDeclaredDefault_ComparesNumbersAsNumbers(string value, string declared, bool differs)
    {
        ShotSidecar.DiffersFromDefault(value, declared).ShouldBe(differs);
    }

    [TestCase("a1489309", false, "a1489309")]
    [TestCase("a1489309", true, "a1489309-dirty")]
    [TestCase(null, true, null)]
    public void GitSha_ACommitAndTheTreeState_MarksADirtyTree(string? sha, bool dirty, string? expected)
    {
        ShotSidecar.GitSha(sha, dirty).ShouldBe(expected);
    }

    [Test]
    public void Json_WithNotes_WritesThem()
    {
        JsonNode.Parse(ShotSidecar.Json(Record() with { Notes = "demo differs" }))!["notes"]!.GetValue<string>().ShouldBe("demo differs");
    }

    [Test]
    public void Read_WithoutCameraOrTick_RefusesNamingBoth()
    {
        FormatException refused = Should.Throw<FormatException>(
            () => ShotSidecar.Read("""{ "schema": 1, "demo": "a.dem", "viewport": [1, 1] }"""));

        refused.Message.ShouldContain("tick");
        refused.Message.ShouldContain("camera");
    }

    [Test]
    public void Read_ACameraWithANullNumber_RefusesNamingCamera()
    {
        FormatException refused = Should.Throw<FormatException>(
            () => ShotSidecar.Read("""{ "demo": "a.dem", "tick": 1, "camera": [null, 0, 0, 0, 0], "viewport": [1, 1] }"""));

        refused.Message.ShouldContain("camera");
    }

    private static string SidecarNamingDemo(string demo) =>
        new JsonObject { ["demo"] = demo, ["tick"] = 1, ["camera"] = new JsonArray(0, 0, 0, 0, 0), ["viewport"] = new JsonArray(1, 1) }
            .ToJsonString();

    /// <remarks>A rooted or UNC demo replaces the sidecar's folder in `Path.Combine`, and `..` leaves it (review, high).</remarks>
    [TestCase(@"C:\Users\x\x.dem")]
    [TestCase(@"\\evil\share\x.dem")]
    [TestCase(@"..\x.dem")]
    [TestCase("sub/x.dem")]
    [TestCase("..")]
    [TestCase(".")]
    [TestCase("C:x.dem")]
    public void Read_ADemoThatIsAPath_RefusesIt(string demo)
    {
        Should.Throw<FormatException>(() => ShotSidecar.Read(SidecarNamingDemo(demo))).Message.ShouldContain("demo must be a file name");
    }

    [Test]
    public void Read_ADemoThatIsAFileName_TakesIt()
    {
        ShotSidecar.Read(SidecarNamingDemo("x.dem")).Demo.ShouldBe("x.dem");
    }

    private static string System32(string program) => Path.Combine(Environment.SystemDirectory, program);

    /// <remarks>A git that blocks (a hook, a prompt, a slow disk) must not hold `--shot` open past the timeout (review).</remarks>
    [Test]
    [Platform("Win")]
    public void SaysAnything_AProgramThatOutlivesTheTimeout_IsKilledAndAnswersFalse()
    {
        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        ShotSidecar.SaysAnything(System32("PING.EXE"), ["-n", "15", "127.0.0.1"], 500).ShouldBeFalse();

        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5), "ping runs fourteen seconds; the answer comes at the timeout");
    }

    /// <remarks>The control for the timeout, and for the pipe: more output than a pipe buffers still comes back true.</remarks>
    [Test]
    [Platform("Win")]
    public void SaysAnything_AProgramPrintingMoreThanAPipeHolds_AnswersTrue()
    {
        string big = Path.GetTempFileName();

        try
        {
            File.WriteAllText(big, new string('x', 200_000));

            ShotSidecar.SaysAnything(System32("cmd.exe"), ["/c", "type", big], 10_000).ShouldBeTrue();
        }
        finally
        {
            File.Delete(big);
        }
    }

    [Test]
    [Platform("Win")]
    public void SaysAnything_AProgramThatPrintsThenFails_AnswersFalse()
    {
        ShotSidecar.SaysAnything(System32("cmd.exe"), ["/c", "echo x& exit 1"], 10_000).ShouldBeFalse();
    }

    [Test]
    public void Expand_WithLike_PrependsTickAndConvarsAndSetsCameraAndViewport()
    {
        Dictionary<string, string> set = [];
        string sidecar = Path.Combine("shots", "door.png.json");

        string[] expanded = ShotSidecar.Expand(
            ["x.dem", "--like", sidecar, "--shot", "out.png"], _ => Tf2Sidecar, (name, value) => set[name] = value);

        expanded.ShouldBe(["--tick", "6520", "+mat_hdr_level", "2", "+mat_phong", "0", "x.dem", "--like", sidecar, "--shot", "out.png"]);
        set.ShouldBe(new Dictionary<string, string>
        {
            ["TF2VIEW_CAMERA"] = "-2240.5 -3180 790 5 90",
            ["TF2VIEW_VIEWPORT_SIZE"] = "1280x720",
        });
    }

    /// <remarks>
    /// **`sv_cheats` expands before the cheats it admits** (B811 review 7, Fable): the line runs in its order after the
    /// configs, so `mat_fullbright` sorted before `sv_cheats` would be refused. The rest stay by name.
    /// </remarks>
    [TestCase("sv_cheats")]
    [TestCase("SV_CHEATS")]
    public void Expand_ASidecarWithSvCheats_PutsItBeforeEveryCheat(string spelled)
    {
        string cheats = Tf2Sidecar.Replace(
            "\"convars\": { \"mat_hdr_level\": \"2\", \"mat_phong\": \"0\" }",
            $"\"convars\": {{ \"mat_fullbright\": \"1\", \"{spelled}\": \"1\", \"mat_phong\": \"0\" }}",
            StringComparison.Ordinal);

        cheats.ShouldNotBe(Tf2Sidecar, "the control: the fixture was rewritten");

        string[] expanded = ShotSidecar.Expand(["x.dem", "--like", "s.json"], _ => cheats, (_, _) => { });

        expanded[2..8].ShouldBe([$"+{spelled}", "1", "+mat_fullbright", "1", "+mat_phong", "0"]);
    }

    /// <remarks>
    /// **A sidecar holding a cheat and no `sv_cheats` admits cheats first** (B811 review 8, Fable, Sonnet): a TF2-side
    /// golden's `cl_drawhud 0` was in effect when it was taken, and `--like` refused it. The control: a sidecar with no
    /// cheat gains nothing.
    /// </remarks>
    [Test]
    public void Expand_ASidecarWithACheatAndNoSvCheats_AdmitsCheatsFirst()
    {
        string cheat = Tf2Sidecar.Replace(
            "\"convars\": { \"mat_hdr_level\": \"2\", \"mat_phong\": \"0\" }",
            "\"convars\": { \"cl_drawhud\": \"0\", \"mat_phong\": \"0\" }",
            StringComparison.Ordinal);

        ShotSidecar.Expand(["x.dem", "--like", "s.json"], _ => Tf2Sidecar, (_, _) => { })
            .ShouldNotContain("+sv_cheats", "the control: no cheat, no sv_cheats");

        string[] expanded = ShotSidecar.Expand(["x.dem", "--like", "s.json"], _ => cheat, (_, _) => { });

        expanded[2..8].ShouldBe(["+sv_cheats", "1", "+cl_drawhud", "0", "+mat_phong", "0"]);
    }

    /// <remarks>
    /// **A replicated cheat admits nothing; a client cheat admits at any value** (B811 reviews 9, 10): `cl_forwardspeed` is
    /// the server's, which the line drops; `cl_drawhud 1` is the default, but the run may hold `cl_drawhud 0` from its
    /// saved settings, and the sidecar's default must replace it. The control: `cl_drawhud 0` admits.
    /// </remarks>
    /// <param name="convars">The sidecar's ConVars.</param>
    /// <param name="admits">Whether `+sv_cheats 1` is prepended.</param>
    [TestCase("\"cl_drawhud\": \"0\"", true)]
    [TestCase("\"cl_drawhud\": \"1\"", true)]
    [TestCase("\"cl_forwardspeed\": \"500\"", false)]
    public void Expand_AReplicatedCheat_AdmitsNothingAndAClientCheatAdmitsAtAnyValue(string convars, bool admits)
    {
        string sidecar = Tf2Sidecar.Replace(
            "\"convars\": { \"mat_hdr_level\": \"2\", \"mat_phong\": \"0\" }",
            $"\"convars\": {{ {convars} }}",
            StringComparison.Ordinal);

        sidecar.ShouldNotBe(Tf2Sidecar, "the fixture was rewritten");

        ShotSidecar.Expand(["x.dem", "--like", "s.json"], _ => sidecar, (_, _) => { }).Contains("+sv_cheats").ShouldBe(admits);
    }

    [Test]
    public void Expand_WithLikeAndNoDemo_OpensTheDemoBesideTheSidecar()
    {
        string sidecar = Path.Combine("shots", "door.png.json");

        string[] expanded = ShotSidecar.Expand(["--like", sidecar], _ => Tf2Sidecar, (_, _) => { });

        expanded[^1].ShouldBe(Path.Combine("shots", "f12.dem"));
    }

    private static (ViewerHost Host, StringWriter Error, List<int> Exits) Process()
    {
        StringWriter error = new();
        List<int> exits = [];

        return (new ViewerHost(TextWriter.Null, error, exits.Add, _ => null, () => new FixedTime(0)), error, exits);
    }

    /// <remarks>The view keeps one call (D214); the refusal it used to write itself is reported here (review).</remarks>
    [Test]
    public void Expand_ASidecarItCannotUse_ReportsItExitsWith1AndStops()
    {
        (ViewerHost host, StringWriter error, List<int> exits) = Process();
        using RecordingLoggers log = new();

        string[]? expanded = ShotSidecar.Expand(
            ["--like", "bad.json"],
            _ => """{ "demo": "a.dem", "tick": 1, "camera": [null, 0, 0, 0, 0], "viewport": [1, 1] }""",
            (_, _) => { },
            host,
            log.CreateLogger("viewer"));

        expanded.ShouldBeNull();
        exits.ShouldBe([1]);
        error.ToString().ShouldBe("--like: the sidecar lacks or misspells camera" + Environment.NewLine);
        log.Count("[viewer] --like: the sidecar lacks or misspells camera").ShouldBe(1);
    }

    [Test]
    public void Expand_ASidecarThatCannotBeRead_ReportsItExitsWith1AndStops()
    {
        (ViewerHost host, StringWriter error, List<int> exits) = Process();
        using RecordingLoggers log = new();

        ShotSidecar.Expand(["--like", "gone.json"], _ => throw new FileNotFoundException("gone.json is not there"), (_, _) => { }, host, log.CreateLogger("viewer"))
            .ShouldBeNull();

        exits.ShouldBe([1]);
        error.ToString().ShouldContain("--like: gone.json is not there");
    }

    /// <remarks>The control: a usable sidecar expands as before, and says nothing.</remarks>
    [Test]
    public void Expand_ASidecarItCanUse_ReturnsTheExpansionAndReportsNothing()
    {
        (ViewerHost host, StringWriter error, List<int> exits) = Process();
        using RecordingLoggers log = new();

        string[]? expanded = ShotSidecar.Expand(["x.dem", "--like", "tf2.json"], _ => Tf2Sidecar, (_, _) => { }, host, log.CreateLogger("viewer"));

        expanded.ShouldNotBeNull()[..2].ShouldBe(["--tick", "6520"]);
        exits.ShouldBeEmpty();
        error.ToString().ShouldBeEmpty();
        log.Lines.ShouldBeEmpty();
    }

    [Test]
    public void Expand_WithoutLike_ReturnsTheArgumentsUntouched()
    {
        string[] given = ["x.dem", "--tick", "5"];

        ShotSidecar.Expand(given, _ => throw new InvalidOperationException("nothing to read"), (_, _) => throw new InvalidOperationException("nothing to set"))
            .ShouldBeSameAs(given);
    }
}
