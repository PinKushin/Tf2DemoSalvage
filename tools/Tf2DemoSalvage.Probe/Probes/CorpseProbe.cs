using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// What the corpses in a demo say about themselves.
/// </summary>
/// <remarks>
/// **`DT_TFRagdoll` is `NOBASE`**, so a corpse carries no model index, no skin, no body and no
/// angles — which is why every one of them is decoded and none is drawn
/// (`docs/PARITY-AUDIT.md` #4). The client builds those fields in `CreateTFRagdoll`
/// (`c_tf_player.cpp:691`) from what IS on the table, and this reports whether that raw material
/// actually arrives.
///
/// <code>
///   corpses serveme-627619-stv-2026-08-07
/// </code>
///
/// **The denominator is the point.** "We decode 299 and draw none" is only actionable if the 299
/// carry a class and a position; a count of entities says nothing about whether they are
/// describable.
/// </remarks>
public sealed class CorpseProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "corpses";

    /// <inheritdoc/>
    public string Summary => "what each CTFRagdoll says about itself: corpses <demo>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("corpses <demo>");
            return;
        }

        if (DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        DemoTimeline timeline;

        try
        {
            timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        }
        catch (InvalidDataException truncated)
        {
            // **Only this exception, and it is a fact about the FILE rather than a failure here.**
            // The 2007 SourceTV specimen's `dem_datatables` is cut off at 65,536 bytes by the
            // writer's own cap (`docs/findings/03-string-tables.md`), so no entity decoding is
            // possible for it at all — reported so that walking the corpus does not stop at it, and
            // so nobody reads a stack trace as a corpse defect. Anything else propagates.
            output.WriteLine($"{Path.GetFileName(path)}: no entity decoding — {truncated.Message}");
            return;
        }

        int seen = 0;
        int described = 0;
        int placed = 0;
        int gibs = 0;
        int burning = 0;
        List<string> examples = [];

        foreach (SceneRagdoll corpse in timeline.Corpses)
        {
            seen++;

            if (corpse.PlayerClass is > 0)
            {
                described++;
            }

            if (corpse.X != 0f || corpse.Y != 0f || corpse.Z != 0f)
            {
                placed++;
            }

            if (corpse.Gib)
            {
                gibs++;
            }

            if (corpse.Burning)
            {
                burning++;
            }

            if (examples.Count < 4)
            {
                examples.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"entity {corpse.EntityIndex} class {corpse.PlayerClass} team {corpse.Team} " +
                    $"at ({corpse.X:0}, {corpse.Y:0}, {corpse.Z:0}) " +
                    $"ticks {corpse.FirstTick}-{corpse.LastTick} yaw {corpse.Yaw:0.0}" +
                    $"{(corpse.Gib ? " GIB" : string.Empty)}" +
                    $"{(corpse.Burning ? " BURNING" : string.Empty)}"));
            }
        }

        output.WriteLine($"{Path.GetFileName(path)}: {seen} CTFRagdoll entities");

        output.WriteLine(
            $"  {described} carry a class, {placed} carry a position — the two the model and the " +
            "placement are derived from");

        output.WriteLine($"  {gibs} gibbed, {burning} burning");

        // **How many corpses `m_nBody` could possibly change**, which is the gate in front of the
        // last unimplemented piece of `CreateTFRagdoll`'s appearance. The engine copies the body
        // group off the player under `if ( !m_bFeignDeath || m_bWasDisguised )`
        // (`c_tf_player.cpp:790-793`) — but a TF2 player's body group is non-zero in exactly one
        // case, a disguised spy wearing a mask (`PlayerProps`, and `ValidateModelIndex`'s tail at
        // `c_tf_player.cpp:9024`). Everything else is zero, and copying zero onto zero is nothing.
        int feigned = 0;
        int wasDisguised = 0;

        foreach (SceneRagdoll corpse in timeline.Corpses)
        {
            if (corpse.FeignDeath)
            {
                feigned++;
            }

            if (corpse.WasDisguised)
            {
                wasDisguised++;
            }
        }

        output.WriteLine($"  {feigned} feigned death, {wasDisguised} died disguised");

        // **How many corpses actually PLAY a death animation** (B323) — the count after all three of
        // the engine's gates, not just the first. Eligibility excludes everything but headshots,
        // decapitations and backstabs; the coin flip discards three quarters of what is left; and
        // being airborne vetoes the rest. Reported next to the eligible count so the two can be
        // compared, since a quarter of a small number is the whole point of this branch.
        int animated = 0;
        int grounded = 0;

        foreach (SceneRagdoll corpse in timeline.Corpses)
        {
            if (corpse.OnGround)
            {
                grounded++;
            }

            if (RagdollDeath.SequenceFor(corpse) is not null)
            {
                animated++;
            }
        }

        output.WriteLine(
            $"  {grounded} died on the ground; {animated} play a death animation");

        // **The number that says whether the feature is VISIBLE.** A match's total is a decode
        // measurement; how many lie on the floor at one moment is what somebody looking at the
        // viewer would see, and it is also how a tick worth screenshotting gets picked.
        (int Tick, int Count) busiest = Busiest(timeline.Corpses);

        output.WriteLine(
            $"  most at once: {busiest.Count}, at tick {busiest.Tick}");

        int first = int.MaxValue;
        int last = int.MinValue;
        int single = 0;

        foreach (SceneRagdoll corpse in timeline.Corpses)
        {
            first = Math.Min(first, corpse.FirstTick);
            last = Math.Max(last, corpse.LastTick);

            if (corpse.FirstTick == corpse.LastTick)
            {
                single++;
            }
        }

        // **The control on the tick range.** A demo does not start at tick zero
        // (`docs/memory/demo-ticks-do-not-start-at-zero.md`), so a range that begins at 0 or 1, or a
        // corpse count where every one lives exactly one tick, is the instrument rather than the
        // subject.
        output.WriteLine($"  ticks {first}-{last}; {single} lived a single tick");

        int born = 0;

        foreach (SceneRagdoll corpse in timeline.Corpses)
        {
            if (corpse.FirstTick <= 10)
            {
                born++;
            }
        }

        // **A corpse cannot exist in the first ten ticks of a match**, so any count here is the
        // instrument reporting on itself — a capture firing on the wrong entity, or a tick that
        // never arrived. The control for the whole window measurement.
        output.WriteLine($"  {born} claim to exist within ten ticks of the start");

        int facing = 0;

        foreach (SceneRagdoll corpse in timeline.Corpses)
        {
            if (corpse.Yaw != 0f)
            {
                facing++;
            }
        }

        // **A yaw of zero is indistinguishable from a yaw that never resolved**, and the corpse's
        // orientation comes from the PLAYER through `m_hPlayer` rather than from its own table. If
        // this is 0 the handle resolution is broken and every body faces north; if it is close to
        // the total, it is working. Exactly zero corpses genuinely facing due north is unlikely
        // enough that the count is a usable instrument.
        output.WriteLine($"  {facing} face a direction taken from the player they were");

        // **Whether a corpse's cosmetics are reachable at all**, which is the question that decides
        // whether the next piece of work exists. The engine builds them by walking the PLAYER's
        // wearable list at the moment of death — `CreateBoneAttachmentsFromWearables`,
        // `c_tf_player.cpp:10169` — and NOT from the corpse's own `m_hRagWearables`, which the
        // client only ever hides. So the question is whether that player's wearable entities are
        // still in the scene on the tick the corpse arrives. If they are gone by then, this is
        // authoring rather than decoding and the answer is to leave it alone.
        int withPlayer = 0;
        int withWearables = 0;
        int wearablesFound = 0;

        List<SceneProp> atDeath = [];

        foreach (SceneRagdoll corpse in timeline.Corpses)
        {
            if (corpse.PlayerIndex is not { } player)
            {
                continue;
            }

            withPlayer++;

            atDeath.Clear();
            timeline.PropsAt(corpse.FirstTick, atDeath);

            int worn = 0;

            foreach (SceneProp prop in atDeath)
            {
                if (prop.AttachedTo == player && prop.BoneMerged)
                {
                    worn++;
                }
            }

            if (worn > 0)
            {
                withWearables++;
                wearablesFound += worn;
            }
        }

        output.WriteLine(
            $"  {withWearables} of {withPlayer} had wearables on the tick they appeared, " +
            $"{wearablesFound} in total");

        // **How many corpses could play a death ANIMATION at all.** `GetSequenceForDeath` is a
        // switch on `m_iDamageCustom` with two cases and no default — headshots and their
        // decapitation variants, and backstabs — returning -1 for every other death
        // (`tf_player_shared.cpp:13441-13454`). Of those, only a quarter keep it (the `RandomFloat`,
        // D136). So this count bounds the whole feature, and if it is small the pose question is
        // almost entirely about PHYSICS rather than about animations.
        int eligible = 0;
        int noCustom = 0;

        foreach (SceneRagdoll corpse in timeline.Corpses)
        {
            if (corpse.DamageCustom is not { } how)
            {
                noCustom++;
                continue;
            }

            if (how is DeathAnimEligible.Headshot or DeathAnimEligible.Backstab
                or DeathAnimEligible.Decapitation or DeathAnimEligible.BarbarianSwing
                or DeathAnimEligible.HeadshotDecapitation)
            {
                eligible++;
            }
        }

        output.WriteLine(
            $"  {eligible} could play a death animation ({noCustom} sent no m_iDamageCustom); " +
            "a quarter of those would");

        // **The control on that zero.** A field that decodes to its default for every entity looks
        // identical to a field nobody eligible triggered, and only the spread tells them apart
        // (`docs/memory/an-empty-search-needs-a-control.md`). A comp 6v6 has no sniper and no spy,
        // so zero headshots and zero backstabs is the expected answer — but it has to be shown
        // rather than assumed, and a single value repeated 159 times would mean the decode.
        Dictionary<int, int> kinds = [];

        foreach (SceneRagdoll corpse in timeline.Corpses)
        {
            if (corpse.DamageCustom is { } how)
            {
                kinds[how] = kinds.TryGetValue(how, out int seenBefore) ? seenBefore + 1 : 1;
            }
        }

        output.WriteLine(
            "  m_iDamageCustom seen: " +
            string.Join(", ", kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Key}x{k.Value}")));

        // **The control that says whether "most at once" is corpses or double-counting.** Two
        // corpses may share an entity slot, and if their windows overlap the count is inflated by
        // an arithmetic fault rather than reporting the scene. The server keeps one ragdoll per
        // PLAYER (`UTIL_Remove` on the next death, `tf_player.cpp:15602`), so a figure above the
        // roster size means the instrument, not the match.
        HashSet<int> slots = [];
        int overlapping = 0;

        foreach (SceneRagdoll corpse in timeline.Corpses)
        {
            if (busiest.Tick >= corpse.FirstTick && busiest.Tick <= corpse.LastTick &&
                !slots.Add(corpse.EntityIndex))
            {
                overlapping++;
            }
        }

        output.WriteLine(
            $"  at that tick: {slots.Count} distinct slots, {overlapping} sharing a slot");

        // **Two counts per sample, because the entity's lifetime and the DRAWN lifetime are
        // different questions and only the second is what a viewer sees.** The server keeps one
        // ragdoll per player until that player next dies, so the entity count climbs across a match;
        // `RagdollFade` is `C_TFRagdoll::ClientThink`'s rule and is what actually removes them.
        //
        // Nothing here is visible, since a probe has no camera — so this measures the case the
        // engine calls "never seen", the 15-second one. A real viewer keeps a corpse longer
        // whenever it is on screen, and that is the point of the rule.
        RagdollFade fade = new(timeline.IntervalPerTick);
        List<SceneProp> drawnAt = [];

        // **The real schema, because the two new skips are no-ops without it** (B324) — and a probe
        // that quietly passed null would report the pre-filter count while looking like it measured
        // the filtered one.
        // **Through `GameContent`, not a single VPK.** `items_game.txt` is reached through the whole
        // archive SET the install exposes, and a probe opening one `.vpk` by hand reported "not
        // read" against an install that has it — an instrument answering about itself. This is also
        // the accessor production uses, so the probe and the viewer cannot disagree about which
        // schema they filtered with.
        ItemSchema? schema =
            new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder)
                    .FindGameFolder() is { } install
                ? GameContent.Open(install, NullLoggerFactory.Instance).Weapons.Items
                : null;

        output.WriteLine(
            $"  item schema {(schema is null ? "NOT read — the two econ skips are inert" : "read")}");

        // **Do TF2's player models actually HAVE the two death animations?** The whole death branch
        // resolves a LABEL through `SequenceByLabel`, and a label that does not exist resolves to -1
        // and silently changes nothing — a feature that runs, counts, and draws the same pose. The
        // engine has the same dependency: `GetSequenceForDeath` returns whatever
        // `LookupSequence( "primary_death_headshot" )` gives, including -1.
        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder)
                .FindGameFolder() is { } where &&
            File.Exists(Path.Combine(where, "tf2_misc_dir.vpk")))
        {
            VpkArchive models = VpkArchive.Open(Path.Combine(where, "tf2_misc_dir.vpk"));

            foreach (string model in (string[])["models/player/scout.mdl", "models/player/spy.mdl"])
            {
                if (models.ReadFile(model) is not { } bytes)
                {
                    continue;
                }

                int headshot = -1;
                int backstab = -1;
                int index = -1;

                foreach (string label in StudioSequences.Read(bytes).Select(one => one.Label))
                {
                    index++;

                    if (string.Equals(
                        label, RagdollDeath.HeadshotSequence, StringComparison.OrdinalIgnoreCase))
                    {
                        headshot = index;
                    }
                    else if (string.Equals(
                        label, RagdollDeath.BackstabSequence, StringComparison.OrdinalIgnoreCase))
                    {
                        backstab = index;
                    }
                }

                output.WriteLine(
                    $"  {Path.GetFileName(model)}: headshot death at {headshot}, backstab at {backstab}");
            }
        }

        foreach (int fraction in (int[])[1, 2, 3, 4])
        {
            int when = first + (((last - first) * fraction) / 4);
            int alive = 0;

            foreach (SceneRagdoll corpse in timeline.Corpses)
            {
                if (when >= corpse.FirstTick && when <= corpse.LastTick)
                {
                    alive++;
                }
            }

            drawnAt.Clear();

            RagdollProps.Fill(
                timeline.Corpses, when, ClassModel, drawnAt, fade, visible: null, items: schema);

            output.WriteLine($"  tick {when}: {alive} entities alive, {drawnAt.Count} drawn");

            // **WHICH models a corpse carries, which is what caught the first version of this.**
            // The engine walks the player's WEARABLE list — `GetWearable(wbl)`,
            // `c_tf_player.cpp:10178` — and a weapon is a `CTFWeaponBase`, not a `CEconWearable`,
            // so it is not in it. Scanning every bone-merged child of the player instead put all
            // four of a demoman's weapons on his corpse, holstered ones included. Naming them is
            // what made that obvious; a count would have read as a success.
            //
            // **Nothing here says where they are DRAWN.** A bone-merged item carries no transform
            // of its own, so comparing `SceneProp.Pose` between item and corpse compares two inputs
            // and answers nothing about placement — the first version of this block did exactly
            // that and reported "off the origin" for items sitting on it.
            // **Which corpse each item belongs to**, so a picture can be aimed at a body that
            // actually has one. Nothing here says where the item is DRAWN: a bone-merged item
            // carries no transform of its own, so comparing `SceneProp.Pose` between item and
            // corpse compares two inputs. The first version of this block did that and reported
            // "off the origin" for items it could not have placed.
            foreach (SceneProp prop in drawnAt.Where(p => p.AttachedTo is not null))
            {
                output.WriteLine(
                    $"    worn {Path.GetFileName(prop.ModelPath)} on corpse {prop.AttachedTo}");
            }

            // **Where they are, so the claim can be checked by LOOKING.** Nothing here can say a
            // corpse appears on screen — that needs the viewer and a person — but it can say where
            // to point the camera. `TF2VIEW_CAMERA` plus `--shot` takes the picture
            // (`docs/memory/take-your-own-screenshot.md`).
            foreach (SceneProp drawn in drawnAt)
            {
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"    entity {drawn.EntityIndex} at " +
                    $"({drawn.Pose.X:0} {drawn.Pose.Y:0} {drawn.Pose.Z:0}) " +
                    $"yaw {drawn.Pose.Yaw:0} skin {drawn.Pose.Skin}"));
            }
        }

        foreach (string example in examples)
        {
            output.WriteLine($"  {example}");
        }
    }

    /// <summary>A stand-in class table, so the probe needs no game install.</summary>
    /// <param name="playerClass">The class index.</param>
    /// <returns>A path that is never opened.</returns>
    /// <remarks>
    /// **The probe measures HOW MANY corpses are drawn, not which model each gets.** Resolving the
    /// real paths would make this need TF2 installed and would measure `PlayerClassModels`, which
    /// `ClassAirwalkTests` already covers. Every playing class answers, so nothing is dropped for
    /// the wrong reason — a table that returned null would silently deflate the count and look like
    /// the fade working.
    /// </remarks>
    private static string? ClassModel(int playerClass) =>
        playerClass is >= PlayerClassModels.FirstClass and <= PlayerClassModels.LastPlayingClass
            ? "models/player/scout.mdl"
            : null;

    /// <summary>The moment with the most corpses lying about, and how many.</summary>
    /// <param name="corpses">Every corpse the demo described.</param>
    /// <returns>The tick and the count.</returns>
    /// <remarks>
    /// **A sweep over the endpoints rather than over every tick.** The count can only change where
    /// an interval begins or ends, so those are the only moments worth asking about — a match is
    /// hundreds of thousands of ticks and a few hundred corpses.
    /// </remarks>
    private static (int Tick, int Count) Busiest(IReadOnlyList<SceneRagdoll> corpses)
    {
        (int Tick, int Count) best = (0, 0);

        // LINQ for the projection because the outer loop only ever wanted the tick (S3267), and a
        // probe is off every hot path — `docs/memory/linq-is-a-test-tool.md`.
        foreach (int tick in corpses.Select(corpse => corpse.FirstTick))
        {
            int here = 0;

            foreach (SceneRagdoll other in corpses)
            {
                if (tick >= other.FirstTick && tick <= other.LastTick)
                {
                    here++;
                }
            }

            if (here > best.Count)
            {
                best = (tick, here);
            }
        }

        return best;
    }

    /// <summary>
    /// The only <c>TF_DMG_CUSTOM_*</c> ordinals <c>GetSequenceForDeath</c> answers for.
    /// </summary>
    /// <remarks>
    /// Counted off the enumerators of `ETFDmgCustom` (`tf_shareddefs.h:1181`), comments excluded —
    /// the first attempt at these numbers took line offsets and would have been wrong for every
    /// value after the first comment in the block.
    /// </remarks>
    private static class DeathAnimEligible
    {
        public const int Headshot = 1;
        public const int Backstab = 2;
        public const int Decapitation = 20;
        public const int BarbarianSwing = 24;
        public const int HeadshotDecapitation = 51;
    }
}
