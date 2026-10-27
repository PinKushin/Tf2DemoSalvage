using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// A hash of everything <see cref="DetailSprites.Build"/> emits for a fixed sweep of views over one map's real detail
/// lump, so two builds of it can be compared for byte identity without a viewer.
/// </summary>
/// <remarks>
/// **An identity instrument, not a timer.** Run it on the commit before a change and on the change; equal hashes mean
/// every corner (all eleven floats, by bit pattern), every <see cref="DetailSprites.Frame"/> field and every leaf lane's
/// <c>Take</c> range agreed on every view. The views are a deterministic scatter about the lump's own props, so each
/// one stands among detail rather than in a void, at <c>cl_detaildist 1200</c> / <c>cl_detailfade 400</c>.
///
/// **Its control** is the second line: the views must build quads (a hash of nothing agrees with everything), and the
/// third reports what the sort's identity rests on — whether the lump's leaf runs ascend, and how many emitted quads
/// tie with another of their leaf and lane on the exact squared distance, the only case an unstable sort may order
/// two ways.
///
/// <code>
///   detail-hash cp_process_f12 [views]
/// </code>
/// </remarks>
public sealed class DetailBuildHashProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "detail-hash";

    /// <inheritdoc/>
    public string Summary => "hash of every detail sprite quad over a view sweep, for identity: detail-hash <map> [views]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1)
        {
            output.WriteLine("detail-hash <map> [views]");
            return;
        }

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).Find(arguments[0]) is not { } path)
        {
            output.WriteLine($"No map named '{arguments[0]}'.");
            return;
        }

        int views = arguments.Count > 1 ? int.Parse(arguments[1], System.Globalization.CultureInfo.InvariantCulture) : 2000;

        (_, IReadOnlyList<BspDetailSprite> sprites, IReadOnlyList<BspDetailProp> objects) =
            BspDetailProps.Read(File.ReadAllBytes(path));

        if (objects.Count == 0)
        {
            output.WriteLine($"{Path.GetFileName(path)}: no detail props.");
            return;
        }

        DetailReach reach = new(objects, sprites);
        DetailFade fade = DetailFade.For(1200f, 400f);
        List<DetailSpriteVertex> corners = [];
        DetailSpriteLeaves leaves = new();
        SortedSet<int> leafIds = [];

        int descending = 0;
        int previous = int.MinValue;

        for (int run = 0; run < reach.Runs; run++)
        {
            int leaf = objects[reach.Range(run).Start].Leaf;
            descending += leaf <= previous ? 1 : 0;
            previous = leaf;
            leafIds.Add(leaf);
        }

        ulong hash = 14695981039346656037UL;
        ulong lanes = hash;
        List<string> quadKeys = [];
        long quads = 0;
        long ties = 0;
        Dictionary<(int Leaf, bool Fast, float Squared), int> seen = [];

        for (int view = 0; view < views; view++)
        {
            BspDetailProp near = objects[(int)((view * 7919L) % objects.Count)];
            (float X, float Y, float Z) eye = (
                near.Origin.X + (float)((Fraction(view * 0.618034) - 0.5) * 1200),
                near.Origin.Y + (float)((Fraction(view * 0.754878) - 0.5) * 1200),
                near.Origin.Z + 64f);
            double yaw = Fraction(view * 0.414214) * Math.Tau;
            double pitch = (Fraction(view * 0.569840) - 0.5) * 1.0;
            (float X, float Y, float Z) forward = (
                (float)(Math.Cos(yaw) * Math.Cos(pitch)), (float)(Math.Sin(yaw) * Math.Cos(pitch)), (float)-Math.Sin(pitch));

            corners.Clear();
            DetailSprites.Frame frame = DetailSprites.Build(objects, sprites, eye, fade, corners, leaves, forward, reach: reach);

            hash = Mix(hash, frame.Built);
            hash = Mix(hash, frame.Faded);
            hash = Mix(hash, frame.Aligned);
            hash = Mix(hash, frame.Fast);
            quads += frame.Built;

            foreach (DetailSpriteVertex corner in corners)
            {
                hash = Mix(hash, BitConverter.SingleToInt32Bits(corner.X));
                hash = Mix(hash, BitConverter.SingleToInt32Bits(corner.Y));
                hash = Mix(hash, BitConverter.SingleToInt32Bits(corner.Z));
                hash = Mix(hash, BitConverter.SingleToInt32Bits(corner.U));
                hash = Mix(hash, BitConverter.SingleToInt32Bits(corner.V));
                hash = Mix(hash, BitConverter.SingleToInt32Bits(corner.Red));
                hash = Mix(hash, BitConverter.SingleToInt32Bits(corner.Green));
                hash = Mix(hash, BitConverter.SingleToInt32Bits(corner.Blue));
                hash = Mix(hash, BitConverter.SingleToInt32Bits(corner.Alpha));
                hash = Mix(hash, BitConverter.SingleToInt32Bits(corner.NextU));
                hash = Mix(hash, BitConverter.SingleToInt32Bits(corner.NextV));
            }

            leaves.Begin();

            foreach (int leaf in leafIds)
            {
                foreach (bool fast in (ReadOnlySpan<bool>)[true, false])
                {
                    (int first, int count) = leaves.Take(leaf, null, fast);
                    hash = Mix(hash, first);
                    hash = Mix(hash, count);
                    lanes = Mix(lanes, leaf);
                    lanes = Mix(lanes, fast ? 1 : 0);
                    lanes = Mix(lanes, count);
                    lanes = MixLane(lanes, corners, first, count, quadKeys);
                }
            }

            ties += Ties(objects, sprites, eye, fade, reach, seen);
        }

        output.WriteLine($"{Path.GetFileName(path)}: {objects.Count} props, {reach.Runs} runs, {views} views");
        output.WriteLine($"quads {quads} (control: must be > 0)");
        output.WriteLine($"runs whose leaf does not ascend {descending}; exact (leaf, lane, squared) ties {ties}");
        output.WriteLine($"hash {hash:x16} (order within a lane included)");
        output.WriteLine($"lanes {lanes:x16} (each leaf lane's quads as a set: blind to order within a lane)");
    }

    /// <summary>Mixes one leaf lane's quads after sorting them by their bits, so only WHICH quads it holds counts.</summary>
    private static ulong MixLane(ulong hash, List<DetailSpriteVertex> corners, int first, int count, List<string> keys)
    {
        keys.Clear();

        for (int quad = first; quad < first + count; quad++)
        {
            System.Text.StringBuilder key = new();

            for (int at = quad * DetailSprites.CornersPerQuad; at < (quad + 1) * DetailSprites.CornersPerQuad; at++)
            {
                DetailSpriteVertex c = corners[at];
                key.Append(System.Globalization.CultureInfo.InvariantCulture,
                    $"{BitConverter.SingleToInt32Bits(c.X)},{BitConverter.SingleToInt32Bits(c.Y)},{BitConverter.SingleToInt32Bits(c.Z)},{BitConverter.SingleToInt32Bits(c.U)},{BitConverter.SingleToInt32Bits(c.V)},{BitConverter.SingleToInt32Bits(c.Red)},{BitConverter.SingleToInt32Bits(c.Green)},{BitConverter.SingleToInt32Bits(c.Blue)},{BitConverter.SingleToInt32Bits(c.Alpha)};");
            }

            keys.Add(key.ToString());
        }

        keys.Sort(StringComparer.Ordinal);

        foreach (string key in keys)
        {
            foreach (char ch in key)
            {
                hash = Mix(hash, ch);
            }
        }

        return hash;
    }

    /// <summary>Counts props in range sharing a leaf, a lane and an exact squared distance with another.</summary>
    /// <remarks>
    /// **A census of the DATA, recomputed by a second route — not the values the sort used.** An upper bound: it counts
    /// fast members of culled groups and faded props, which the build leaves out.
    /// </remarks>
    private static long Ties(
        IReadOnlyList<BspDetailProp> objects,
        IReadOnlyList<BspDetailSprite> sprites,
        (float X, float Y, float Z) eye,
        DetailFade fade,
        DetailReach reach,
        Dictionary<(int Leaf, bool Fast, float Squared), int> seen)
    {
        seen.Clear();
        long ties = 0;

        for (int run = 0; run < reach.Runs; run++)
        {
            if (!reach.Near(run, eye, fade.Radius))
            {
                continue;
            }

            (int first, int end) = reach.Range(run);

            for (int index = first; index < end; index++)
            {
                BspDetailProp prop = objects[index];

                if (prop.Type != DetailPropType.Sprite || prop.DetailModel < 0 || prop.DetailModel >= sprites.Count)
                {
                    continue;
                }

                bool fast = DetailSprites.IsFast(prop);
                (float X, float Y, float Z) at = fast ? DetailSprites.Anchor(prop, sprites[prop.DetailModel]) : prop.Origin;
                float x = at.X - eye.X;
                float y = at.Y - eye.Y;
                float z = at.Z - eye.Z;
                float squared = (x * x) + (y * y) + (z * z);

                if (squared > fade.MaximumSquared)
                {
                    continue;
                }

                (int, bool, float) key = (prop.Leaf, fast, squared);
                int count = seen.GetValueOrDefault(key);
                ties += count > 0 ? 1 : 0;
                seen[key] = count + 1;
            }
        }

        return ties;
    }

    private static ulong Mix(ulong hash, int value)
    {
        for (int shift = 0; shift < 32; shift += 8)
        {
            hash ^= (byte)(value >> shift);
            hash *= 1099511628211UL;
        }

        return hash;
    }

    private static double Fraction(double value) => value - Math.Floor(value);
}
