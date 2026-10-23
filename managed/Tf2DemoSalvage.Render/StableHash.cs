using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Tf2DemoSalvage.Render;

/// <summary>FNV-1a over bytes: the same value for the same content in any process — the frame-hash and queue-verifier
/// instrument's hash (B581).</summary>
/// <remarks>
/// **By content only, and not `HashCode`**: `HashCode` is seeded per process, so its values never match between two
/// runs. **Only types whose every byte is a field go through <see cref="Value{T}"/>**; a struct the compiler or the
/// runtime may pad hashes its padding, which is whatever was on the stack (finding 98 §9).
/// </remarks>
internal struct StableHash()
{
    public long Result { get; private set; } = unchecked((long)14695981039346656037UL);

    public void Bytes(ReadOnlySpan<byte> bytes)
    {
        ulong value = (ulong)Result;

        foreach (byte b in bytes)
        {
            value = (value ^ b) * 1099511628211UL;
        }

        Result = (long)value;
    }

    public void Value<T>(T value)
        where T : unmanaged =>
        Bytes(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in value)));

    public void Floats(float[]? values)
    {
        if (values is null)
        {
            Value(-1);

            return;
        }

        Bytes(MemoryMarshal.AsBytes(values.AsSpan()));
    }

    public void Text(string? text) =>
        Bytes(MemoryMarshal.AsBytes((text ?? "\0").AsSpan()));

    /// <summary>Adds a run list: its count, then each run's material, first vertex and vertex count; -1 for null.</summary>
    public void Batches(IReadOnlyList<WorldBatch>? batches)
    {
        if (batches is null)
        {
            Value(-1);

            return;
        }

        Value(batches.Count);

        for (int at = 0; at < batches.Count; at++)
        {
            // Field by field, never as a `ValueTuple`: that type is auto-layout, and its bytes are not its fields'
            // (StableHashTests, finding 98 §9).
            Value(batches[at].MaterialIndex);
            Value(batches[at].FirstVertex);
            Value(batches[at].VertexCount);
        }
    }

    /// <summary>One run list's hash on its own.</summary>
    public static long OfBatches(IReadOnlyList<WorldBatch>? batches)
    {
        StableHash hash = new();

        hash.Batches(batches);

        return hash.Result;
    }
}
