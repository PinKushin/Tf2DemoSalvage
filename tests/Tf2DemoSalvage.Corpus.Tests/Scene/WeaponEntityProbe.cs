using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// What a weapon entity actually carries, before the timeline's filters see it.
/// </summary>
/// <remarks>
/// A probe, not a test. <see cref="CarriedItemProbe"/> established that every weapon track in a
/// modern demo lives a median of 148 ticks and that exactly one is present at a mid-match tick —
/// the profile of weapons dropped on death, not of the twelve being held. So carried weapons are
/// lost before <see cref="ScenePropTrack"/> exists, and the only ways out of
/// <c>DemoTimeline.RecordProp</c> are a missing origin and a missing model.
///
/// This looks at the entities themselves to say which, because the two need completely different
/// work: a missing model is a precache lookup, a missing origin means resolving
/// <c>m_hMoveParent</c> and an attachment point on the owner's skeleton.
/// </remarks>
public sealed class WeaponEntityProbe
{
    [Test]
    public void WeaponEntities_TheCorpus_AreReported()
    {
        string path = Corpus.Demo("cp_process");

        ReadOnlyMemory<byte> file = File.ReadAllBytes(path);
        DemoHeader header = DemoHeader.Parse(file.Span);
        NetDecodeState state = new() { NetworkProtocol = (ushort)header.NetworkProtocol };

        List<DemoCommand> commands = [.. DemoCommandReader.Read(file[DemoHeader.SizeBytes..])];

        DemoCommand dataTables = commands.First(
            command => command.Type == DemoCommandType.DataTables);

        DemoSchema schema = SendTableParser.Parse(
            dataTables.Payload.Span, (ushort)header.NetworkProtocol);

        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
        EntityStateTable entities = new();

        foreach (ServerClass serverClass in schema.ServerClasses)
        {
            entities.SetClassName(serverClass.Id, serverClass.ClassName);
        }

        ModelPrecache precache = new();
        int processed = 0;
        SortedSet<string> tableNames = [];

        // **The demo's own mid-match tick, not a number that looks like one.** demos.tf recordings
        // start at whatever tick the server was on, which on this file is past 20000 — so stopping
        // at a hardcoded 20000 walked zero commands and reported an empty world with no error.
        int first = commands.First(command => command.Type == DemoCommandType.Packet).Tick;
        int last = commands.Last(command => command.Type == DemoCommandType.Packet).Tick;
        int stopAt = first + ((last - first) / 2);

        foreach (DemoCommand command in commands)
        {
            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            if (command.Tick > stopAt)
            {
                break;
            }

            processed++;

            foreach (INetMessage message in
                NetMessageReader.Read(command.Payload.Span, state).Messages)
            {
                // **What the tables are actually called.** The dynamic-model table is engine side
                // and named nowhere in the published SDK, so the demo is the only source for it —
                // and every recording lists its own tables by name.
                if (message is CreateStringTableMessage named)
                {
                    tableNames.Add($"{named.Name}({named.Entries.Count})");
                }

                switch (message)
                {
                    // Without these an entering entity is decoded against nothing and the stream
                    // desyncs mid-packet — which reads as a decoder bug and is a missing input.
                    case CreateStringTableMessage { Name: BaselineBuilder.TableName } create:
                        BaselineBuilder.Apply(create.Entries, decoder);
                        continue;

                    case UpdateStringTableMessage update
                        when state.StringTableName(update.TableId) == BaselineBuilder.TableName:
                        BaselineBuilder.Apply(update.Entries, decoder);
                        continue;

                    case CreateStringTableMessage { Name: ModelPrecache.TableName } models:
                        precache.Apply(models.Entries);
                        continue;

                    case UpdateStringTableMessage update
                        when state.StringTableName(update.TableId) == ModelPrecache.TableName:
                        precache.Apply(update.Entries);
                        continue;

                    default:
                        break;
                }

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
        }

        // Every live weapon at tick 20000, and what it does and does not say about itself.
        int withOrigin = 0;
        int withoutOrigin = 0;
        int withModelIndex = 0;
        List<string> examples = [];

        // **Every live entity with no origin, by class.** Naming the classes to look for was the
        // first mistake here — TF2's weapon classes are CTFScattergun and CTFRocketLauncher, with
        // no "Weapon" anywhere in them, so a substring filter reported zero and read as "there are
        // no weapons" rather than as "I asked the wrong question".
        Dictionary<string, (int With, int Without)> byClass = [];

        foreach (EntityState any in entities.All)
        {
            string key = any.ClassName ?? "(unnamed)";

            (int with, int without) = byClass.TryGetValue(key, out (int, int) seen) ? seen : (0, 0);

            byClass[key] = any.Origin() is null ? (with, without + 1) : (with + 1, without);
        }

        TestContext.Out.WriteLine(
            $"WEAP {processed} of {commands.Count} commands walked; " +
            $"{entities.All.Count()} live entities across {byClass.Count} classes: " +
            string.Join(
                ", ",
                byClass
                    .OrderByDescending(entry => entry.Value.With + entry.Value.Without)
                    .Take(14)
                    .Select(entry => $"{entry.Key} x{entry.Value.With + entry.Value.Without}")));

        TestContext.Out.WriteLine(
            "WEAP classes with entities carrying no origin: " + string.Join(
                ", ",
                byClass
                    .Where(entry => entry.Value.Without > 0)
                    .OrderByDescending(entry => entry.Value.Without)
                    .Take(12)
                    .Select(entry => $"{entry.Key} {entry.Value.Without}/{entry.Value.With + entry.Value.Without}")));

        // **EF_BONEMERGE is 0x001** (`public/const.h:284`), and CBaseCombatWeapon::Equip sets it
        // through FollowEntity (`shared/basecombatweapon_shared.cpp:987`). So an entity that hangs
        // off a player says so on the wire, in m_fEffects, and needs no origin BECAUSE the merge
        // takes the parent's bone matrices outright rather than offsetting from a position.
        const int BoneMerge = 0x001;

        int merging = 0;
        int withOwner = 0;

        foreach (EntityState weapon in entities.All)
        {
            if (weapon.ClassName is not { } name ||
                (!name.StartsWith("CTF", StringComparison.Ordinal) &&
                 !name.StartsWith("CWeapon", StringComparison.Ordinal)))
            {
                continue;
            }

            bool placed = weapon.Origin() is not null;

            _ = placed ? withOrigin++ : withoutOrigin++;

            if (weapon.ModelIndex() is not null)
            {
                withModelIndex++;
            }

            int effects = weapon.Integer("DT_BaseEntity.m_fEffects") ?? 0;

            if ((effects & BoneMerge) != 0)
            {
                merging++;
            }

            int? owner = weapon.Integer("DT_BaseEntity.m_hOwnerEntity");

            if (owner is not null)
            {
                withOwner++;
            }

            if (examples.Count < 6 && !placed)
            {
                examples.Add(
                    $"{name}#{weapon.EntityIndex} model=" +
                    $"{weapon.ModelIndex()?.ToString(CultureInfo.InvariantCulture) ?? "NO"} " +
                    $"effects=0x{effects:X} owner=" +
                    $"{owner?.ToString(CultureInfo.InvariantCulture) ?? "NO"}");
            }
        }

        TestContext.Out.WriteLine(
            $"WEAP of those, {merging} carry EF_BONEMERGE and {withOwner} name an owner entity");

        TestContext.Out.WriteLine(
            $"WEAP live weapon entities at tick 20000: {withOrigin} with an origin, " +
            $"{withoutOrigin} without, {withModelIndex} with a model index");

        TestContext.Out.WriteLine($"WEAP {string.Join("; ", examples)}");

        // **Do the wearables' model indices actually resolve?** A track is only made for an entity
        // whose model can be named, so a precache miss loses the cosmetic exactly as thoroughly as
        // a missing owner would — and the two are indistinguishable from the drawn scene.
        int resolved = 0;
        int unresolved = 0;
        List<string> missing = [];

        foreach (EntityState wearable in entities.All.Where(
            entity => string.Equals(entity.ClassName, "CTFWearable", StringComparison.Ordinal)))
        {
            if (wearable.ModelIndex() is not { } raw)
            {
                missing.Add("no index");
                continue;
            }

            string? resolvedPath = precache.Path(
                ModelPrecache.Unpack(raw, header.NetworkProtocol));

            if (resolvedPath is null)
            {
                unresolved++;

                if (missing.Count < 5)
                {
                    missing.Add($"index {raw.ToString(CultureInfo.InvariantCulture)} unknown");
                }
            }
            else
            {
                resolved++;
            }
        }

        TestContext.Out.WriteLine($"WEAP string tables: {string.Join(" ", tableNames)}");

        TestContext.Out.WriteLine(
            $"WEAP wearable models: {resolved} resolve, {unresolved} do not " +
            $"({string.Join("; ", missing)})");

        TestContext.Out.WriteLine(
            "WEAP wearable attachment: " + string.Join(
                ", ",
                entities.All
                    .Where(entity => string.Equals(entity.ClassName, "CTFWearable", StringComparison.Ordinal))
                    .Take(6)
                    .Select(entity =>
                        $"#{entity.EntityIndex}->{entity.Attachment()?.ToString(CultureInfo.InvariantCulture) ?? "NO"}")));

        // **A wearable and a weapon separately**, because they reach their owner by different
        // members in the SDK and assuming one answer for both is the shape of mistake this whole
        // investigation has been made of.
        foreach (string wanted in new[] { "CTFWearable", "CTFRocketLauncher", "CTFShovel" })
        {
            EntityState? one = entities.All.FirstOrDefault(
                entity => string.Equals(entity.ClassName, wanted, StringComparison.Ordinal));

            if (one is null)
            {
                continue;
            }

            TestContext.Out.WriteLine(
                $"WEAP {wanted}#{one.EntityIndex}: " +
                string.Join(", ", one.Properties.Keys.OrderBy(name => name, StringComparer.Ordinal)));
        }

        // Which properties one of them actually holds — the point being that guessing the name of
        // the parent property is exactly the mistake that made this take two attempts already.
        EntityState? sample = entities.All.FirstOrDefault(
            entity => entity.ClassName?.Contains("Weapon", StringComparison.Ordinal) == true);

        if (sample is not null)
        {
            TestContext.Out.WriteLine(
                $"WEAP properties of {sample.ClassName}#{sample.EntityIndex}: " +
                string.Join(", ", sample.Properties.Keys.OrderBy(name => name, StringComparer.Ordinal)));
        }

        Assert.Pass();
    }
}
