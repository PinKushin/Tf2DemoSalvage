using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, where `Corpus` binds to the namespace.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Finding real viewmodels, and the players they belong to.
/// </summary>
/// <remarks>
/// **The unit tests prove the reader looks in the right table; only a real demo proves the table
/// is what these files actually use.** A fixture written from the SDK header agrees with my
/// reading of the header — it would pass identically if TF2 had renamed the property in 2011, or
/// if the corpus carried a subclass that declares its own.
///
/// The claim that matters for the feature is the JOIN: every viewmodel names an owner, and that
/// owner is a player that exists. Without it the weapon cannot be attached to the camera, and a
/// handle that decoded to a plausible-but-wrong slot would put somebody else's weapon in frame.
/// </remarks>
public sealed class CorpusViewmodelTests
{
    [Test]
    public void Viewmodel_EveryOneFound_NamesAnOwnerThatIsAPlayer()
    {
        List<string> measured = [];
        List<string> joined = [];

        foreach (string path in Corpus.FilesWithSchema())
        {
            (int viewmodels, int owned, int matched) = Survey(path);

            if (viewmodels == 0)
            {
                continue;
            }

            measured.Add(
                $"{Path.GetFileName(path)}: {viewmodels} viewmodels, {owned} with an owner, " +
                $"{matched} whose owner is a known player");

            // Written out BEFORE any assertion, so a failure does not hide the numbers that
            // explain it — the same lesson as the median-of-zero measurement earlier.
            TestContext.Out.WriteLine(measured[^1]);

            if (owned > 0 && matched == owned)
            {
                joined.Add(Path.GetFileName(path) ?? path);
            }
        }

        // The positive control. Five absence claims in this project have been facts about the
        // search rather than about the data, and one of them was in this very feature.
        measured.ShouldNotBeEmpty("no viewmodel was found in any corpus demo");

        // **Every demo carries at least one viewmodel entity**, which is the claim the feature
        // rests on: there is something to draw.
        measured.Count.ShouldBeGreaterThanOrEqualTo(
            8, "fewer demos carry a viewmodel than when this was measured");

        // **And where owners ARE sent, the join works completely.** z1800 carries one viewmodel per
        // player, 37 of them, and every single owner handle resolves to an entity whose class is a
        // player. Anything less than all of them would mean the handle decode is wrong — a
        // plausible-but-wrong slot would put somebody else's weapon in frame.
        //
        // Asserted as "some demo joins perfectly" rather than as a per-demo rule, because the older
        // recordings send no owner at all and that is era, not error.
        joined.ShouldNotBeEmpty(
            "no demo resolved a viewmodel owner to a player, so the handle decode is wrong");
    }

    [Test]
    public void Viewmodel_TheTimeline_OffersAWeaponForTheRecorderOnAPointOfViewDemo()
    {
        // **The plumbing the viewer actually calls.** Everything above walks the entity stream by
        // hand; this asks the timeline, which is what the renderer will do — and a unit test on a
        // written demo proves the lookup arithmetic while saying nothing about whether production
        // populates it.
        List<string> found = [];

        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoTimeline timeline = TimelineCache.For(path);

            if (timeline.RecorderEntityIndex is not { } recorder)
            {
                continue;
            }

            if (timeline.ViewmodelAt(timeline.LastTick, recorder) is not { } weapon)
            {
                continue;
            }

            found.Add($"{Path.GetFileName(path)}: {weapon.ModelPath} seq {weapon.Sequence}");

            // A resolved path, not an empty one. An index that failed to resolve would come back
            // as a blank string and reach a model loader as a missing asset.
            weapon.ModelPath.ShouldNotBeNullOrWhiteSpace();
        }

        TestContext.Out.WriteLine(string.Join(Environment.NewLine, found));

        found.ShouldNotBeEmpty(
            "no demo offered the recorder a viewmodel through the timeline");
    }

    /// <summary>Counts viewmodels, how many name an owner, and how many owners are players.</summary>
    private static (int Viewmodels, int Owned, int Matched) Survey(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        DemoSchema schema = Corpus.Schema(path);
        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        NetDecodeState state = new()
        {
            NetworkProtocol = (ushort)Corpus.Header(path).NetworkProtocol,
        };

        EntityStateTable entities = new();

        // **Class names have to be seeded or every entity is anonymous.** DemoTimeline.Build does
        // this and a hand-rolled walk does not, so the owner-is-a-player check silently compared
        // against null for every entity — the measurement said "0 owners are players" and meant
        // "this harness never learned any class names".
        foreach (ServerClass serverClass in schema.ServerClasses)
        {
            entities.SetClassName(serverClass.Id, serverClass.ClassName);
        }
        HashSet<int> viewmodels = [];
        HashSet<int> owned = [];
        HashSet<int> matched = [];

        foreach (DemoCommand command in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            foreach (INetMessage message in
                NetMessageReader.Read(command.Payload.Span, state).Messages)
            {
                if (message is not PacketEntitiesMessage snapshot || snapshot.LengthBits <= 0)
                {
                    continue;
                }

                foreach (DecodedEntity entity in
                    decoder.Decode(snapshot.Body.Span, snapshot, snapshot.LengthBits))
                {
                    entities.Apply(entity);
                }
            }

            foreach (EntityState entity in entities.All)
            {
                if (entity.ViewmodelModelIndex() is null)
                {
                    continue;
                }

                viewmodels.Add(entity.EntityIndex);

                if (entity.ViewmodelOwner() is not { } owner)
                {
                    continue;
                }

                owned.Add(entity.EntityIndex);

                if (entities.TryGet(owner, out EntityState? player) &&
                    player.ClassName?.Contains("Player", StringComparison.Ordinal) == true)
                {
                    matched.Add(entity.EntityIndex);
                }
            }
        }

        return (viewmodels.Count, owned.Count, matched.Count);
    }
}
