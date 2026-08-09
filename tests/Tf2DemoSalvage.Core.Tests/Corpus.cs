using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// Locates the reference demos, shared by every test that needs real files.
/// </summary>
internal static class Corpus
{
    /// <summary>Anything smaller than this is a Git LFS pointer stub, not a demo.</summary>
    public const int SmallestPlausibleDemo = 4096;

    /// <summary>Every usable demo in the corpus, in a stable order.</summary>
    public static IReadOnlyList<string> Files()
    {
        string? directory = Directory();
        if (directory is null)
        {
            return [];
        }

        return
        [
            .. System.IO.Directory
                .EnumerateFiles(directory, "*.dem")
                .Where(p => new FileInfo(p).Length >= SmallestPlausibleDemo)
                .OrderBy(p => p, StringComparer.Ordinal)
        ];
    }

    /// <summary>
    /// Walks up from the test binary looking for the corpus, rather than hard-coding a
    /// relative depth that breaks whenever the output path changes.
    /// </summary>
    public static string? Directory()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "tools", "corpus", "demos");
            if (System.IO.Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    /// <summary>Parsed schemas, keyed by demo path.</summary>
    private static readonly ConcurrentDictionary<string, DemoSchema> Schemas =
        new(StringComparer.Ordinal);

    /// <summary>Parsed headers, keyed by demo path.</summary>
    private static readonly ConcurrentDictionary<string, DemoHeader> Headers =
        new(StringComparer.Ordinal);

    /// <summary>The demo's header.</summary>
    /// <param name="path">Path to a corpus demo.</param>
    /// <returns>The parsed header.</returns>
    public static DemoHeader Header(string path) =>
        Headers.GetOrAdd(path, static p => DemoHeader.Parse(File.ReadAllBytes(p)));

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
                    if (message is not CreateStringTableMessage { Name: "userinfo" } table)
                    {
                        continue;
                    }

                    foreach (StringTableEntry entry in table.Entries)
                    {
                        if (entry.UserData.Count >= PlayerInfo.RecordBytes &&
                            int.TryParse(
                                entry.Text, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                out int entityIndex))
                        {
                            byEntity[entityIndex] = PlayerInfo.Parse([.. entry.UserData], entityIndex);
                        }
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
}
