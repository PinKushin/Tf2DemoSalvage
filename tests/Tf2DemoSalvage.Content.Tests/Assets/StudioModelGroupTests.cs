using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Resolving a networked sequence number through a model and the models it includes.
/// </summary>
/// <remarks>
/// **A player model holds almost none of its own animation.** scout.mdl declares 306 sequences and
/// two local animations of one frame each; the rest is in <c>scout_animations.mdl</c>, which holds
/// 1,012 animations across five megabytes. So a sequence number from a demo cannot be answered by
/// the player model alone.
///
/// <c>virtualmodel_t::AppendSequences</c> (<c>public/studio_virtualmodel.cpp:142</c>) merges them
/// **by label**: the base model's sequences first, then each included model appends only those
/// whose names are not already present. Reimplemented here rather than copied — the Source SDK
/// licence grants use for Source engine mods, which this is not.
/// </remarks>
public sealed class StudioModelGroupTests
{
    private const string Game = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";

    [Test]
    public void APlayerModel_NamesTheModelsItsAnimationsLiveIn()
    {
        if (Read("models/player/scout.mdl") is not { } scout)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        IReadOnlyList<string> included = StudioModelGroups.Read(scout);

        included.Count.ShouldBe(3);

        included.ShouldContain(
            name => name.Contains("scout_animations", StringComparison.OrdinalIgnoreCase),
            "the animation model is what the 306 sequences actually point at");
    }

    [Test]
    public void AModelWithNoIncludes_NamesNone()
    {
        // **The control that says the offsets are real.** numincludemodels sits at 336, counted
        // from studio.h's field order; a wrong offset lands on some other integer and produces a
        // plausible non-zero count. A health pack genuinely includes nothing.
        if (Read("models/items/medkit_small.mdl") is not { } medkit)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        StudioModelGroups.Read(medkit).ShouldBeEmpty();
    }

    [Test]
    public void ASequencesLabel_IsReadableAndNamesWhatItDoes()
    {
        // The merge is by label, so the labels have to come out as real names rather than as
        // whatever bytes sit at a mistaken offset.
        if (Read("models/player/scout.mdl") is not { } scout)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        IReadOnlyList<StudioSequence> sequences = StudioSequences.Read(scout);

        string[] labels = [.. sequences.Select(sequence => sequence.Label)];

        labels.ShouldAllBe(label => label.Length > 0);

        TestContext.Out.WriteLine($"SEQL scout first: {string.Join(", ", labels.Take(8))}");

        // TF2's own naming: the stand and run activities every class has.
        labels.ShouldContain(
            label => label.Contains("stand", StringComparison.OrdinalIgnoreCase),
            "every class has a standing sequence");
    }

    [Test]
    public void TheMergedList_PutsTheBaseModelsSequencesFirst()
    {
        // Valve appends group by group starting from the base, so a virtual index below the base
        // model's own count is that model's own sequence. Getting this backwards would resolve
        // every sequence to an animation model's numbering instead.
        StudioSequenceTable table = StudioSequenceTable.Merge(
        [
            (0, [Named("a"), Named("b")]),
            (1, [Named("c")]),
        ]);

        table.Count.ShouldBe(3);
        table.At(0).ShouldBe((0, 0));
        table.At(1).ShouldBe((0, 1));
        table.At(2).ShouldBe((1, 0));
    }

    [Test]
    public void ASequenceNameAlreadyPresent_DoesNotAppendAgain()
    {
        // **The whole reason the merge is by name.** An animation model re-declares sequences the
        // base model already has, and appending both would shift every later index - so a demo's
        // sequence number would resolve to the wrong animation from that point on.
        StudioSequenceTable table = StudioSequenceTable.Merge(
        [
            (0, [Named("stand"), Named("run")]),
            (1, [Named("stand"), Named("jump")]),
        ]);

        table.Count.ShouldBe(3, "stand is declared twice and counts once");

        table.At(0).ShouldBe((0, 0));
        table.At(1).ShouldBe((0, 1));
        table.At(2).ShouldBe((1, 1), "jump is the new one, and it keeps its own local index");
    }

    [Test]
    public void AForwardDeclaredSequence_IsReplacedByTheRealOne()
    {
        // **This is why a player model's sequences look like stubs. They are stubs.** The scout
        // player model holds the reload label pointing at an animation of one frame, while the
        // scout animation model holds the same label with twenty-one real frames.
        //
        // Valve's merge keeps the first occurrence UNLESS that one is flagged STUDIO_OVERRIDE,
        // which is 0x0800 and which studio.h describes as a forward declared and empty sequence.
        // In that case the later one replaces it, at the same index.
        //
        // Keeping the stub resolves every named animation a class has to a single frame, which is
        // what the first version of this measured on every label it looked for.
        StudioSequenceTable table = StudioSequenceTable.Merge(
        [
            (0, [Declared("reload"), Named("stand")]),
            (1, [Named("reload")]),
        ]);

        table.Count.ShouldBe(2, "the real one replaces the declaration rather than appending");

        table.At(0).ShouldBe((1, 0), "reload now comes from the model that actually has it");
        table.At(1).ShouldBe((0, 1), "and the index it sits at does not move");
    }

    [Test]
    public void ASequenceThatIsNotADeclaration_IsNotReplaced()
    {
        // **The control.** Without it, "replace on collision" would pass this test suite too - and
        // that rule would let a workshop animation model silently take over sequences the base
        // model really owns.
        StudioSequenceTable table = StudioSequenceTable.Merge(
        [
            (0, [Named("reload")]),
            (1, [Named("reload")]),
        ]);

        table.Count.ShouldBe(1);
        table.At(0).ShouldBe((0, 0), "the base model's real sequence stands");
    }

    [Test]
    public void ASequenceNumberPastTheEnd_ResolvesToNothing()
    {
        // A demo can name a sequence a later game version added. Answering with a wrong animation
        // is worse than answering with none.
        StudioSequenceTable.Merge([(0, [Named("a")])]).At(7).ShouldBeNull();
    }

    [Test]
    public void ARealSequenceNumber_ResolvesToRealAnimationData()
    {
        // **The end to end measurement, and the point of all of it.** A demo networks a number,
        // and this has to turn that number into frames of animation that really exist. Before the
        // merge a player model could only answer with its own pair of single-frame animations.
        if (Read("models/player/scout.mdl") is not { } scout)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        List<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups =
            [(0, StudioSequences.Read(scout))];

        List<byte[]> models = [scout];

        foreach (string path in StudioModelGroups.Read(scout))
        {
            if (Read(path) is not { } included)
            {
                continue;
            }

            groups.Add((models.Count, StudioSequences.Read(included)));
            models.Add(included);
        }

        StudioSequenceTable table = StudioSequenceTable.Merge(groups);

        table.Count.ShouldBeGreaterThan(
            306, "the included models contribute sequences the player model does not have");

        int animated = 0;
        int longest = 0;

        for (int sequence = 0; sequence < table.Count; sequence++)
        {
            if (table.At(sequence) is not { } where)
            {
                continue;
            }

            IReadOnlyList<StudioSequence> owner = groups[where.Group].Sequences;
            int frames = StudioAnimation.Frames(models[where.Group], owner[where.Local].Animation);

            if (frames > 1)
            {
                animated++;
                longest = Math.Max(longest, frames);
            }
        }

        TestContext.Out.WriteLine(
            $"SEQR scout: {table.Count} merged sequences, {animated} resolve to animation " +
            $"of more than one frame, longest {longest} frames");

        // Named sequences a reader would expect a class to have, with how long each really is -
        // a name resolving to a single frame would mean the merge found the label and not the
        // animation behind it.
        foreach (string wanted in (string[])["stand_", "run_", "crouch_", "airwalk", "idle"])
        {
            for (int sequence = 0; sequence < table.Count; sequence++)
            {
                if (table.At(sequence) is not { } where)
                {
                    continue;
                }

                StudioSequence entry = groups[where.Group].Sequences[where.Local];

                if (!entry.Label.Contains(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Every group that declares this same label, and what each would resolve to.
                // If the base model's copy is a stub, keeping the first occurrence picks it.
                string report = string.Join(
                    "  |  ",
                    groups.SelectMany(g => g.Sequences
                        .Where(q => string.Equals(q.Label, entry.Label, StringComparison.OrdinalIgnoreCase))
                        .Select(q => $"group {g.Group} anim {q.Animation} " +
                            $"{StudioAnimation.Frames(models[g.Group], q.Animation)}f")));

                TestContext.Out.WriteLine($"SEQN [{sequence}] {entry.Label}: {report}");

                break;
            }
        }

        // The player model alone offered two animations of one frame each. Anything substantial
        // here is data that was unreachable before the merge.
        animated.ShouldBeGreaterThan(100, "a class has hundreds of real animations");
        longest.ShouldBeGreaterThan(10);
    }

    private static StudioSequence Named(string label) => new(0, 0, label);

    /// <summary>A forward declaration: the empty stub a base model holds a name with.</summary>
    private static StudioSequence Declared(string label) => new(0, 0x0800, label);

    private static byte[]? Read(string path)
    {
        if (!Directory.Exists(Game))
        {
            return null;
        }

        return new[] { "tf2_misc_dir.vpk", "tf2_textures_dir.vpk" }
            .Select(name => Path.Combine(Game, name))
            .Where(File.Exists)
            .Select(VpkArchive.Open)
            .Select(archive => archive.ReadFile(path))
            .FirstOrDefault(found => found is not null);
    }
}
