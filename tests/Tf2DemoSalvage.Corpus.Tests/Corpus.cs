using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Probe;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// Locates the reference demos, shared by every test that needs real files.
/// </summary>
/// <remarks>
/// **Finding the files is <see cref="DemoCorpus"/>'s, not this type's** (D126). The probes are
/// console programs rather than <c>[Explicit]</c> tests, and they need the same corpus this suite
/// needs — so the locating lives in the tool and this delegates to it. The direction is that way
/// round because the tool must not drag NUnit into a program whose point is to build quickly.
///
/// What stays here is everything a TEST needs on top: caches keyed by path, and
/// <see cref="Demo(string)"/>, which skips with a reason rather than throwing.
/// </remarks>
internal static class Corpus
{
    /// <summary>Anything smaller than this is a Git LFS pointer stub, not a demo.</summary>
    public const int SmallestPlausibleDemo = DemoCorpus.SmallestPlausibleDemo;

    /// <summary>The demo's network protocol, read from its header.</summary>
    /// <param name="path">Path to the demo.</param>
    /// <returns>The protocol, for seeding a <see cref="NetDecodeState"/>.</returns>
    /// <remarks>
    /// **Decoding a corpus demo without this is wrong, and wrong quietly.** The message type
    /// field is five bits at protocol 15 and below and six above (RISKS B17), so a default state
    /// reads one bit too many from every old demo and everything after it is noise.
    ///
    /// It hid twice. `CorpusNetMessageTests` passed because reading six bits where five were
    /// written gives the same value whenever the sixth is zero, which for a packet's first
    /// message it usually is. `CorpusGameEventTests` passed because the misdecode returned no
    /// event list at all, and that test treats a missing list as "not yet reachable" and skips -
    /// so the 2008 demo was silently excluded from both from the day it was added.
    /// </remarks>
    public static ushort ProtocolOf(string path)
    {
        byte[] header = new byte[DemoHeader.SizeBytes];
        using FileStream stream = File.OpenRead(path);
        stream.ReadExactly(header);
        return (ushort)DemoHeader.Parse(header).NetworkProtocol;
    }

    /// <summary>Whether this run is restricted to the committed corpus.</summary>
    /// <remarks>
    /// Set <c>TF2DEMOSALVAGE_GCOR_ONLY=1</c> to skip <c>tools/corpus/local</c>. Anything other
    /// than unset or "0" counts as on, so a typo errs towards the smaller, faster run rather than
    /// towards silently including 774 MB of demos.
    /// </remarks>
    public static bool GcorOnly() => DemoCorpus.GcorOnly();

    /// <summary>Every usable demo in the corpus, in a stable order.</summary>
    /// <remarks>
    /// **Opt out of the local corpus with <c>TF2DEMOSALVAGE_GCOR_ONLY</c>, for a run that matches
    /// CI.** lcor is 774 MB and takes about 23 minutes; gcor is one specimen per era and takes a
    /// fraction of that. The local set is for spot-checking across many real matches, not for
    /// gating a merge, so a merge should not have to wait for it.
    ///
    /// Announced rather than silent, which is why <see cref="TestContext.Out"/> is passed down: a
    /// suite that quietly halved its corpus would report a smaller total that reads as a passing
    /// run — the failure *"Passed! is not the result, the COUNT is"* is about.
    /// </remarks>
    public static IReadOnlyList<string> Files() => DemoCorpus.Files(TestContext.Out);

    /// <summary>The one demo whose name contains a fragment, skipping the test when there is none.</summary>
    /// <param name="fragment">Part of the file name, such as a map.</param>
    /// <returns>The path.</returns>
    /// <remarks>
    /// **Because a test whose specimen is missing has not failed — it has not run.** Several probes
    /// name a modern match that lives in the local corpus, which is git-ignored, so a
    /// <c>TF2DEMOSALVAGE_GCOR_ONLY</c> run threw <c>InvalidOperationException</c> from
    /// <c>First</c> and reported four failures that said nothing about the code.
    ///
    /// Ignoring says so in the run's own summary, where a skipped count is visible and a silently
    /// absent test is not. It deliberately does NOT fall back to another demo: the specimens differ
    /// enormously — the committed 2013 badlands POV carries 11 props and no wearables at all, so a
    /// test quietly redirected there would pass while measuring nothing.
    /// </remarks>
    public static string Demo(string fragment)
    {
        string? found = FilesWithSchema()
            .FirstOrDefault(file => Path.GetFileName(file).Contains(fragment, StringComparison.Ordinal));

        if (found is null)
        {
            Assert.Ignore(
                $"No demo named '{fragment}' is present. It lives in the local corpus, which is " +
                "not committed; unset TF2DEMOSALVAGE_GCOR_ONLY and add it to run this.");
        }

        // Assert.Ignore throws, so anything past it has a value — the analyser can see that and
        // says so, which is a better guarantee than a forgiving operator would have been.
        return found;
    }

    /// <summary>
    /// Walks up from the test binary looking for the corpus, rather than hard-coding a
    /// relative depth that breaks whenever the output path changes.
    /// </summary>
    public static string? Directory() => DemoCorpus.Directory();

    /// <summary>Parsed schemas, keyed by demo path.</summary>
    private static readonly ConcurrentDictionary<string, DemoSchema> Schemas =
        new(StringComparer.Ordinal);

    /// <summary>Parsed headers, keyed by demo path.</summary>
    private static readonly ConcurrentDictionary<string, DemoHeader> Headers =
        new(StringComparer.Ordinal);

    /// <summary>The demo's header.</summary>
    /// <param name="path">Path to a corpus demo.</param>
    /// <returns>The parsed header.</returns>
    /// <remarks>
    /// **Reads 1,072 bytes, not the file.** It used to be
    /// <c>DemoHeader.Parse(File.ReadAllBytes(p))</c>, which pulled an entire demo off disk to parse
    /// its first kilobyte — reported 2026-08-21 by the tf2-comp-archive agent in
    /// <c>PinKushin/TF2DEMOSALVAGE-LOG.md</c> while building an independent header reader.
    ///
    /// Across the 53 committed and local demos that is 1,757 MB read against 0.057 MB needed, a
    /// factor of about 31,000. **The I/O was probably not the real cost** — the dictionary caches
    /// per path, the OS page-caches the files and most tests parse them fully anyway — but
    /// <c>File.ReadAllBytes</c> on a 100 MB demo puts a 100 MB array straight on the Large Object
    /// Heap, and doing that per demo is GC pressure that shows up as pauses rather than as slow
    /// reads.
    ///
    /// <see cref="ProtocolOf"/> already did it this way, so the two paths to a header disagreed with
    /// each other. That is the part worth fixing regardless of the timing.
    /// </remarks>
    public static DemoHeader Header(string path) =>
        Headers.GetOrAdd(path, static p =>
        {
            byte[] header = new byte[DemoHeader.SizeBytes];
            using FileStream stream = File.OpenRead(p);
            stream.ReadExactly(header);

            return DemoHeader.Parse(header);
        });

    /// <summary>
    /// The entity schema the demo carries, parsed once per process.
    /// </summary>
    /// <param name="path">Path to a corpus demo.</param>
    /// <returns>The demo's schema.</returns>
    /// <exception cref="InvalidDataException">The demo carries no <c>dem_datatables</c>.</exception>
    /// <remarks>
    /// **Cached because it was measurably the most expensive thing the suite did.** Thirty-three
    /// call sites parsed the same eight schemas, each a bit-level walk of up to 1.4 MB, and the
    /// two heaviest test classes accounted for roughly 14 of the suite's 34 seconds. Mutation
    /// testing multiplies that by every mutant, which is most of why a full run reached 1h29m
    /// (<c>DECISIONS.md</c> D15 addendum).
    ///
    /// **Schemas are cached; the demo bytes deliberately are not.** The corpus is 305 MB, and
    /// Stryker runs several test hosts at once — holding every demo resident in each would trade
    /// a time problem for a memory one. A parsed schema is small, and re-reading bytes costs
    /// little once the file is in the OS page cache.
    ///
    /// Safe to share because <see cref="DemoSchema"/> is read-only once parsed. Nothing here
    /// caches an <see cref="Tf2DemoSalvage.Core.Schema.EntityDecoder"/> — that is stateful by
    /// design, since a delta update's class comes from the snapshot the entity entered on, so
    /// sharing one across tests would let one test's entities answer another's questions.
    /// </remarks>
    public static DemoSchema Schema(string path) => Schemas.GetOrAdd(path, static p =>
    {
        byte[] bytes = File.ReadAllBytes(p);
        ushort protocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol;

        foreach (DemoCommand command in
            DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type == DemoCommandType.DataTables)
            {
                return SendTableParser.Parse(command.Payload.Span, protocol);
            }
        }

        throw new InvalidDataException($"{p} carries no dem_datatables command.");
    });

    /// <summary>The demo's schema, or <c>null</c> when the demo does not carry a usable one.</summary>
    /// <param name="path">Path to a corpus demo.</param>
    /// <returns>The schema, or <c>null</c>.</returns>
    /// <remarks>
    /// **Not every demo has a schema, and that is a property of the demo rather than a defect.**
    /// A SourceTV recording on TF2's launch build truncates <c>dem_datatables</c> at exactly
    /// 65,536 bytes; the POV of the same session carries 85,063, which is how the truncation was
    /// identified as the writer's rather than the parser's. The file is otherwise intact and every
    /// other layer of it decodes.
    ///
    /// Tests that need entities use <see cref="FilesWithSchema"/> so those demos are excluded by
    /// their own property rather than by name. The truncation is asserted directly elsewhere, so
    /// skipping here hides nothing.
    /// </remarks>
    public static DemoSchema? TrySchema(string path)
    {
        try
        {
            return Schema(path);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Demos whose <c>dem_datatables</c> parses, and which can therefore decode entities.</summary>
    /// <returns>The subset of <see cref="Files"/> carrying a usable schema.</returns>
    public static IReadOnlyList<string> FilesWithSchema() =>
        [.. Files().Where(f => TrySchema(f) is not null)];

    /// <summary>Player rosters, keyed by demo path.</summary>
    private static readonly ConcurrentDictionary<string, IReadOnlyList<PlayerInfo>> Rosters =
        new(StringComparer.Ordinal);

    /// <summary>Everyone the demo's <c>userinfo</c> table named, in entity order.</summary>
    /// <param name="path">Path to a corpus demo.</param>
    /// <returns>The roster.</returns>
    /// <remarks>
    /// **The suite's critical path before this existed.** Building a roster means reading every
    /// packet in the demo, and four tests each did it for all eight demos — 6 to 12 seconds
    /// apiece, roughly 32 of the suite's 34 seconds. Tests within a class run sequentially in
    /// xUnit, so those four walks did not even overlap each other.
    ///
    /// Worth stating how that was found, because the first attempt at this was wrong: timing
    /// each test class separately suggested schema parsing dominated. It did not. Each of those
    /// runs carried about two seconds of host startup, and classes run in parallel, so the sum
    /// of per-class times says nothing about wall clock. Per-test durations from the detailed
    /// logger showed the real shape immediately.
    /// </remarks>
    public static IReadOnlyList<PlayerInfo> Players(string path) =>
        Rosters.GetOrAdd(path, static p =>
        {
            byte[] bytes = File.ReadAllBytes(p);

            // Seeded from the header: the protocol sizes the message type field, so a
            // protocol-15 demo yields no messages at all without it (RISKS B17).
            NetDecodeState state = new()
            {
                NetworkProtocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol,
            };

            Dictionary<int, PlayerInfo> byEntity = [];

            foreach (DemoCommand command in
                DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
            {
                if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
                {
                    continue;
                }

                foreach (INetMessage message in
                    NetMessageReader.Read(command.Payload.Span, state).Messages)
                {
                    // Creates and updates both, through the same builder the production scan
                    // uses - a helper that built rosters differently would be measuring
                    // something other than what the tool produces.
                    switch (message)
                    {
                        case CreateStringTableMessage { Name: RosterBuilder.TableName } table:
                            RosterBuilder.Apply(table.Entries, byEntity);
                            break;

                        case UpdateStringTableMessage update
                            when state.StringTableName(update.TableId) == RosterBuilder.TableName:
                            RosterBuilder.Apply(update.Entries, byEntity);
                            break;

                        default:
                            break;
                    }
                }
            }

            return [.. byEntity.Values.OrderBy(player => player.EntityIndex)];
        });

    /// <summary>How many snapshots <see cref="FirstSnapshots"/> keeps per demo.</summary>
    /// <remarks>
    /// A cap rather than the whole stream, because a demo carries tens of thousands of
    /// snapshots and the tests that need them are checking shape, not completeness. Callers
    /// must not silently want more: <see cref="FirstSnapshots"/> throws rather than returning a
    /// short list, so a test asking for 500 fails loudly instead of quietly measuring 400.
    /// </remarks>
    public const int CachedSnapshots = 400;

    /// <summary>One entity snapshot's header fields, without its body.</summary>
    /// <remarks>
    /// **The body is deliberately dropped.** <c>PacketEntitiesMessage.Body</c> is a
    /// <see cref="ReadOnlyMemory{T}"/> over the demo's bytes, so caching whole messages would
    /// pin all 305 MB of the corpus in memory for the life of the process — trading the time
    /// problem this cache solves for a worse memory one under Stryker's parallel hosts.
    /// </remarks>
    internal sealed record SnapshotSummary(
        int MaxEntries,
        bool IsDelta,
        int? DeltaFromTick,
        bool BaselineIndex,
        int UpdatedEntries,
        int LengthBits,
        bool UpdateBaseline,
        int ServerTick);

    private static readonly ConcurrentDictionary<string, IReadOnlyList<SnapshotSummary>> SnapshotCache =
        new(StringComparer.Ordinal);

    /// <summary>The demo's first entity snapshots, walked once per process.</summary>
    /// <param name="path">Path to a corpus demo.</param>
    /// <param name="count">How many are wanted. Must not exceed <see cref="CachedSnapshots"/>.</param>
    /// <returns>The snapshot headers, in stream order.</returns>
    /// <exception cref="ArgumentOutOfRangeException">More than the cache holds was asked for.</exception>
    public static IReadOnlyList<SnapshotSummary> FirstSnapshots(string path, int count)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, CachedSnapshots);

        IReadOnlyList<SnapshotSummary> all = SnapshotCache.GetOrAdd(path, static p =>
        {
            byte[] bytes = File.ReadAllBytes(p);
            NetDecodeState state = new()
            {
                NetworkProtocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol,
            };

            List<SnapshotSummary> snapshots = [];

            // The server's own clock, from net_Tick in the same packet. Deliberately not the
            // container command's tick: those are different counters offset by a constant, and
            // comparing them is what an earlier version of the delta test got wrong.
            int serverTick = 0;

            foreach (DemoCommand command in
                DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
            {
                if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
                {
                    continue;
                }

                foreach (INetMessage message in
                    NetMessageReader.Read(command.Payload.Span, state).Messages)
                {
                    if (message is NetTickMessage tick)
                    {
                        serverTick = tick.Tick;
                    }

                    if (message is not PacketEntitiesMessage snapshot)
                    {
                        continue;
                    }

                    snapshots.Add(new SnapshotSummary(
                        snapshot.MaxEntries,
                        snapshot.IsDelta,
                        snapshot.DeltaFromTick,
                        snapshot.BaselineIndex,
                        snapshot.UpdatedEntries,
                        snapshot.LengthBits,
                        snapshot.UpdateBaseline,
                        serverTick));

                    if (snapshots.Count == CachedSnapshots)
                    {
                        return snapshots;
                    }
                }
            }

            return snapshots;
        });

        return count >= all.Count ? all : [.. all.Take(count)];
    }

    /// <summary>One voice packet, with its body copied out of the demo.</summary>
    /// <param name="Client">Speaking client slot, as <c>svc_VoiceData</c> carries it.</param>
    /// <param name="Body">The codec payload, owned rather than a view over the demo.</param>
    internal sealed record VoicePacketSummary(int Client, byte[] Body);

    /// <summary>Every voice packet in a demo, and the codec the session declared.</summary>
    /// <param name="Codec">From <c>svc_VoiceInit</c>: <c>steam</c>, <c>vaudio_celt</c>, …</param>
    /// <param name="Packets">The packets, in stream order.</param>
    internal sealed record VoiceSummary(string? Codec, IReadOnlyList<VoicePacketSummary> Packets);

    /// <summary>
    /// Held as <see cref="Lazy{T}"/> rather than the value itself, which is the difference
    /// between caching and actually parsing once.
    /// </summary>
    /// <remarks>
    /// <c>ConcurrentDictionary.GetOrAdd</c> does not promise the factory runs a single time — it
    /// promises a single value is *published*. xUnit runs test classes in parallel, so four voice
    /// classes starting together all miss, all walk the same demo, and three of those walks are
    /// thrown away. Measured exactly that way: per-test times showed one class at 9 ms and three
    /// still at 39 s. <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/> makes the losers
    /// block on the winner instead of duplicating it.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, Lazy<VoiceSummary>> VoiceCache =
        new(StringComparer.Ordinal);

    /// <summary>The demo's voice packets, walked once per process.</summary>
    /// <param name="path">Path to a corpus demo.</param>
    /// <returns>The codec and every voice packet.</returns>
    /// <remarks>
    /// **Four test classes each walked every demo for this**, at roughly 40 seconds apiece
    /// against a local corpus — the Steam framing check, the Opus decode, the CELT/Speex decode
    /// and the CRC32 check all needed the same packets and each rebuilt them from scratch. A
    /// walk is a walk whether one test wants the result or four do.
    ///
    /// **Bodies are copied, unlike the demo bytes elsewhere in this file.** A
    /// <c>VoiceDataMessage.Body</c> is a <see cref="ReadOnlyMemory{T}"/> over the file, so
    /// holding the messages would pin the whole corpus — 1.4 GB locally — for the life of the
    /// process, which is the trade this file's other caches deliberately refuse. Voice payloads
    /// are small enough that copying them out escapes that: about a megabyte across the corpus,
    /// against gigabytes pinned.
    ///
    /// The codec is captured rather than assumed, because it decides which decoder the packets
    /// belong to and it is per-recording.
    /// </remarks>
    public static VoiceSummary Voice(string path) => VoiceCache.GetOrAdd(
        path,
        static p => new Lazy<VoiceSummary>(() => WalkVoice(p), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    /// <summary>Whether any demo present carries voice in the named codec.</summary>
    /// <param name="codec">Codec as <c>svc_VoiceInit</c> names it, e.g. <c>"steam"</c>.</param>
    /// <returns>True if at least one demo carries voice packets in that codec.</returns>
    /// <remarks>
    /// **The committed corpus and a developer's corpus do not carry the same codecs, and a test
    /// cannot tell the two situations apart without asking.** Measured 2026-08-12: the committed
    /// corpus is <c>vaudio_celt</c> and <c>vaudio_speex</c> only, while every <c>steam</c>-codec
    /// (Opus) packet in existence here lives in the git-ignored local corpus. So the five Steam
    /// voice tests had nothing to run against in CI and failed on their own "this proved nothing"
    /// guards — correctly, on the first CI run there ever was.
    ///
    /// Used with <c>Assert.SkipUnless</c> rather than an early <c>return</c>, and the difference
    /// is the entire point. A test that returns early passes having asserted nothing and is
    /// indistinguishable in the output from one that did real work; a skip names the missing
    /// codec in the run summary. The existing guards inside those tests keep their teeth for the
    /// case that actually matters — demos carrying the codec that nonetheless yield no packets,
    /// which is a decoder bug and still fails.
    /// </remarks>
    public static bool AnyDemoUses(string codec) =>
        Files().Any(path => string.Equals(Voice(path).Codec, codec, StringComparison.Ordinal));

    private static VoiceSummary WalkVoice(string p)
    {
        byte[] bytes = File.ReadAllBytes(p);
        NetDecodeState state = new()
        {
            NetworkProtocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol,
        };

        string? codec = null;
        List<VoicePacketSummary> packets = [];

        foreach (DemoCommand command in
            DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            foreach (INetMessage message in
                NetMessageReader.Read(command.Payload.Span, state).Messages)
            {
                if (message is VoiceInitMessage init)
                {
                    codec = init.Codec;
                }

                if (message is VoiceDataMessage voice && voice.BodyBits > 0)
                {
                    packets.Add(new VoicePacketSummary(voice.Client, voice.Body.ToArray()));
                }
            }
        }

        return new VoiceSummary(codec, packets);
    }
}
