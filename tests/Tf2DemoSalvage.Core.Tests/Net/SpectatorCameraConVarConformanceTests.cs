using System;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// The two ConVars `CTFSpectatorGUI::UpdateKeyLabels` (tf_spectatorgui.cpp:562, :595) hides a dead player's camera keys by
/// (B733), as gamevars_shared.cpp declares them.
/// </summary>
/// <remarks>
/// `mp_forcecamera` (:24) is `"0"` under `CSTRIKE` and `"1"` otherwise — TF2 builds the second — and `mp_fadetoblack` (:51)
/// is `"0"`; both `FCVAR_REPLICATED`. The `#ifdef` inside the first declaration defeats the single-regex SDK check the
/// movement suite uses, so the second witness here is the game's own `cvarlist.log`.
/// </remarks>
public sealed class SpectatorCameraConVarConformanceTests
{
    private static readonly (string Name, string Default)[] Declared =
    [
        ("mp_forcecamera", "1"),
        ("mp_fadetoblack", "0"),
    ];

    [Test]
    public void Declarations_ForTheSpectatorCameraConVars_AreValvesReplicatedDefaults()
    {
        foreach ((string name, string expected) in Declared)
        {
            EngineConVar declared = EngineConVars.ByName(name);

            declared.Default.ShouldBe(expected, name);
            declared.Replicated.ShouldBeTrue(name);
        }
    }

    [Test]
    public void ShippedCvarList_ForTheSpectatorCameraConVars_AgreesWithTheDeclarations()
    {
        string listing = System.IO.File.ReadAllText(GameInstall.RequireFile("cvarlist.log"));

        foreach ((string name, string expected) in Declared)
        {
            Match row = Regex.Match(
                listing, $"^{Regex.Escape(name)} +: +([^ ]+) +: *([^:]*):", RegexOptions.Multiline, TimeSpan.FromSeconds(10));

            row.Success.ShouldBeTrue($"{name} appears in the game's own convar dump");
            row.Groups[1].Value.ShouldBe(expected);
            row.Groups[2].Value.ShouldContain("\"rep\"");
        }
    }
}
