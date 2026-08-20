using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// Baselines reaching the trace, and the progress report a long trace makes.
/// </summary>
/// <remarks>
/// **An entering entity is a delta against its class's instance baseline**, so a trace that ignores
/// baselines shows an entity missing most of what the server had already told the client — and
/// shows it as an entity that simply did not send those properties. The two look identical in the
/// output, which is why this needs a demo where the difference is constructed: the entity below
/// omits a property the baseline supplies, so the value can only have come from one place.
///
/// Applying baselines was added to <c>DemoTimeline</c> long before the trace, and the trace's copy
/// went unasserted because <b>no corpus demo changes shape either way</b> — TF2's baselines repeat
/// what the snapshot sends. Written data is what makes the mechanism observable at all.
/// </remarks>
public sealed class TraceBaselineAndProgressTests
{
    /// <summary>The property the baseline states and the snapshot omits.</summary>
    private const string BaselineOnly = "m_nSkin";

    /// <summary>The property the snapshot states, so the merge has both directions to get right.</summary>
    private const string SnapshotOnly = "m_lifeState";

    [Test]
    public void Trace_APropertyOnlyTheBaselineStated_AppearsOnTheEnteringEntity()
    {
        // The mechanism, at the level of the rendered artefact rather than the decoder's API.
        // A trace with no baseline handling omits this line entirely and reports no error.
        string trace = Trace(Demo(viaUpdate: false));

        trace.ShouldContain($"{BaselineOnly} 7");
    }

    [Test]
    public void Trace_APropertyTheSnapshotStated_KeepsTheSnapshotValue()
    {
        // The other direction of the same merge, and the one that fails silently if the baseline
        // is applied on top rather than underneath: the server's newest word must win.
        string trace = Trace(Demo(viaUpdate: false));

        trace.ShouldContain($"{SnapshotOnly} 1");

        // The baseline said something different for it, so the stale value must not survive.
        trace.ShouldNotContain($"{SnapshotOnly} 0");
    }

    [Test]
    public void Trace_ABaselineArrivingByTableUpdate_IsAppliedTheSameWay()
    {
        // **Baselines arrive twice: on the create, and on later updates as new classes appear.**
        // An update names its table only by ID, so the trace has to resolve that ID through the
        // decode state — a separate code path from the create, and one a demo that creates the
        // table already populated never reaches.
        string trace = Trace(Demo(viaUpdate: true));

        trace.ShouldContain($"{BaselineOnly} 7");
    }

    [Test]
    public void Trace_WithoutABaseline_OmitsTheUnsentProperty()
    {
        // **The control.** Without it, an assertion that a property appears cannot distinguish
        // "the baseline supplied it" from "the fixture sent it after all" — the first is what is
        // being tested and the second would pass identically.
        string trace = Trace(DemoWithoutBaseline());

        trace.ShouldNotContain(BaselineOnly);
        trace.ShouldContain($"{SnapshotOnly} 1");
    }

    [Test]
    public void Trace_WithAProgressReporter_ReportsTheCommandsItWalked()
    {
        // Progress is reported every 512 commands AND on the last one, so a short demo exercises
        // only the second condition — which is the one that matters, because a reporter that
        // never fires on a demo under 512 commands looks broken to a caller with a progress bar.
        List<DumpProgress> reports = [];

        (DemoHeader header, IReadOnlyList<DemoCommand> commands) = Read(Demo(viaUpdate: false));

        StringWriter text = new() { NewLine = "\n" };
        DemoTraceWriter.Write(
            text, "synthetic.dem", header, commands, new Progress(reports.Add));

        reports.ShouldNotBeEmpty();

        DumpProgress last = reports[^1];
        last.Stage.ShouldBe("Tracing");
        last.Completed.ShouldBe(commands.Count);
        last.Total.ShouldBe(commands.Count);
    }

    /// <summary>A synchronous <see cref="IProgress{T}"/>, so a report is observable in-test.</summary>
    /// <remarks>
    /// <see cref="Progress{T}"/> posts to a synchronisation context, so on a test thread its
    /// callback runs at an unspecified later moment — which would make this a race rather than a
    /// measurement, and a passing one most of the time.
    /// </remarks>
    private sealed class Progress(Action<DumpProgress> onReport) : IProgress<DumpProgress>
    {
        public void Report(DumpProgress value) => onReport(value);
    }

    /// <summary>
    /// A demo whose instance baseline states a property the snapshot leaves out.
    /// </summary>
    /// <param name="viaUpdate">
    /// Whether the baseline arrives on a later <c>svc_UpdateStringTable</c> rather than in the
    /// table that created it.
    /// </param>
    private static byte[] Demo(bool viaUpdate)
    {
        DemoSchema schema = SyntheticPlayer.Schema();
        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        byte[] baseline = Payload(
            decoder,
            (BaselineOnly, 7),

            // Stated here as well, and differently, so the merge has a value to be overridden.
            (SnapshotOnly, 0));

        List<INetMessage> signon =
        [
            viaUpdate

                // An empty table first, so the update below has an ID to name and the writer's
                // state has learned it.
                ? SyntheticDemo.StringTable(
                    BaselineBuilder.TableName,
                    Array.Empty<(string, IReadOnlyList<byte>)>(),
                    maxEntries: 64)
                : SyntheticDemo.StringTable(
                    BaselineBuilder.TableName,
                    [(ClassKey, baseline)],
                    maxEntries: 64),
        ];

        if (viaUpdate)
        {
            signon.Add(SyntheticDemo.UpdateTable(
                tableId: 0, [(ClassKey, baseline)], maxEntries: 64));
        }

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 0, [.. signon]),
            SyntheticDemo.DataTables(schema),
            Snapshot(decoder));
    }

    /// <summary>The same demo with no baseline table at all.</summary>
    private static byte[] DemoWithoutBaseline()
    {
        DemoSchema schema = SyntheticPlayer.Schema();
        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.DataTables(schema),
            Snapshot(decoder));
    }

    /// <summary>A snapshot with one entering player that states only <see cref="SnapshotOnly"/>.</summary>
    private static DemoCommand Snapshot(EntityDecoder decoder)
    {
        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(SyntheticPlayer.PlayerClassId);
        int index = IndexOf(flat, SnapshotOnly);

        DecodedEntity entity = new(
            EntityIndex: 1,
            ClassId: SyntheticPlayer.PlayerClassId,
            SerialNumber: 1,
            EntityUpdateType.Enter,
            [new DecodedProperty(index, flat[index], PropertyValue.FromInt(1))]);

        byte[] body = decoder.EncodeEntities([entity], [], isDelta: false, 0, out int bits);

        return SyntheticDemo.Packet(
            SyntheticDemo.DefaultProtocol,
            66,
            new PacketEntitiesMessage(
                MaxEntries: 64,
                IsDelta: false,
                DeltaFromTick: null,
                BaselineIndex: false,
                UpdatedEntries: 1,
                LengthBits: bits,
                UpdateBaseline: false,
                Body: body));
    }

    /// <summary>An instance baseline entry is keyed by CLASS ID, as text.</summary>
    private static string ClassKey =>
        SyntheticPlayer.PlayerClassId.ToString(CultureInfo.InvariantCulture);

    /// <summary>The encoded property block a baseline carries.</summary>
    private static byte[] Payload(
        EntityDecoder decoder, params (string Name, int Value)[] properties)
    {
        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(SyntheticPlayer.PlayerClassId);

        List<DecodedProperty> decoded = [.. properties
            .Select(property =>
            {
                int index = IndexOf(flat, property.Name);
                return new DecodedProperty(
                    index, flat[index], PropertyValue.FromInt(property.Value));
            })
            .OrderBy(property => property.Index)];

        return EntityDecoder.EncodeProperties(decoded);
    }

    private static int IndexOf(IReadOnlyList<FlatProperty> flat, string name) =>
        flat.Select((entry, index) => (entry, index))
            .First(pair => pair.entry.Property.Name == name).index;

    private static string Trace(byte[] demo)
    {
        (DemoHeader header, IReadOnlyList<DemoCommand> commands) = Read(demo);

        StringWriter text = new() { NewLine = "\n" };
        DemoTraceWriter.Write(
            text,
            "synthetic.dem",
            header,
            commands,
            options: new DemoTraceOptions { IncludeEntities = true });

        return text.ToString();
    }

    private static (DemoHeader Header, IReadOnlyList<DemoCommand> Commands) Read(byte[] demo) =>
        (DemoHeader.Parse(demo.AsSpan(0, DemoHeader.SizeBytes)),
            [.. DemoCommandReader.Read(demo.AsMemory(DemoHeader.SizeBytes))]);
}
