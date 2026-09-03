using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The gesture layer's transcribed constants, against the SDK they were copied from.
/// </summary>
/// <remarks>
/// **This should have been written before B112's implementation and was not.** The project's order
/// of work is a conformance test first, then the ordinary tests, then the code — precisely so the
/// parity claim is written down before there is an implementation to bias it. Three slices of the
/// gesture layer went in with unit tests that assert what the code does and citations in comments
/// saying where it came from, and nothing that would notice if the SDK said something else.
///
/// **What was at risk is a hand-transcription of 41 enum members, 7 slot values and a table of
/// activity names.** Every one was typed out by reading Valve's headers. A wrong ordinal does not
/// throw — it plays the wrong gesture, or none — and a misspelled <c>ACT_*</c> name resolves to no
/// sequence and silently animates nothing. That is the same failure mode
/// <c>WeaponScriptNameTests</c> exists to catch for weapon scripts: reproduced mappings go stale in
/// silence.
///
/// The three checks below are deliberately of different kinds. The first two are exhaustive
/// agreement with an enum the SDK declares. The third is an existence check over the names this
/// project actually uses, which is the only one of the three that can catch a typo.
/// </remarks>
public sealed class GestureConformanceTests
{
    private const string AnimState = "src/game/shared/Multiplayer/multiplayer_animstate.h";
    private const string Activities = "src/game/shared/ai_activity.h";

    [Test]
    public void Gestures_EveryPlayerAnimEvent_HasTheSdkOrdinal()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore("the Source SDK is not available");
            return;
        }

        IReadOnlyDictionary<string, int> sdk =
            SourceSdk.Enumerators(AnimState, "PlayerAnimEvent_t");

        // The control. A regex that matched nothing would make every assertion below vacuous, and
        // an empty dictionary is exactly what a renamed or moved enum produces.
        sdk.Count.ShouldBeGreaterThan(
            30, "PlayerAnimEvent_t declares about forty events plus its COUNT");

        Dictionary<string, int> byNormalized = sdk.ToDictionary(
            pair => Normalize(pair.Key), pair => pair.Value, StringComparer.Ordinal);

        foreach (PlayerAnimEvent value in Enum.GetValues<PlayerAnimEvent>())
        {
            string wanted = Normalize("PLAYERANIMEVENT_" + value);

            byNormalized.ShouldContainKey(wanted, $"{value} has no counterpart in the SDK enum");
            byNormalized[wanted].ShouldBe((int)value, $"{value} disagrees with the SDK ordinal");
        }
    }

    [Test]
    public void Gestures_TheEnum_IsStillAppendOnly()
    {
        // **The claim `docs/findings/25-gesture-layer.md` rests on**: ordinals 0-29 are identical
        // across every era, so one mapping decodes every protocol. Today's SDK agreeing is
        // necessary and not sufficient - what would break the finding is a member INSERTED before
        // the tail, which shifts everything after it. Pinning the boundary member's ordinal
        // catches exactly that, and costs nothing if Valve only ever appends.
        if (!SourceSdk.Available)
        {
            Assert.Ignore("the Source SDK is not available");
            return;
        }

        IReadOnlyDictionary<string, int> sdk =
            SourceSdk.Enumerators(AnimState, "PlayerAnimEvent_t");

        // The last member the Orange Box era knew. Everything at or below it must keep its place
        // for a 2008 demo to decode under the same table as a modern one.
        sdk["PLAYERANIMEVENT_VOICE_COMMAND_GESTURE"].ShouldBe(29);

        // And the two ends of the shared prefix, so a shift anywhere inside it shows up here
        // rather than as a wrong animation on an old demo.
        sdk["PLAYERANIMEVENT_ATTACK_PRIMARY"].ShouldBe(0);
        sdk["PLAYERANIMEVENT_DOUBLEJUMP"].ShouldBe(15);
    }

    [Test]
    public void Gestures_EveryGestureSlot_HasTheSdkOrdinal()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore("the Source SDK is not available");
            return;
        }

        // **Parsed from the text rather than through SourceSdk.Enumerators, because the SDK
        // declares this one ANONYMOUSLY** - `// Gesture Slots.` then a bare `enum {`. There is no
        // name to look it up by, so the block is found by its first member instead.
        Dictionary<string, int> sdk = AnonymousEnum("GESTURE_SLOT_ATTACK_AND_RELOAD");

        sdk.Count.ShouldBeGreaterThan(6, "there are seven slots plus GESTURE_SLOT_COUNT");

        Dictionary<string, int> byNormalized = sdk.ToDictionary(
            pair => Normalize(pair.Key), pair => pair.Value, StringComparer.Ordinal);

        foreach (GestureSlot slot in Enum.GetValues<GestureSlot>())
        {
            string wanted = Normalize("GESTURE_SLOT_" + slot);

            byNormalized.ShouldContainKey(wanted, $"{slot} has no counterpart in the SDK enum");
            byNormalized[wanted].ShouldBe((int)slot, $"{slot} disagrees with the SDK ordinal");
        }

        // The count must agree too: a slot added by Valve and not here would leave every gesture
        // mapped to a slot that exists, while silently losing the new one.
        sdk["GESTURE_SLOT_COUNT"].ShouldBe(Enum.GetValues<GestureSlot>().Length);
    }

    [Test]
    public void Gestures_EveryMappedActivity_IsRealInTheSdk()
    {
        // **The only one of these three that catches a typo.** The activities are STRINGS in
        // PlayerGestureEvent, resolved against a model at run time, and a misspelling resolves to
        // no sequence and animates nothing - it does not throw and it does not log.
        if (!SourceSdk.Available)
        {
            Assert.Ignore("the Source SDK is not available");
            return;
        }

        string activities = SourceSdk.Text(Activities).ShouldNotBeNull();

        List<string> used = [];

        // Every combination of context that can select a different activity, so the sweep covers
        // the whole table rather than its default column.
        GestureContext[] contexts =
        [
            new(),
            new(InDuck: true),
            new(InSwim: true),
            new(InSwim: true, IsMinigun: true),
            new(IsMinigun: true),
            new(IsSniperZoomed: true),
            new(IsSniperZoomed: true, InDuck: true),
            new(InAirWalk: true),
            new(IsLoser: true),
        ];

        foreach (PlayerAnimEvent anEvent in Enum.GetValues<PlayerAnimEvent>())
        {
            foreach (GestureContext context in contexts)
            {
                if (PlayerGestureEvent.Map(anEvent, context) is { ActivityName: { } name })
                {
                    // **The slot is substituted before the name is looked up** (B284). A gesture
                    // activity on a TF2 player model carries the held weapon's role —
                    // `ACT_MP_RELOAD_STAND_PRIMARY` — so the map emits a placeholder and the scene
                    // fills it from the installed game. Checking the placeholder against the SDK
                    // would look for `ACT_MP_RELOAD_STAND_{0}` and find nothing; checking the
                    // filled name is the question that matters, and it is what a model is asked.
                    //
                    // Every slot, not just the first: `ACT_MP_ATTACK_STAND_ITEM2` and
                    // `ACT_MP_JUMP_LAND_MELEE` are separate declarations and a map that produced
                    // one valid combination and three invalid ones would pass on the default.
                    used.Add(name);
                }
            }
        }

        // The control: a sweep that produced nothing would pass every assertion below.
        used.Distinct().Count().ShouldBeGreaterThan(
            20, "the mapping names a couple of dozen distinct activities");

        foreach (string name in used.Distinct())
        {
            // **The GENERIC name is what this map emits and what the SDK declares**, which is the
            // arrangement the engine has: `DoAnimationEvent` names `ACT_MP_RELOAD_STAND` and the
            // weapon in hand rewrites it to `ACT_MP_RELOAD_STAND_PRIMARY` through its own
            // `acttable_t` — `WeaponActivityTable` in this project, applied by the scene.
            //
            // So both the generic name and every rewrite of it must be a real activity. Checking
            // the generic one alone would miss a role whose table names something `ai_activity.h`
            // does not declare, and checking only the rewrites would miss a typo in a name no
            // weapon rewrites — a flinch.
            activities.ShouldContain(
                name, Case.Sensitive, $"{name} is not declared in ai_activity.h");

            foreach (string role in WeaponSlots)
            {
                string rewritten = WeaponActivityTable.Override(role, name);

                activities.ShouldContain(
                    rewritten,
                    Case.Sensitive,
                    $"{role} rewrites {name} to {rewritten}, which ai_activity.h does not declare");
            }
        }
    }

    /// <summary>The weapon roles a gesture activity can be suffixed with.</summary>
    /// <remarks>
    /// **Valve's own set, from the activity list itself** — a TF2 player model declares
    /// `ACT_MP_ATTACK_STAND_PRIMARY`, `_SECONDARY`, `_MELEE`, `_ITEM1`, `_ITEM2`, `_GRENADE`,
    /// `_BUILDING` and `_PDA`. They are separate declarations rather than one parameterised name,
    /// which is why every one is swept: a map that filled in a valid PRIMARY name and an invalid
    /// ITEM2 one would pass on the default alone.
    /// </remarks>
    private static readonly string[] WeaponSlots =
    [
        "PRIMARY", "SECONDARY", "MELEE", "ITEM1", "ITEM2", "GRENADE", "BUILDING", "PDA",
    ];

    /// <summary>A name with its underscores removed and its case flattened.</summary>
    /// <remarks>
    /// **Underscore placement is not derivable, which this test found the hard way.** The first
    /// version inserted one before every capital, turning <c>FlinchLeftArm</c> into
    /// <c>FLINCH_LEFT_ARM</c> — and the SDK writes <c>PLAYERANIMEVENT_FLINCH_LEFTARM</c>, with no
    /// underscore inside "LeftArm". Valve is not consistent about it and there is no rule to find.
    ///
    /// So both sides are normalised by deleting underscores entirely rather than by trying to
    /// reproduce them. That still compares the full name and the value; it only stops the test
    /// failing over a convention neither codebase promises. The alternative — a hand-written table
    /// of exceptions — would be exactly the kind of transcribed mapping this file exists to check.
    /// </remarks>
    private static string Normalize(string name) =>
        name.Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    /// <summary>Reads an anonymous enum, found by a member it is known to contain.</summary>
    private static Dictionary<string, int> AnonymousEnum(string knownMember)
    {
        string text = SourceSdk.Text(AnimState).ShouldNotBeNull();

        Match block = Regex.Match(
            text,
            @"enum\s*\{(?<body>[^}]*?" + Regex.Escape(knownMember) + @"[^}]*?)\}",
            RegexOptions.Singleline);

        block.Success.ShouldBeTrue($"no anonymous enum containing {knownMember}");

        Dictionary<string, int> values = new(StringComparer.Ordinal);
        int next = 0;

        foreach (string rawLine in block.Groups["body"].Value.Split(','))
        {
            // Strip comments and whitespace; an explicit `= n` resets the running counter.
            string line = Regex.Replace(rawLine, @"//.*?$|/\*.*?\*/", string.Empty, RegexOptions.Multiline).Trim();

            if (line.Length == 0)
            {
                continue;
            }

            string[] parts = line.Split('=', StringSplitOptions.TrimEntries);

            if (parts.Length == 2 && int.TryParse(parts[1], out int explicitValue))
            {
                next = explicitValue;
            }

            values[parts[0]] = next;
            next++;
        }

        return values;
    }
}
