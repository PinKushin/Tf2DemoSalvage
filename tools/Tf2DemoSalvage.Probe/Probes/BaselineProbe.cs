using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Which classes a demo gives an instance baseline, and what that baseline says.
/// </summary>
/// <remarks>
/// **Written for B245, and the question generalises.** An entity entering the potentially visible
/// set is a delta against a baseline, so anything the baseline does NOT declare cannot be restored
/// by an <c>ENTER</c> — it simply keeps whatever the reader last accumulated. That makes "does this
/// class's baseline declare this property" the difference between a value that can go stale and one
/// that cannot, and nothing could answer it.
///
/// The specific case: a `CTFBonesaw` last stated <c>m_iState 2</c> at tick 8060 was still ACTIVE
/// six thousand ticks later, so its owner drew two weapons at once. If `CTFBonesaw` has no class
/// baseline, or has one that omits <c>m_iState</c>, the staleness is structural rather than a
/// bookkeeping slip.
///
/// **The scale is worth knowing on its own:** `tf2-2026-pub-pov-clean` declares an
/// `instancebaseline` table of **40 entries against 363 server classes**, so most classes have no
/// class baseline at all.
///
/// **It walks the demo with the same public helpers production uses, in the same order** —
/// `DemoCommandReader`, `SendTableParser`, `NetMessageReader` and `BaselineBuilder.Apply` — rather
/// than reimplementing the pipeline. A probe that parsed baselines its own way would agree with
/// whoever wrote the probe (D126).
///
/// <code>
///   baseline tf2-2026-pub-pov-clean
///   baseline tf2-2026-pub-pov-clean CTFBonesaw
///   baseline tf2-2026-pub-pov-clean CTFBonesaw m_iState
/// </code>
/// </remarks>
public sealed class BaselineProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "baseline";

    /// <inheritdoc/>
    public string Summary =>
        "which classes have an instance baseline, and what it declares: baseline <demo> [class] [prop]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("baseline <demo> [class] [property]");
            return;
        }

        if (DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        string wantedClass = arguments.Count > 1 ? arguments[1] : string.Empty;
        string wantedProp = arguments.Count > 2 ? arguments[2] : string.Empty;

        byte[] bytes = File.ReadAllBytes(path);
        ushort protocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol;

        List<DemoCommand> commands = [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))];

        DemoSchema? schema = commands
            .Where(command => command.Type == DemoCommandType.DataTables)
            .Select(command => SendTableParser.Parse(command.Payload.Span, protocol))
            .FirstOrDefault();

        if (schema is null)
        {
            output.WriteLine("The demo carries no dem_datatables, so it declares no classes.");
            return;
        }

        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        // **State is carried across commands**, because `UpdateStringTableMessage` names a table by
        // id and only the create that preceded it says which table that is.
        NetDecodeState state = new();

        // **Every entry the table ever carried, and whether it had a payload.** `BaselineBuilder`
        // skips an entry whose `UserData` is empty, so a class whose baseline legitimately encodes
        // NOTHING — every property at its default — is indistinguishable from a class the table
        // never mentions. Those are different facts and the difference decides B245: the engine
        // `Host_Error`s when `GetClassBaseline` fails, so a class whose entities enter the visible
        // set must have an entry, and an empty one means "all defaults".
        Dictionary<string, int> entriesSeen = new(StringComparer.Ordinal);

        // **Routing counters, because "no entries" has three different causes.** The table's create
        // may never be seen, its updates may never be routed to it, or they may be routed and yield
        // nothing. Only counting each hop separately tells them apart, and a bare entry count reads
        // the same way for all three.
        int creates = 0;
        int updatesRouted = 0;
        int updatesTotal = 0;
        int entriesOffered = 0;

        void Record(IReadOnlyList<StringTableEntry> entries)
        {
            foreach (StringTableEntry seen in entries)
            {
                if (seen.Text is { } text)
                {
                    entriesSeen[text] = seen.UserData.Count;
                }
            }
        }

        foreach (DemoCommand command in commands.Where(
            command => command.Type is DemoCommandType.Packet or DemoCommandType.Signon))
        {
            foreach (INetMessage message in
                NetMessageReader.Read(command.Payload.Span, state).Messages)
            {
                switch (message)
                {
                    case CreateStringTableMessage { Name: BaselineBuilder.TableName } create:
                        creates++;
                        entriesOffered += create.Entries.Count;
                        Record(create.Entries);
                        BaselineBuilder.Apply(create.Entries, decoder);
                        break;

                    case UpdateStringTableMessage update
                        when state.StringTableName(update.TableId) == BaselineBuilder.TableName:
                        updatesRouted++;
                        updatesTotal++;
                        entriesOffered += update.Entries.Count;
                        Record(update.Entries);
                        BaselineBuilder.Apply(update.Entries, decoder);
                        break;

                    case UpdateStringTableMessage:
                        updatesTotal++;
                        break;

                    default:
                        break;
                }
            }
        }

        output.WriteLine(
            $"{Path.GetFileName(path)} protocol {protocol.ToString(CultureInfo.InvariantCulture)}, "
            + $"{schema.ServerClasses.Count.ToString(CultureInfo.InvariantCulture)} classes");

        int withBaseline = 0;

        foreach (ServerClass entry in schema.ServerClasses.OrderBy(
            entry => entry.ClassName, StringComparer.Ordinal))
        {
            IReadOnlyList<DecodedProperty>? baseline = decoder.Baseline(entry.Id);

            if (baseline is not null)
            {
                withBaseline++;
            }

            if (wantedClass.Length > 0
                && !entry.ClassName.Contains(wantedClass, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // **Null and empty are reported differently**, because they are different facts: a
            // class the table never mentions cannot restore anything, while one whose baseline is
            // empty was checkpointed as "all defaults".
            if (baseline is null)
            {
                // **Three states, not two.** No entry at all is a fact about the recording; an
                // entry with an empty payload is a baseline of all defaults, which the engine
                // treats as a perfectly good baseline and this reader discards.
                string id = entry.Id.ToString(CultureInfo.InvariantCulture);

                output.WriteLine(
                    $"  {entry.ClassName,-32} "
                    + (entriesSeen.TryGetValue(id, out int payload)
                        ? $"ENTRY id {id} with {payload.ToString(CultureInfo.InvariantCulture)} "
                            + "bytes of payload — SKIPPED as empty"
                        : $"NO ENTRY (id {id} never appears in the table)"));
                continue;
            }

            List<DecodedProperty> shown =
            [
                .. wantedProp.Length == 0
                    ? baseline
                    : baseline.Where(property => property.Definition.Property.Name.Contains(
                        wantedProp, StringComparison.OrdinalIgnoreCase)),
            ];

            output.WriteLine(
                $"  {entry.ClassName,-32} baseline of "
                + $"{baseline.Count.ToString(CultureInfo.InvariantCulture)} properties"
                + (wantedProp.Length > 0
                    ? $", {shown.Count.ToString(CultureInfo.InvariantCulture)} matching '{wantedProp}'"
                    : string.Empty));

            foreach (DecodedProperty property in shown)
            {
                output.WriteLine(
                    $"      {property.Definition.OwnerTable}."
                    + $"{property.Definition.Property.Name} = {property.Value}");
            }
        }

        // **The entry count is the check that matters, not the baseline count.** `GetClassBaseline`
        // — `GetDynamicBaseline` in the binary — formats the class id as a string, looks it up in
        // `instancebaseline`, and calls `Error(...)` when `FindStringIndex` misses. That is fatal,
        // so a class whose entities enter the visible set MUST have an entry in a recording the
        // game can play. Fewer entries here than classes-that-enter means this reader is losing
        // them, not that the recording lacks them.
        output.WriteLine(
            $"BASELINES {withBaseline.ToString(CultureInfo.InvariantCulture)} of "
            + $"{schema.ServerClasses.Count.ToString(CultureInfo.InvariantCulture)} classes carry one; "
            + $"the table yielded {entriesSeen.Count.ToString(CultureInfo.InvariantCulture)} distinct "
            + $"entries, {entriesSeen.Values.Count(bytes => bytes == 0).ToString(CultureInfo.InvariantCulture)} of them empty");

        output.WriteLine(
            $"ROUTING creates {creates.ToString(CultureInfo.InvariantCulture)}, "
            + $"updates {updatesRouted.ToString(CultureInfo.InvariantCulture)} routed here of "
            + $"{updatesTotal.ToString(CultureInfo.InvariantCulture)} seen, "
            + $"{entriesOffered.ToString(CultureInfo.InvariantCulture)} entries offered");
    }
}
