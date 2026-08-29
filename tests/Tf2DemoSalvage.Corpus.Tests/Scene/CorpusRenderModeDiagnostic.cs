using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// How much render state real matches actually carry — a MEASUREMENT, not a test.
/// </summary>
/// <remarks>
/// **This was written as `CorpusRenderModeTests` and that was a D38 violation.** It asserted what
/// real demos contain, and D38 answers that directly: *"why do we need to verify a demo has
/// anything?"* — an assertion that a recording holds a translucent entity is a claim about TF2, not
/// about this parser. It was also in `Corpus.Tests`, which needs Git LFS and which Stryker
/// therefore never mutates.
///
/// **The decode it appeared to cover is covered properly** by `RenderStateDecodeTests` in
/// `Core.Tests` — synthetic, exact, and it runs everywhere including CI and the measurement boxes.
///
/// **What survives here is the number**, because the number is what justified B221 and is not
/// derivable from the SDK. Measured 2026-08-29 across three real matches:
///
/// <code>
///   demostf-cp_process_f12-2026-08-07: 612 entities, 0 not fully opaque
///   etf2l-12025-pov-2020-07-21:        604 entities, 83 not fully opaque
///   tf2-2026-pub-pov-clean:            757 entities, 327 not fully opaque
///   render modes: 0=1852, 3=2, 5=1, 10=118
///   render fx:    0=1706, 1=24, 2=18, 4=24, 9=36, 11=15, 12=76, 13=64, 23=10
/// </code>
///
/// **118 entities at `kRenderNone`** — *"Don't render."* — is the sharpest of those: until the
/// render mode was decoded, every one of them was drawn.
///
/// Explicit, and it asserts nothing: it reports so the numbers can be taken again, and cannot fail
/// because somebody swapped a demo.
/// </remarks>
[Explicit("Diagnostic: reports the render state real matches carry.")]
public sealed class CorpusRenderModeDiagnostic
{
    /// <summary>Real matches, both points of view, rather than the era specimens.</summary>
    /// <remarks>
    /// Era specimens are solo recordings on period clients with nobody else on the server, so they
    /// carry almost no entities that could be tinted or faded — they would inflate the denominator
    /// and measure nothing, which is `CorpusPlayerOriginTests`' argument applied here.
    /// </remarks>
    private static readonly string[] Sampled =
    [
        "demostf-cp_process_f12-2026-08-07",
        "etf2l-12025-pov-2020-07-21",
        "tf2-2026-pub-pov-clean",
    ];

    [Test]
    public void Decode_AcrossRealMatches_ReportsWhichEntitiesCarryARenderMode()
    {
        Dictionary<int, int> modes = [];
        Dictionary<int, int> effects = [];
        int entities = 0;
        int tinted = 0;
        int demos = 0;

        IReadOnlyList<string> available = Corpus.FilesWithSchema();

        foreach (string fragment in Sampled)
        {
            string? path = available.FirstOrDefault(
                file => Path.GetFileName(file).Contains(fragment, StringComparison.Ordinal));

            if (path is null)
            {
                TestContext.Out.WriteLine($"absent: {fragment}");
                continue;
            }

            EntityStateTable table = Accumulate(path, packetLimit: 3000);

            demos++;

            int here = 0;
            int hereTinted = 0;

            foreach (EntityState entity in table.All)
            {
                entities++;
                here++;

                int mode = entity.RenderMode() ?? 0;
                int effect = entity.RenderFx() ?? 0;

                modes[mode] = modes.GetValueOrDefault(mode) + 1;
                effects[effect] = effects.GetValueOrDefault(effect) + 1;

                if (entity.RenderAlpha() != 255)
                {
                    tinted++;
                    hereTinted++;
                }
            }

            TestContext.Out.WriteLine(
                $"{fragment}: {here} entities, {hereTinted} not fully opaque");
        }

        TestContext.Out.WriteLine(
            "render modes: " + string.Join(
                ", ", modes.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}")));

        TestContext.Out.WriteLine(
            "render fx: " + string.Join(
                ", ", effects.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}")));

        TestContext.Out.WriteLine($"total {entities} entities, {tinted} not fully opaque");

        // **No assertion, deliberately.** This reports; it does not judge. A diagnostic that failed
        // when somebody swapped a demo would be the corpus test this replaced, wearing a new name —
        // and `kRenderNone = 10` is the value worth reading out of the modes line above
        // (<c>public/const.h:363</c>, *"Don't render."*).
        if (demos == 0)
        {
            Assert.Ignore("no real match was available; this needs lcor.");
            return;
        }

        // **A precondition on the HARNESS, not a claim about the data** — the same shape
        // `WorldCullingDiagnostic` uses. "The decoder produced entities" says this measurement
        // actually ran; it says nothing about what a demo contains, which is the assertion D38
        // rules out and which this file used to make.
        entities.ShouldBeGreaterThan(0, "the sampled demos decoded into no entities at all");
    }

    /// <summary>Every entity a demo's first snapshots produce.</summary>
    /// <remarks>
    /// **Copied from <c>CorpusSceneTests.Accumulate</c> rather than written afresh**, because the
    /// first attempt here was written from memory and got four things wrong at once: the table needs
    /// its baselines (an entering entity is a delta against its class baseline), the command type is
    /// <c>Signon</c> not <c>SignOn</c>, `NetMessageReader.Read` returns a result whose `.Messages`
    /// carries the list, and the decoder produces `DecodedEntity` for the table to `Apply` rather
    /// than taking the snapshot itself.
    ///
    /// The class names come from <c>dem_datatables</c> rather than <c>svc_ClassInfo</c>, for the
    /// reason that file records: TF2 sets "create on client" and sends no names.
    /// </remarks>
    private static EntityStateTable Accumulate(string path, int packetLimit)
    {
        byte[] file = File.ReadAllBytes(path);
        DemoHeader header = DemoHeader.Parse(file);
        NetDecodeState state = new() { NetworkProtocol = (ushort)header.NetworkProtocol };

        List<DemoCommand> commands =
            [.. DemoCommandReader.Read(file.AsMemory(DemoHeader.SizeBytes))];

        EntityDecoder? decoder = null;
        IReadOnlyList<ServerClass> classes = [];
        DemoCommand? tables = commands.FirstOrDefault(c => c.Type == DemoCommandType.DataTables);

        if (tables is { } dataTables)
        {
            DemoSchema schema = SendTableParser.Parse(
                dataTables.Payload.Span, (ushort)header.NetworkProtocol);

            decoder = new EntityDecoder(
                schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

            classes = schema.ServerClasses;
        }

        EntityStateTable table = new((IEntityBaselines?)decoder ?? EntityBaselines.None);

        foreach (ServerClass serverClass in classes)
        {
            table.SetClassName(serverClass.Id, serverClass.ClassName);
        }

        if (decoder is null)
        {
            return table;
        }

        int snapshots = 0;

        foreach (DemoCommand command in commands
            .Where(c => c.Type is DemoCommandType.Signon or DemoCommandType.Packet))
        {
            foreach (INetMessage message in NetMessageReader.Read(command.Payload.Span, state)
                .Messages)
            {
                if (message is not PacketEntitiesMessage snapshot || snapshot.LengthBits <= 0)
                {
                    continue;
                }

                foreach (DecodedEntity entity in
                    decoder.Decode(snapshot.Body.Span, snapshot, snapshot.LengthBits))
                {
                    table.Apply(entity);
                }

                if (++snapshots >= packetLimit)
                {
                    return table;
                }
            }
        }

        return table;
    }
}
