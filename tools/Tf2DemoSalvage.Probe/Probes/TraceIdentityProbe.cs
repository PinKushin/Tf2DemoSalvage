using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>A hash of every field of many seeded cube traces through one map, to compare two builds of the trace.</summary>
/// <remarks>Written to show the three-extent trace leaves every cube caller's answer bit for bit as it was.</remarks>
public sealed class TraceIdentityProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "trace-identity";

    /// <inheritdoc/>
    public string Summary => "SHA-256 of seeded cube traces (world, terrain, submodels): trace-identity <bsp path> [count]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1)
        {
            output.WriteLine(Summary);
            return;
        }

        byte[] bytes = File.ReadAllBytes(arguments[0]);
        int count = arguments.Count > 1 ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : 20000;
        MapLevel level = MapLevel.Read(bytes, NullLogger.Instance);
        BspLeafTree tree = level.Leaves ?? throw new InvalidOperationException("the map has no tree");
        IReadOnlyList<BspModel> models = BspModels.Read(bytes);
        Lcg random = new();
        float[] sizes = [0f, 6f, 16f, 24f];
        int hits = 0;

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        for (int index = 0; index < count; index++)
        {
            (float X, float Y, float Z) from = (Coordinate(random), Coordinate(random), Coordinate(random) / 4f);
            (float X, float Y, float Z) to = (from.X + Delta(random), from.Y + Delta(random), from.Z + Delta(random));
            float size = sizes[index % sizes.Length];

            Add(hash, level.Trace(from, to, size), ref hits);
            Add(hash, level.TraceBrushOnly(from, to, size, BspLeafTree.MaskSolid | 0x10000), ref hits);

            BspModel model = models[1 + (index % (models.Count - 1))];
            Add(hash, tree.Trace(from.X, from.Y, from.Z, to.X, to.Y, to.Z, size, model.HeadNode), ref hits);

            // The player hull through an unturned brush entity standing off its compiled origin — MovementWorld's path.
            SolidBrush brush = new(model.HeadNode, new Vector3(8f, -8f, 0f), index);
            Add(
                hash,
                level.TraceHull(from, to, (-24f, -24f, 0f), (24f, 24f, 82f), BspLeafTree.MaskPlayerSolid, [brush])
                    ?? default,
                ref hits);
        }

        output.WriteLine($"{count} x 4 traces, {hits} stopped, sha256 {Convert.ToHexString(hash.GetHashAndReset())}");
    }

    private static float Coordinate(Lcg random) => (random.Next() * 8192f) - 4096f;

    private static float Delta(Lcg random) => (random.Next() * 1024f) - 512f;

    /// <summary>A fixed sequence, the same in both builds; not randomness anyone relies on.</summary>
    private sealed class Lcg
    {
        private uint _state = 1;

        public float Next()
        {
            _state = (_state * 1664525u) + 1013904223u;
            return (_state >> 8) / 16777216f;
        }
    }

    private static void Add(IncrementalHash hash, BspTrace trace, ref int hits)
    {
        if (trace.Fraction < 1f)
        {
            hits++;
        }

        hash.AppendData(BitConverter.GetBytes(trace.Fraction));
        hash.AppendData(BitConverter.GetBytes(trace.Texinfo));
        hash.AppendData(BitConverter.GetBytes(trace.Normal.X));
        hash.AppendData(BitConverter.GetBytes(trace.Normal.Y));
        hash.AppendData(BitConverter.GetBytes(trace.Normal.Z));
        hash.AppendData(BitConverter.GetBytes(trace.Distance));
        hash.AppendData(BitConverter.GetBytes(trace.AllSolid));
        hash.AppendData(BitConverter.GetBytes(trace.DisplacementTexdata));
        hash.AppendData(BitConverter.GetBytes(trace.SurfaceProp2));
        hash.AppendData(BitConverter.GetBytes(trace.BrushEntity));
        hash.AppendData(BitConverter.GetBytes(trace.StartSolid));
    }
}
