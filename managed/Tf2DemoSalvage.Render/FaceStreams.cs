using System;
using System.Collections.Generic;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Render;

/// <summary>
/// Each face's flex stream kept on the device, re-sent only when the scene wrote its floats again (B597).
/// </summary>
/// <remarks>
/// **An addition above Valve, with the same output.** `R_StudioFlexMeshGroup` (studiorender `0x1800124f0`, read in
/// disassembly) rebuilds a dynamic flex mesh per mesh group per draw, every draw, with no change test — so the engine pays
/// the upload whether or not the weights moved. Here a face array carries the stamp of its last write
/// (<c>ModelInstance.FlexWritten</c>); the same array with the same stamp binds the buffer it already has, and a new
/// stamp, a new array or the stamp 0 ("unknown") uploads. Keyed on the array, so a resting face shared by every
/// instance of a model goes up once, and a class change (a new array) gets a new buffer.
/// **Thread:** only the drawing thread calls this. The scene rewrites an array in place only after the previous frame's
/// draw has finished (the pose waits for it, finding 98 §9), and a buffer re-sent with <c>WRITE_DISCARD</c> is renamed
/// by the driver, so a draw the GPU has not run yet keeps what it was given.
/// **Ceiling (ponytail):** one buffer per face drawn in the last frame — players, corpses and resting faces, at most
/// about 650 KB each for a TF2 class model — released one frame after it was last drawn.
/// </remarks>
internal sealed unsafe class FaceStreams : IDisposable
{
    private sealed class Entry
    {
        public ComPtr<ID3D11Buffer> Buffer;
        public long Written;
        public bool Drawn;
    }

    private readonly Dictionary<float[], Entry> _faces = new(ReferenceEqualityComparer.Instance);
    private readonly List<float[]> _undrawn = [];

    /// <summary>Whether a stamp may skip an upload; a test turns it off for an A/B against uploading every draw.</summary>
    public bool Allowed { get; set; } = true;

    /// <summary>The buffer holding <paramref name="face"/>'s floats, uploaded if its stamp is new.</summary>
    /// <param name="device">Creates a face's buffer the first time it is drawn.</param>
    /// <param name="context">Maps it.</param>
    /// <param name="face">Six floats per vertex of the model's buffer.</param>
    /// <param name="written">When the scene wrote <paramref name="face"/>; 0 is unknown and always uploads.</param>
    /// <returns>The buffer, and the bytes uploaded — zero when the device already held this write.</returns>
    public (ComPtr<ID3D11Buffer> Buffer, long Uploaded) Bind(
        ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, float[] face, long written)
    {
        ArgumentNullException.ThrowIfNull(face);

        if (!_faces.TryGetValue(face, out Entry? entry))
        {
            BufferDesc description = new()
            {
                ByteWidth = (uint)(face.Length * sizeof(float)),
                Usage = Usage.Dynamic,
                BindFlags = (uint)BindFlag.VertexBuffer,
                CPUAccessFlags = (uint)CpuAccessFlag.Write,
            };

            entry = new Entry();
            SilkMarshal.ThrowHResult(device.CreateBuffer(in description, null, ref entry.Buffer));
            _faces[face] = entry;
        }
        else if (Allowed && written != 0 && entry.Written == written)
        {
            entry.Drawn = true;
            return (entry.Buffer, 0);
        }

        MappedSubresource mapped = default;
        SubmitCounters.Upload();
        SilkMarshal.ThrowHResult(context.Map(entry.Buffer, 0, Map.WriteDiscard, 0, ref mapped));

        long bytes = (long)face.Length * sizeof(float);

        fixed (float* from = face)
        {
            System.Buffer.MemoryCopy(from, mapped.PData, bytes, bytes);
        }

        context.Unmap(entry.Buffer, 0);
        entry.Written = written;
        entry.Drawn = true;

        return (entry.Buffer, bytes);
    }

    /// <summary>A frame boundary: releases every face not drawn since the last one.</summary>
    public void ReleaseUndrawn()
    {
        _undrawn.Clear();

        foreach ((float[] face, Entry entry) in _faces)
        {
            if (!entry.Drawn)
            {
                _undrawn.Add(face);
            }

            entry.Drawn = false;
        }

        foreach (float[] face in _undrawn)
        {
            _faces[face].Buffer.Dispose();
            _faces.Remove(face);
        }

        _undrawn.Clear();
    }

    /// <summary>Releases every buffer.</summary>
    public void Dispose()
    {
        foreach (Entry entry in _faces.Values)
        {
            entry.Buffer.Dispose();
        }

        _faces.Clear();
    }
}
