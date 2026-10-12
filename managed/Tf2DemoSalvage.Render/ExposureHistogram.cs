using System;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Render;

/// <summary>The luminance histogram <c>CLuminanceHistogramSystem</c> keeps, read back from the frame (B514).</summary>
/// <remarks>
/// **Valve counts with occlusion queries** — one bin per frame, each answer read at least two frames later
/// (`viewpostprocess.cpp:551-613`), so its histogram is a rolling mix of the last sixteen-odd frames. This copies the
/// exposure region of the finished 3D frame into one of three staging textures and reads the oldest back without
/// waiting, so the counts are a whole frame three or more frames old. *Interpolated*: the same delay, not the same
/// per-bin staggering — a still or a slow pan gives identical counts either way.
///
/// **A copy in flight is never queued again** (<see cref="HistogramRing"/>, the engine's own rule for a query). Until
/// 2026-10-11 every frame overwrote the oldest copy whether or not it had been read, so the one the reader asked for
/// was always the one the device had just been told to redo: ten readings took 16 seconds at 300 frames a second and
/// 77 on WARP, and a `--shot`, which waits for ten, outlasted CI's three minutes.
///
/// The region is <c>mat_exposure_center_region_x/y</c>'s centre (`:449-453`); each pixel is read as LINEAR light — the
/// frame's bytes through the sRGB curve, as <c>screenspace_general</c> samples it — and binned by
/// <see cref="AutoExposure.Count"/>. ponytail: every fourth pixel each way; the histogram is a ratio of counts.
/// </remarks>
public sealed unsafe class ExposureHistogram : IDisposable
{
    private const int Ring = 3;

    private const int Stride = 4;

    private static readonly float[] Linear = BuildLinear();

    private readonly ComPtr<ID3D11Texture2D>[] _staging = new ComPtr<ID3D11Texture2D>[Ring];

    private (int Width, int Height) _region;

    private readonly HistogramRing _ring = new(Ring);

    /// <summary>Copies this frame's exposure region for a later read.</summary>
    /// <param name="device">The device.</param>
    /// <param name="context">The context.</param>
    /// <param name="frame">The back buffer, B8G8R8A8.</param>
    /// <param name="width">Its width.</param>
    /// <param name="height">Its height.</param>
    public void Capture(ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, ComPtr<ID3D11Texture2D> frame, int width, int height)
    {
        _ring.BeginFrame();

        int skipX = (int)(width * 0.5f * (1f - AutoExposure.CentreRegionX));
        int skipY = (int)(height * 0.5f * (1f - AutoExposure.CentreRegionY));
        (int w, int h) region = (width - (2 * skipX), height - (2 * skipY));

        if (region.w <= 0 || region.h <= 0)
        {
            return;
        }

        if (_region != region)
        {
            Release();
            _region = region;

            Texture2DDesc description = new()
            {
                Width = (uint)region.w,
                Height = (uint)region.h,
                MipLevels = 1,
                ArraySize = 1,
                Format = Silk.NET.DXGI.Format.FormatB8G8R8A8Unorm,
                SampleDesc = new Silk.NET.DXGI.SampleDesc(1, 0),
                Usage = Usage.Staging,
                CPUAccessFlags = (uint)CpuAccessFlag.Read,
            };

            for (int i = 0; i < Ring; i++)
            {
                SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, null, ref _staging[i]));
            }
        }

        // Every copy still waiting to be read: this frame is not measured, as an entry in flight issues no query.
        if (_ring.SlotToQueue() is not { } slot)
        {
            return;
        }

        Box box = new((uint)skipX, (uint)skipY, 0, (uint)(skipX + region.w), (uint)(skipY + region.h), 1);
        context.CopySubresourceRegion(_staging[slot], 0, 0, 0, 0, frame, 0, in box);
        _ring.Queued(slot);
    }

    /// <summary>Counts the oldest copy the GPU has finished, if any.</summary>
    /// <param name="context">The context.</param>
    /// <param name="counts">Sixteen counts, overwritten.</param>
    /// <returns>Whether a histogram was read.</returns>
    public bool TryRead(ComPtr<ID3D11DeviceContext> context, Span<int> counts)
    {
        // `cur_query_frame > m_frame_queued + 2`, oldest first.
        if (_ring.SlotToRead() is not { } slot)
        {
            return false;
        }

        MappedSubresource mapped = default;

        // "-1=query not finished. wait until next time": the copy stays in flight and is asked for again.
        if (context.Map(_staging[slot], 0, Map.Read, (uint)MapFlag.DONotWait, ref mapped) < 0)
        {
            return false;
        }

        try
        {
            counts.Clear();

            for (int y = 0; y < _region.Height; y += Stride)
            {
                byte* row = (byte*)mapped.PData + ((uint)y * mapped.RowPitch);

                for (int x = 0; x < _region.Width; x += Stride)
                {
                    byte* pixel = row + (x * 4);

                    // B8G8R8A8: blue first. `luminance_compare_ps2x.fxc`'s weights.
                    AutoExposure.Count(counts, (0.2125f * Linear[pixel[2]]) + (0.7154f * Linear[pixel[1]]) + (0.0721f * Linear[pixel[0]]));
                }
            }
        }
        finally
        {
            context.Unmap(_staging[slot], 0);
        }

        _ring.Read(slot);
        return true;
    }

    private static float[] BuildLinear()
    {
        float[] table = new float[256];

        for (int i = 0; i < 256; i++)
        {
            float c = i / 255f;
            table[i] = c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        }

        return table;
    }

    private void Release()
    {
        for (int i = 0; i < Ring; i++)
        {
            _staging[i].Dispose();
            _staging[i] = default;
        }

        _ring.Reset();
    }

    /// <inheritdoc/>
    public void Dispose() => Release();
}
