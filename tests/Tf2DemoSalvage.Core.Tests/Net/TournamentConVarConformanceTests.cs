using System;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// That <c>mp_tournament</c>, <c>mp_tournament_stopwatch</c> and <c>mp_winlimit</c> — the replicated
/// cvars `IsInTournamentMode` and the round counter's win limit are modelled from — say what Valve's
/// declarations say.
/// </summary>
/// <remarks>
/// Same shape as <see cref="MovementConVarConformanceTests"/>: written against the SDK's own
/// declarations rather than restating <see cref="EngineConVars"/>. `mp_tournament` and `mp_winlimit`
/// are `teamplayroundbased_gamerules.cpp:202,227`; `mp_tournament_stopwatch` is TF2's own,
/// `tf/tf_gamerules.cpp:797`, so it needs its own source file rather than the shared one.
/// </remarks>
public sealed class TournamentConVarConformanceTests
{
    private static readonly (string Name, string Default, string File)[] Declared =
    [
        ("mp_tournament", "0", "src/game/shared/teamplayroundbased_gamerules.cpp"),
        ("mp_winlimit", "0", "src/game/shared/teamplayroundbased_gamerules.cpp"),
        ("mp_tournament_stopwatch", "1", "src/game/shared/tf/tf_gamerules.cpp"),
    ];

    [Test]
    public void Declarations_ForEveryTournamentConVar_MatchValvesDefault()
    {
        foreach ((string name, string expected, _) in Declared)
        {
            EngineConVars.ByName(name).Default.ShouldBe(expected, $"{name} is declared \"{expected}\" by Valve");
        }
    }

    [Test]
    public void Declarations_ForEveryTournamentConVar_AreReplicatedNotCheat()
    {
        foreach ((string name, _, _) in Declared)
        {
            EngineConVar declared = EngineConVars.ByName(name);
            declared.Replicated.ShouldBeTrue($"{name} carries FCVAR_REPLICATED");
            declared.Cheat.ShouldBeFalse($"{name} is not FCVAR_CHEAT");
        }
    }

    [Test]
    public void Sdk_ForEveryTournamentConVar_DeclaresTheSameDefault()
    {
        foreach ((string name, string expected, string file) in Declared)
        {
            string source = Skip.Unless(SourceSdk.Text(file), SourceSdk.Missing);

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
}
