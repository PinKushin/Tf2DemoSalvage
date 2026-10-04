using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// How many instances of each entity class, and how many of each temp entity, a demo carries — counted
/// on the WIRE, before any admission gate.
/// </summary>
/// <remarks>
/// **Below the timeline on purpose.** `projectiles` and `props` count tracks, and a track is what
/// survives `DemoTimeline`'s admission gate — so a class the gate drops (a NOBASE table such as
/// `DT_Beam` or `DT_RopeKeyframe`, whose model index is not `DT_BaseEntity`'s) is invisible to them,
/// which is the question restated rather than evidence. This counts each (index, serial) that ENTERS
/// and each temp entity that arrives, then prints the first instance of every matching class in full.
///
/// <code>
///   entity-census cp_process_f12 Beam,Rope,SpriteTrail
/// </code>
/// </remarks>
public sealed class EntityCensusProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "entity-census";

    /// <inheritdoc/>
    public string Summary =>
        "entity instances and temp entities by class on the wire, pre-gate, with a first example: " +
        "entity-census <demo|directory> [class substrings] [property suffixes to tally], each comma separated";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("entity-census <demo|directory> [class substrings] [property suffixes to tally]");
            return;
        }

        string[] filters = arguments.Count > 1
            ? arguments[1].Split(',', StringSplitOptions.RemoveEmptyEntries)
            : [];

        string[] tallySuffixes = arguments.Count > 2
            ? arguments[2].Split(',', StringSplitOptions.RemoveEmptyEntries)
            : [];

        // **A directory is every demo in it, one at a time** — the census question is usually "which
        // demos carry this at all", and a shell loop over paths is refused in a worktree.
        if (Directory.Exists(arguments[0]))
        {
            foreach (string each in Directory.EnumerateFiles(arguments[0], "*.dem").Order(StringComparer.Ordinal))
            {
                Census(output, each, filters, tallySuffixes);
            }

            return;
        }

        if (DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        Census(output, path, filters, tallySuffixes);
    }

    private static void Census(TextWriter output, string path, string[] filters, string[] tallySuffixes)
    {
        bool Wanted(string className) =>
            filters.Length == 0 ||
            filters.Any(filter => className.Contains(filter, StringComparison.OrdinalIgnoreCase));

        byte[] bytes = File.ReadAllBytes(path);
        ushort protocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol;
        List<DemoCommand> commands = [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))];

        DemoCommand tables = commands.FirstOrDefault(command => command.Type == DemoCommandType.DataTables);

        if (tables.Type != DemoCommandType.DataTables)
        {
            output.WriteLine("The demo carries no dem_datatables.");
            return;
        }

        DemoSchema schema = SendTableParser.Parse(tables.Payload.Span, protocol);
        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
        EntityStateTable entities = new(decoder);
        Dictionary<int, string> names = [];

        foreach (ServerClass serverClass in schema.ServerClasses)
        {
            entities.SetClassName(serverClass.Id, serverClass.ClassName);
            names[serverClass.Id] = serverClass.ClassName;
        }

        NetDecodeState state = new() { NetworkProtocol = protocol };
        Dictionary<string, HashSet<(int Index, int Serial)>> instances = new(StringComparer.Ordinal);
        Dictionary<string, int> effects = new(StringComparer.Ordinal);
        Dictionary<string, string> firstEntity = new(StringComparer.Ordinal);
        Dictionary<string, string> firstEffect = new(StringComparer.Ordinal);
        Dictionary<string, int> tallies = new(StringComparer.Ordinal);
        ModelPrecache precache = new();

        // **What each matching entity ENTERS with, tallied over the named property suffixes** — the
        // distribution of a field, which one example cannot give. A model index prints as its path.
        string Tally(EntityState live) => string.Join(" ", tallySuffixes.Select(suffix =>
        {
            string? key = live.Properties.Keys.FirstOrDefault(
                name => name.EndsWith(suffix, StringComparison.Ordinal));

            if (key is null)
            {
                return $"{suffix}=-";
            }

            PropertyValue value = live.Properties[key];

            return suffix.Contains("ModelIndex", StringComparison.Ordinal) ||
                   suffix.Contains("HaloIndex", StringComparison.Ordinal)
                ? $"{suffix}={precache.Path(ModelPrecache.Unpack((int)value.AsInt, protocol))}"
                : $"{suffix}={value}";
        }));

        foreach (DemoCommand command in commands.Where(
            command => command.Type is DemoCommandType.Packet or DemoCommandType.Signon))
        {
            foreach (INetMessage message in NetMessageReader.Read(command.Payload.Span, state).Messages)
            {
                switch (message)
                {
                    case CreateStringTableMessage { Name: ModelPrecache.TableName } models:
                        precache.Replace(models.Entries);
                        break;

                    case UpdateStringTableMessage update
                        when state.StringTableName(update.TableId) == ModelPrecache.TableName:
                        precache.Apply(update.Entries);
                        break;

                    case CreateStringTableMessage { Name: BaselineBuilder.TableName } create:
                        BaselineBuilder.Replace(create.Entries, decoder);
                        break;

                    case UpdateStringTableMessage update
                        when state.StringTableName(update.TableId) == BaselineBuilder.TableName:
                        BaselineBuilder.Apply(update.Entries, decoder);
                        break;

                    case TempEntitiesMessage temp when temp.BodyBits > 0:
                        try
                        {
                            foreach (DecodedTempEntity effect in
                                decoder.DecodeTempEntities(temp.Body.Span, temp.Count, temp.BodyBits))
                            {
                                string name = names.GetValueOrDefault(effect.ClassId, "?");
                                effects[name] = effects.GetValueOrDefault(name) + 1;

                                if (Wanted(name) && !firstEffect.ContainsKey(name))
                                {
                                    firstEffect[name] = string.Create(
                                        CultureInfo.InvariantCulture,
                                        $"tick {command.Tick}: ") + string.Join(", ", effect.State.Select(
                                            property => $"{property.Definition.Property.Name}={property.Value}"));
                                }
                            }
                        }
                        catch (Exception error) when (error is InvalidDataException or EndOfStreamException)
                        {
                            output.WriteLine($"  tick {command.Tick}: temp entities skipped, {error.Message}");
                        }

                        break;

                    case PacketEntitiesMessage snapshot when snapshot.LengthBits > 0:
                        entities.PacketTick = command.Tick;

                        foreach (DecodedEntity entity in
                            decoder.Decode(snapshot.Body.Span, snapshot, snapshot.LengthBits))
                        {
                            entities.Apply(entity);

                            if (!entities.TryGet(entity.EntityIndex, out EntityState? live) ||
                                live.ClassName is null)
                            {
                                continue;
                            }

                            if (!instances.TryGetValue(live.ClassName, out HashSet<(int, int)>? seen))
                            {
                                instances[live.ClassName] = seen = [];
                            }

                            bool fresh = seen.Add((entity.EntityIndex, live.SerialNumber));

                            if (fresh && tallySuffixes.Length > 0 && Wanted(live.ClassName))
                            {
                                string key = $"{live.ClassName} {Tally(live)}";
                                tallies[key] = tallies.GetValueOrDefault(key) + 1;
                            }

                            if (Wanted(live.ClassName) && !firstEntity.ContainsKey(live.ClassName) &&
                                entity.UpdateType == EntityUpdateType.Enter)
                            {
                                firstEntity[live.ClassName] = string.Create(
                                    CultureInfo.InvariantCulture,
                                    $"tick {command.Tick} entity {entity.EntityIndex}: ") +
                                    string.Join(", ", live.Properties
                                        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                                        .Select(pair => $"{pair.Key}={pair.Value}"));
                            }
                        }

                        break;

                    default:
                        break;
                }
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)} protocol {protocol}: {instances.Values.Sum(set => set.Count):N0} " +
            $"entity instances, {effects.Values.Sum():N0} temp entities"));

        output.WriteLine("ENTITIES (distinct index+serial):");

        foreach ((string className, HashSet<(int, int)> set) in instances
            .Where(pair => Wanted(pair.Key))
            .OrderByDescending(pair => pair.Value.Count))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {className,-32} {set.Count,6}"));
        }

        output.WriteLine("TEMP ENTITIES:");

        foreach ((string className, int count) in effects
            .Where(pair => Wanted(pair.Key))
            .OrderByDescending(pair => pair.Value))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {className,-32} {count,6}"));
        }

        foreach ((string key, int count) in tallies.OrderByDescending(pair => pair.Value))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"TALLY {count,5} {key}"));
        }

        if (filters.Length == 0 || tallySuffixes.Length > 0)
        {
            return;
        }

        foreach ((string className, string example) in firstEntity.Concat(firstEffect))
        {
            output.WriteLine($"FIRST {className} {example}");
        }
    }
}
