using System;
using System.IO;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// The four sound ConVars, read from <c>engine.dll</c>'s own registrations — and the one place
/// <c>cvarlist.log</c> disagrees with them.
/// </summary>
/// <remarks>
/// **These are absent from the SDK entirely**, so unlike the movement set there is no published
/// declaration to check against. The values below were read from the engine's ConVar registrations
/// on 2026-08-27 by the method in `docs/memory/a-default-is-not-a-constant.md`: find the single
/// `push` of the name string, read the two pushes before it, follow the default pointer.
///
/// <code>
/// push 0x00004000     ; flags = FCVAR_CHEAT
/// push &lt;default&gt;  ; the default, as a string pointer
/// push &lt;name&gt;     ; "snd_gain_min"
/// </code>
///
/// **`snd_gain_min` is the one that matters, because the game's own dump is WRONG about it.**
/// `tf/cvarlist.log` prints `snd_gain_min : 0`; the registration's default pointer resolves to the
/// literal `"0.01"`. The dump reports the value in force at the moment it was written, which is the
/// default only when nothing has changed it — and engine code may set a ConVar at startup whatever
/// its flags say. `snd_gain_min` is not archived and is cheat-protected, so "the user cannot have
/// changed it" was the reasoning that made the dump look authoritative, and it is not sufficient.
///
/// **This corrects a memory written the same morning**, which said the dump "beats scanning PE
/// strings by a wide margin". It beats the *adjacency* trick, which this session also disproved —
/// `snd_refdist` and `snd_refdb` do have their defaults pooled immediately before their names, and
/// `snd_gain_min`'s sits in a different section altogether, with the four names packed
/// consecutively and no literal between them. What the dump does not beat is the registration.
///
/// So the rule is: **registration first, dump as a cross-check, adjacency never.**
/// </remarks>
public sealed class SoundConVarConformanceTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    /// <summary>Read from the registrations in <c>engine.dll</c>, 2026-08-27.</summary>
    private static readonly (string Name, string Default, bool InDumpToo)[] Declared =
    [
        ("snd_refdist", "36", true),
        ("snd_refdb", "60", true),
        ("snd_gain", "1", true),

        // The dump says 0 here. The registration says 0.01, and the registration is the default.
        ("snd_gain_min", "0.01", false),
    ];

    [Test]
    public void Declarations_ForEverySoundConVar_MatchTheEnginesRegistration()
    {
        foreach ((string name, string expected, _) in Declared)
        {
            EngineConVars.ByName(name).Default.ShouldBe(expected);
        }
    }

    [Test]
    public void Declarations_ForEverySoundConVar_AreCheatProtectedAndNotReplicated()
    {
        foreach ((string name, _, _) in Declared)
        {
            EngineConVar declared = EngineConVars.ByName(name);

            declared.Cheat.ShouldBeTrue($"{name} registers FCVAR_CHEAT (0x4000)");
            declared.Replicated.ShouldBeFalse($"{name} is the watcher's, not the server's");
        }
    }

    /// <summary>That the shipped dump agrees where it agrees, and still disagrees where it did.</summary>
    /// <remarks>
    /// **The disagreement is asserted rather than worked around**, so it cannot quietly heal or
    /// quietly spread. If a TF2 update makes the dump print `0.01`, this test fails and the note
    /// above gets deleted; if the dump starts disagreeing about a second ConVar, the first half
    /// fails and that is a finding too.
    /// </remarks>
    [Test]
    public void ShippedCvarList_ForTheSoundConVars_AgreesExceptOnMinimumGain()
    {
        string listing = File.ReadAllText(GameInstall.RequireFile("cvarlist.log"));

        foreach ((string name, string expected, bool agrees) in Declared)
        {
            Match row = Regex.Match(
                listing,
                $"^{Regex.Escape(name)} +: +([^ ]+) +:",
                RegexOptions.Multiline,
                TimeSpan.FromSeconds(10));

            row.Success.ShouldBeTrue($"{name} appears in the game's own dump");

            if (agrees)
            {
                row.Groups[1].Value.ShouldBe(expected);
            }
            else
            {
                row.Groups[1].Value.ShouldBe(
                    "0",
                    "the dump reports the value in force, not the registered default");
            }
        }
    }

    /// <summary>That the SDK really does lack these, so the binary was not a shortcut.</summary>
    /// <remarks>
    /// **A control for the claim in this class's remarks.** "Not in the SDK" is an absence, and an
    /// absence found by a search is a fact about the search until something proves the search
    /// works — so this looks for a ConVar that IS there in the same sweep.
    /// </remarks>
    [Test]
    public void Sdk_ForTheSoundConVars_DeclaresNoneOfThem()
    {
        if (!SourceSdk.Available)
        {
            Skip.Because(SourceSdk.Missing);
        }

        SourceSdk.Names(
                "src/game/client",
                "*.cpp",
                new Regex(@"ConVar[^;\n]*""(cl_showpos)""", RegexOptions.None, Limit),
                recursive: true)
            .ShouldNotBeEmpty("the control: a client ConVar the SDK does declare");

        SourceSdk.Names(
                "src/game",
                "*.cpp",
                new Regex(
                    @"ConVar[^;\n]*""(snd_refdist|snd_refdb|snd_gain|snd_gain_min)""",
                    RegexOptions.None,
                    Limit),
                recursive: true)
            .ShouldBeEmpty("the sound ConVars are engine-side and absent from the SDK");
    }
}
