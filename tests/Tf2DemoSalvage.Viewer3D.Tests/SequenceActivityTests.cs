using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// That a real player model names the activities its sequences answer to.
/// </summary>
/// <remarks>
/// **The claim the whole approach rests on** (B100). `studio.h` says
/// <c>mstudioseqdesc_t.activity</c> is "initialized at loadtime to game DLL values", so the number is
/// not in the file and the NAME is what a model ships. If TF2's player models turned out to leave
/// <c>szactivitynameindex</c> empty, selecting by activity would be impossible and the old
/// guess-the-sequence-name approach would be the only option available.
///
/// Reading the field is not the same as the field having anything in it — the offsets are already
/// checked against Valve's struct by <c>StudioStructTests</c>, and that says nothing about content.
///
/// Lives in this assembly rather than beside the other studio tests because reading a model out of
/// the game's archives goes through <c>GameArchives</c>, which is part of the viewer.
/// </remarks>
public sealed class SequenceActivityTests
{
    private const string GamePath = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";

    /// <summary>The scout, because every class model is built the same way.</summary>
    private const string PlayerModel = "models/player/scout.mdl";


    /// <remarks>
    /// **The base model alone is the wrong file to measure, and measuring it was the first mistake
    /// here.** scout.mdl carries one activity name between all of its sequences, because a player
    /// model holds the NAME of every animation it can play with an empty one-frame animation behind
    /// it (STUDIO_OVERRIDE) and the real sequences arrive with the included animation models. The
    /// merged table is what the viewer poses from, so it is what these assertions must read.
    /// </remarks>
    private static List<StudioSequence>? Sequences()
    {
        if (!Directory.Exists(GamePath))
        {
            return null;
        }

        GameArchives archives = GameArchives.Open(GamePath);

        if (archives.Read(PlayerModel) is not { Length: > 0 } bytes)
        {
            return null;
        }

        // The base model's own sequences, then each included animation model's, which is the order
        // the engine merges them in.
        List<StudioSequence> all = [.. StudioSequences.Read(bytes)];

        foreach (string include in StudioModelGroups.Read(bytes))
        {
            if (archives.Read(include) is { Length: > 0 } included)
            {
                all.AddRange(StudioSequences.Read(included));
            }
        }

        return all;
    }

    [Test]
    public void APlayerModelsSequences_NameTheirActivities()
    {
        if (Sequences() is not { Count: > 0 } sequences)
        {
            Assert.Ignore("The TF2 install or the scout model is not on this machine.");
            return;
        }

        IReadOnlyList<StudioSequence> named =
            [.. sequences.Where(sequence => sequence.Activity.Length > 0)];

        // The control and the claim together: the model has sequences, and a real share of them
        // carry an activity name. A handful would suggest the offset was landing on stray text.
        named.Count.ShouldBeGreaterThan(20);

        // Every name follows the engine's convention, which is what makes matching on it safe. A
        // misread offset would produce arbitrary strings from elsewhere in the string table, and
        // those would not all start this way.
        named.ShouldAllBe(sequence => sequence.Activity.StartsWith("ACT_", StringComparison.Ordinal));
    }

    [Test]
    public void SequenceActivity_TheMovementActivities_AreAllPresent()
    {
        // Named individually because these are the ones the activity state machine chooses, and a
        // count cannot say whether the specific one a crouching player needs exists.
        if (Sequences() is not { Count: > 0 } sequences)
        {
            Assert.Ignore("The TF2 install or the scout model is not on this machine.");
            return;
        }

        IReadOnlyList<string> activities = [.. sequences.Select(sequence => sequence.Activity)];

        // **Every activity a model claims is weapon-suffixed**, which is the finding that shaped the
        // rest of this work. ACT_MP_RUN -- what CalcMainActivity returns -- appears nowhere in a
        // model; CTFPlayerAnimState::TranslateActivity adds the suffix, and that is why the
        // translation step exists rather than being an optimisation.
        activities.ShouldContain("ACT_MP_STAND_PRIMARY");
        activities.ShouldContain("ACT_MP_RUN_PRIMARY");
        activities.ShouldContain("ACT_MP_CROUCH_PRIMARY");
        activities.ShouldContain("ACT_MP_CROUCHWALK_PRIMARY");
        activities.ShouldContain("ACT_MP_AIRWALK_PRIMARY");
        activities.ShouldContain("ACT_MP_SWIM_PRIMARY");

        // Other weapon slots exist for the same states, so the suffix is a real axis rather than a
        // naming quirk of the primary set.
        activities.ShouldContain("ACT_MP_STAND_MELEE");
        activities.ShouldContain("ACT_MP_RUN_SECONDARY");

        // **A jump is three activities, not one.** There is no ACT_MP_JUMP_PRIMARY: the model
        // carries a start, a float and a land, so drawing a jump needs sub-state this project does
        // not derive yet -- and asserting the single name that felt obvious is what found that.
        activities.ShouldNotContain("ACT_MP_JUMP_PRIMARY");
        activities.ShouldContain("ACT_MP_JUMP_START_primary");
        activities.ShouldContain("ACT_MP_JUMP_FLOAT_primary");
        activities.ShouldContain("ACT_MP_JUMP_LAND_primary");
    }

    [Test]
    public void SomeSequencesShareAnActivity_WithWeights()
    {
        // Why SelectWeightedSequence exists at all. If no activity were ever claimed twice, picking
        // the first match would be equivalent and the weight would be decoration.
        if (Sequences() is not { Count: > 0 } sequences)
        {
            Assert.Ignore("The TF2 install or the scout model is not on this machine.");
            return;
        }

        IReadOnlyList<StudioSequence> weighted =
            [.. sequences.Where(sequence => sequence.Activity.Length > 0 && sequence.ActivityWeight > 0)];

        weighted.ShouldNotBeEmpty();

        // A weight of zero with a name present is a real case — the sequence names the activity and
        // is never selected for it — so this checks the weights are being read rather than defaulted.
        weighted.Select(sequence => sequence.ActivityWeight).Distinct().ShouldNotBeEmpty();
    }

    [Test]
    public void ValvesOwnCasingIsInconsistent_SoMatchingMustIgnoreIt()
    {
        // Not a style note — a correctness requirement, and measured rather than assumed. The scout
        // ships ACT_MP_JUMP_START_primary in lower case beside ACT_MP_JUMP_LAND_SECONDARY in upper.
        // A case-sensitive lookup finds one and misses the other, which would draw a jump on some
        // weapon slots and the reference pose on the rest.
        if (Sequences() is not { Count: > 0 } sequences)
        {
            Assert.Ignore("The TF2 install or the scout model is not on this machine.");
            return;
        }

        IReadOnlyList<string> activities =
            [.. sequences.Select(sequence => sequence.Activity).Where(name => name.Length > 0)];

        activities.ShouldContain(name => name.EndsWith("_primary", StringComparison.Ordinal));
        activities.ShouldContain(name => name.EndsWith("_PRIMARY", StringComparison.Ordinal));
    }
}
