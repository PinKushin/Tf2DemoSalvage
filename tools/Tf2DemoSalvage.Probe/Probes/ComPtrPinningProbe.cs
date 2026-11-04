using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Does an unpinned <c>ref ComPtr</c> field crash when a compacting collection moves its object (B706)?</summary>
/// <remarks>
/// **A crash is the answer, not a failure of the probe**: the process dies with `0xC0000005` and the runtime prints the stack.
/// Surviving prints a line, and a `field` run often survives, so read several against `stack`. The record:
/// `docs/measurements/2026-11-04-unpinned-comptr.md`.
///
/// **Set these in the environment first**, because the runtime reads them at start: `DOTNET_TC_CallCounting=0` and
/// `DOTNET_TieredPGO=0`, which keep tier 0, as in the first frames after a load. It resolves Silk.NET's own overloads: this
/// namespace is not the renderer's, so `PinnedComCalls` is not asked. `stack` is the control for `field`.
/// </remarks>
#pragma warning disable S6640 // The experiment is what unsafe code does here, and it runs only when asked for.
public sealed unsafe class ComPtrPinningProbe : IProbe
#pragma warning restore S6640
{
    /// <summary>An object holding the buffer as a field, as <c>WorldRenderer</c> holds <c>_camera</c>.</summary>
    private sealed class Holder
    {
        public ComPtr<ID3D11Buffer> Camera;
    }

    private const int PairsPerRead = 64;
    private const int GarbageArrays = 256;
    private const int GarbageBytes = 64 * 1024;

    private volatile Holder _current = new();
    private volatile bool _stop;
    private long _pairs;

    /// <inheritdoc/>
    public string Name => "comptr-pinning";

    /// <inheritdoc/>
    public string Summary =>
        "field|stack|pinned <seconds> warp|hardware [cores]: does a ref ComPtr field crash under a compacting GC? (B706) Set DOTNET_TC_CallCounting=0 DOTNET_TieredPGO=0 first.";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!OperatingSystem.IsWindows() || arguments.Count < 3 || arguments[0] is not ("field" or "stack" or "pinned"))
        {
            output.WriteLine(Summary);
            return;
        }

        string form = arguments[0];
        int seconds = int.Parse(arguments[1], CultureInfo.InvariantCulture);
        D3DDriverType driver = arguments[2] == "warp" ? D3DDriverType.Warp : D3DDriverType.Hardware;
        int cores = arguments.Count > 3 ? int.Parse(arguments[3], CultureInfo.InvariantCulture) : 2;

        using Process process = Process.GetCurrentProcess();
        process.ProcessorAffinity = (nint)((1L << cores) - 1);

        // Not disposed: that unloads `d3d11.dll` under WARP's threads (B402).
        D3D11 d3d = D3D11.GetApi(null);
        ComPtr<ID3D11Device> device = default;
        ComPtr<ID3D11DeviceContext> context = default;

        SilkMarshal.ThrowHResult(d3d.CreateDevice(
            default(ComPtr<IDXGIAdapter>), driver, 0, 0u, null, 0u, D3D11.SdkVersion, ref device, null, ref context));

        BufferDesc description = new() { ByteWidth = 64, BindFlags = (uint)BindFlag.ConstantBuffer, Usage = Usage.Default };
        ComPtr<ID3D11Buffer> buffer = default;

        SilkMarshal.ThrowHResult(device.CreateBuffer(in description, null, ref buffer));

        _current = new Holder { Camera = buffer };

        Thread draws = new(() => Draw(context, form)) { Name = "MatQueue", IsBackground = true };

        draws.Start();

        long collections = Collect(buffer, seconds);

        _stop = true;
        draws.Join();
        buffer.Dispose();
        context.Dispose();
        device.Dispose();

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"survived {form} {arguments[2]} on {cores} cores: {seconds} s, {collections} collections, {_pairs} pairs of calls"));
    }

    private void Draw(ComPtr<ID3D11DeviceContext> context, string form)
    {
        while (!_stop)
        {
            Holder holder = _current;

            for (int pair = 0; pair < PairsPerRead; pair++)
            {
                switch (form)
                {
                    case "field":
                        DrawField(context, holder);
                        break;
                    case "stack":
                        DrawStack(context, holder);
                        break;
                    default:
                        DrawPinned(context, holder);
                        break;
                }
            }

            Interlocked.Add(ref _pairs, PairsPerRead);
        }
    }

    // **Each form its own method with no loop, as the first harness had it.** Written inline in the loop above, `field` survived
    // 3 of 3; in this shape it crashed 2 of 4. Too few to say why. On-stack replacement optimising the loop is a reading, untested.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DrawField(ComPtr<ID3D11DeviceContext> context, Holder holder)
    {
        context.VSSetConstantBuffers(0, 1, ref holder.Camera);
        context.PSSetConstantBuffers(0, 1, ref holder.Camera);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DrawStack(ComPtr<ID3D11DeviceContext> context, Holder holder)
    {
        ComPtr<ID3D11Buffer> camera = holder.Camera;

        context.VSSetConstantBuffers(0, 1, ref camera);
        context.PSSetConstantBuffers(0, 1, ref camera);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DrawPinned(ComPtr<ID3D11DeviceContext> context, Holder holder)
    {
        fixed (ComPtr<ID3D11Buffer>* pinned = &holder.Camera)
        {
            context.Handle->VSSetConstantBuffers(0, 1, (ID3D11Buffer**)pinned);
            context.Handle->PSSetConstantBuffers(0, 1, (ID3D11Buffer**)pinned);
        }
    }

    /// <summary>Publishes a new holder inside 32 MB of garbage and collects, so its whole region dies and Aggressive decommits it, as after a load.</summary>
    private long Collect(ComPtr<ID3D11Buffer> buffer, int seconds)
    {
        Stopwatch clock = Stopwatch.StartNew();
        byte[]?[] keep = new byte[GarbageArrays][];
        long collections = 0;

        while (clock.Elapsed.TotalSeconds < seconds)
        {
            Fill(keep);
            _current = new Holder { Camera = buffer };
            Fill(keep);
            Array.Clear(keep);

#pragma warning disable S1215 // Moving the object is the experiment.
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
#pragma warning restore S1215
            collections++;
        }

        return collections;
    }

    private static void Fill(byte[]?[] keep)
    {
        for (int index = 0; index < keep.Length; index++)
        {
            keep[index] = new byte[GarbageBytes];
        }
    }
}
