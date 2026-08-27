using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// That this project's movement ConVar declarations say what Valve's do.
/// </summary>
/// <remarks>
/// **Written before the declarations existed** (D106, 2026-08-27), so it states what the engine
/// does rather than describing what was built. The numbers below were read out of the SDK and out
/// of the game's own `cvarlist.log` before any code named them.
///
/// **The rule being served is that a default is a DECLARATION, not a constant.** The owner:
/// *"baked default is never the right answer i dont think, at least not if its not a baked default
/// valve has"*. Valve wrote a name, a default and the ability for a server to change it; copying
/// only the number keeps the one part that is useless on its own.
///
/// **Two independent sources, and they must agree.** `source-sdk-2013` is a 2013 snapshot and
/// `tf/cvarlist.log` is the installed build's own dump in 2026 — thirteen years apart. Checking
/// both is what distinguishes "this is TF2's value" from "this was TF2's value once", and for these
/// eight the answer is that nothing has moved.
///
/// **`cl_forwardspeed` is declared TWICE in `in_main.cpp` and only one of them is TF2's.** Lines
/// 70–73 are the `CSTRIKE_DLL` branch at 400 with `FCVAR_CHEAT`; lines 76–79 are the `#else` at 450
/// with `FCVAR_REPLICATED | FCVAR_CHEAT`. A grep that takes the first hit gets Counter-Strike's
/// numbers, which is why the SDK check below anchors on the replicated form.
/// </remarks>
public sealed class MovementConVarConformanceTests
{
    /// <summary>What Valve declares, read from both sources before this suite was written.</summary>
    /// <remarks>
    /// Held here rather than taken from <see cref="EngineConVars"/> so the test states an
    /// expectation instead of restating the subject. A conformance test that reads its expected
    /// values out of the thing it is testing cannot fail.
    /// </remarks>
    private static readonly (string Name, string Default, string Sdk)[] Declared =
    [
        ("cl_forwardspeed", "450", "in_main.cpp"),
        ("cl_sidespeed", "450", "in_main.cpp"),
        ("cl_upspeed", "320", "in_main.cpp"),
        ("cl_backspeed", "450", "in_main.cpp"),
        ("sv_maxspeed", "320", "movevars_shared.cpp"),
        ("sv_specspeed", "3", "movevars_shared.cpp"),
        ("sv_specaccelerate", "5", "movevars_shared.cpp"),
        ("sv_specnoclip", "1", "movevars_shared.cpp"),
    ];

    [Test]
    public void Declarations_ForEveryMovementConVar_MatchValvesDefault()
    {
        foreach ((string name, string expected, _) in Declared)
        {
            EngineConVars.ByName(name).Default.ShouldBe(
                expected, $"{name} is declared \"{expected}\" by Valve");
        }
    }

    [Test]
    public void Declarations_ForEveryMovementConVar_AreReplicated()
    {
        foreach ((string name, _, _) in Declared)
        {
            EngineConVars.ByName(name).Replicated.ShouldBeTrue(
                $"{name} carries FCVAR_REPLICATED, so a server may change it for the recording");
        }
    }

    /// <summary>That the SDK still declares each one the way this suite claims.</summary>
    /// <remarks>
    /// **Anchored on `FCVAR_REPLICATED` so the CSTRIKE branch cannot match.** The pattern requires
    /// the name, the quoted default and the replicated flag in one declaration — which the
    /// Counter-Strike copies of the three `cl_` speeds do not have.
    /// </remarks>
    [Test]
    public void Sdk_ForEveryMovementConVar_DeclaresTheSameDefault()
    {
        foreach ((string name, string expected, string file) in Declared)
        {
            string source = Skip.Unless(SourceSdk.Text(SourceFor(file)), SourceSdk.Missing);

            source.ShouldNotBeEmpty($"{file} is readable");

            Match found = Regex.Match(
                source,
                $"""ConVar\s+{Regex.Escape(name)}\s*\(\s*"{Regex.Escape(name)}"\s*,\s*"([^"]*)"\s*,[^)]*FCVAR_REPLICATED""",
                RegexOptions.None,
                TimeSpan.FromSeconds(10));

            found.Success.ShouldBeTrue($"{name} has a replicated declaration in {file}");
            found.Groups[1].Value.ShouldBe(expected);
        }
    }

    /// <summary>That the installed game still ships the same defaults, thirteen years later.</summary>
    /// <remarks>
    /// `cvarlist.log` is the build's own dump, so this is the check that cannot go stale against a
    /// TF2 update — the SDK check above can, because the SDK stopped moving in 2013. See
    /// `docs/memory/nothing-is-closed.md` for the format and the anchored-grep trap.
    /// </remarks>
    [Test]
    public void ShippedCvarList_ForEveryMovementConVar_AgreesWithTheSdk()
    {
        string listing = System.IO.File.ReadAllText(GameInstall.RequireFile("cvarlist.log"));

        foreach ((string name, string expected, _) in Declared)
        {
            Match row = Regex.Match(
                listing,
                $"^{Regex.Escape(name)} +: +([^ ]+) +: *([^:]*):",
                RegexOptions.Multiline,
                TimeSpan.FromSeconds(10));

            row.Success.ShouldBeTrue($"{name} appears in the game's own convar dump");
            row.Groups[1].Value.ShouldBe(expected);
            row.Groups[2].Value.ShouldContain("\"rep\"");
        }
    }

    /// <summary>That the free camera's speed is derived, not typed.</summary>
    /// <remarks>
    /// **960 units a second, and it is `sv_maxspeed * sv_specspeed`** — B215 measured it from
    /// `FullNoClipMove` (`gamemovement.cpp:2260`), which opens
    /// `float maxspeed = sv_maxspeed.GetFloat() * factor;`. The point of asserting it here rather
    /// than in the camera's own tests is that the two multiplicands must come from the DECLARATIONS:
    /// a test against `320f * 3f` would pass against constants and prove nothing about D106.
    /// </remarks>
    [Test]
    public void SpectatorSpeed_FromTheDeclaredDefaults_Is960()
    {
        float maximum = EngineConVars.ByName("sv_maxspeed").Number;
        float scale = EngineConVars.ByName("sv_specspeed").Number;

        (maximum * scale).ShouldBe(960f);
    }

    /// <summary>That an unknown name is refused rather than invented.</summary>
    /// <remarks>
    /// The alternative — returning a declaration with an empty default — is the sentinel trap:
    /// a caller reading zero for `sv_maxspeed` would fly nowhere and nothing would say why. See
    /// [[sentinels-conflate-unknown-with-answer]] in the memory directory.
    /// </remarks>
    [Test]
    public void ByName_ForAConVarNobodyDeclared_Throws()
    {
        Should.Throw<KeyNotFoundException>(() => EngineConVars.ByName("sv_not_a_real_convar"));
    }

    private static string SourceFor(string file) => file switch
    {
        "in_main.cpp" => "src/game/client/in_main.cpp",
        "movevars_shared.cpp" => "src/game/shared/movevars_shared.cpp",
        _ => throw new ArgumentOutOfRangeException(nameof(file), file, "no path known for this file"),
    };
}
