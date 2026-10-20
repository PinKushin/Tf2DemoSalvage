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

    private static string SidecarNamingDemo(string demo) =>
        new JsonObject { ["demo"] = demo, ["tick"] = 1, ["camera"] = new JsonArray(0, 0, 0, 0, 0), ["viewport"] = new JsonArray(1, 1) }
            .ToJsonString();

    /// <remarks>A rooted or UNC demo replaces the sidecar's folder in `Path.Combine`, and `..` leaves it (review, high).</remarks>
    [TestCase(@"C:\Users\x\x.dem")]
    [TestCase(@"\\evil\share\x.dem")]
    [TestCase(@"..\x.dem")]
    [TestCase("sub/x.dem")]
    public void Read_ADemoThatIsAPath_RefusesIt(string demo)
    {
        Should.Throw<FormatException>(() => ShotSidecar.Read(SidecarNamingDemo(demo))).Message.ShouldContain("demo must be a file name");
    }

    [Test]
    public void Read_ADemoThatIsAFileName_TakesIt()
    {
        ShotSidecar.Read(SidecarNamingDemo("x.dem")).Demo.ShouldBe("x.dem");
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

    [Test]
    public void Expand_WithLikeAndNoDemo_OpensTheDemoBesideTheSidecar()
    {
        string sidecar = Path.Combine("shots", "door.png.json");

        string[] expanded = ShotSidecar.Expand(["--like", sidecar], _ => Tf2Sidecar, (_, _) => { });

        expanded[^1].ShouldBe(Path.Combine("shots", "f12.dem"));
    }

    [Test]
    public void Expand_WithoutLike_ReturnsTheArgumentsUntouched()
    {
        string[] given = ["x.dem", "--tick", "5"];

        ShotSidecar.Expand(given, _ => throw new InvalidOperationException("nothing to read"), (_, _) => throw new InvalidOperationException("nothing to set"))
            .ShouldBeSameAs(given);
    }
}
