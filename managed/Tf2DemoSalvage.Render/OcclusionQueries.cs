using System;
using System.Collections.Generic;
using System.Numerics;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Render;

/// <summary>
/// The device half of <c>CPixelVisibilitySystem::EndView</c>: two occlusion queries a proxy, and their counts (B378, B521).
/// </summary>
/// <remarks>
/// *Read from published source*, <c>c_pixel_visibility.cpp:586-625</c>. Each proxy is drawn twice through
/// <c>engine/occlusionproxy</c> — the <c>Occlusion</c> shader with <c>$writez 0</c>, so no colour and no depth is left
/// behind — once as the counting query under <c>DepthRange( 0.0f, 0.01f )</c> and once against the scene.
///
/// **The counting draw has its depth test off rather than its range squeezed.** A range of a hundredth puts the proxy
/// in front of everything further than about a hundredth past the near plane, which is everything a view holds; the
/// test being off is the same count without a second viewport.
///
/// **<see cref="Result"/> waits for the device.** The engine reads whatever the query last returned and can be a
/// frame staler than that; a still has to be the same picture twice, so this asks until the count is in. It is asked
/// a frame after the draw, when it is.
/// </remarks>
public sealed unsafe class OcclusionQueries : IDisposable
{
    private readonly Dictionary<int, Pair> _pairs = [];
    private readonly Dictionary<int, (int Pixels, int Possible)> _results = [];
    private readonly List<int> _pending = [];
    private readonly Vector3[] _corners = new Vector3[6];
    private readonly WorldLineRenderer _shape;
    private ComPtr<ID3D11DepthStencilState> _tested;
    private ComPtr<ID3D11DepthStencilState> _untested;
    private ComPtr<ID3D11BlendState> _noColour;
    private ComPtr<ID3D11RasterizerState> _bothSides;

    private OcclusionQueries(WorldLineRenderer shape) => _shape = shape;

    /// <summary>Builds the pipeline and its states.</summary>
    /// <param name="device">The device.</param>
    /// <returns>The queries, with none issued.</returns>
    public static OcclusionQueries Create(ComPtr<ID3D11Device> device)
    {
        OcclusionQueries queries = new(WorldLineRenderer.Create(device));

        // The scene's own comparison for a pass that writes no depth (`Device3D.DepthState`).
        DepthStencilDesc tested = new()
        {
            DepthEnable = new Silk.NET.Core.Bool32(true),
            DepthWriteMask = DepthWriteMask.Zero,
            DepthFunc = ComparisonFunc.LessEqual,
        };

        SilkMarshal.ThrowHResult(device.CreateDepthStencilState(in tested, ref queries._tested));

        DepthStencilDesc untested = new() { DepthEnable = new Silk.NET.Core.Bool32(false), DepthWriteMask = DepthWriteMask.Zero };

        SilkMarshal.ThrowHResult(device.CreateDepthStencilState(in untested, ref queries._untested));

        BlendDesc noColour = default;
        noColour.RenderTarget[0].RenderTargetWriteMask = 0;

        SilkMarshal.ThrowHResult(device.CreateBlendState(in noColour, ref queries._noColour));

        RasterizerDesc bothSides = new()
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.None,
            DepthClipEnable = new Silk.NET.Core.Bool32(true),
        };

        SilkMarshal.ThrowHResult(device.CreateRasterizerState(in bothSides, ref queries._bothSides));

        return queries;
    }

    /// <summary>Draws each proxy inside its two queries, against whatever depth the bound target holds.</summary>
    /// <param name="device">The device.</param>
    /// <param name="context">The context; its depth, blend and rasteriser states are replaced and the caller restores its own.</param>
    /// <param name="proxies">The quads, from <c>PixelVisibility.EndView</c>.</param>
    /// <param name="viewProjection">The view they were built in, row major.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public void Issue(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        IReadOnlyList<PixelVisibilityProxy> proxies,
        float[] viewProjection)
    {
        ArgumentNullException.ThrowIfNull(proxies);
        ArgumentNullException.ThrowIfNull(viewProjection);

        // Last frame's counts first: a query begun again before it is read loses what it held.
        Collect(context);

        if (proxies.Count == 0)
        {
            return;
        }

        float* factor = stackalloc float[4] { 1f, 1f, 1f, 1f };

        // Culling is the one state no later pass sets for itself, so it is handed back as it was found.
        ComPtr<ID3D11RasterizerState> culling = default;

        context.RSGetState(ref culling);
        context.OMSetBlendState(_noColour, factor, 0xFFFFFFFF);
        context.RSSetState(_bothSides);

        foreach (PixelVisibilityProxy proxy in proxies)
        {
            if (!_pairs.TryGetValue(proxy.Key, out Pair pair))
            {
                QueryDesc description = new() { Query = Query.Occlusion };

                SilkMarshal.ThrowHResult(device.CreateQuery(in description, ref pair.Possible));
                SilkMarshal.ThrowHResult(device.CreateQuery(in description, ref pair.Visible));
                _pairs[proxy.Key] = pair;
            }

            _corners[0] = proxy.First;
            _corners[1] = proxy.Second;
            _corners[2] = proxy.Third;
            _corners[3] = proxy.First;
            _corners[4] = proxy.Third;
            _corners[5] = proxy.Fourth;

            // `IssueCountingQuery`, then `IssueQuery`.
            Count(device, context, pair.Possible, _untested, viewProjection);
            Count(device, context, pair.Visible, _tested, viewProjection);

            _pending.Add(proxy.Key);
        }

        context.RSSetState(culling);
        culling.Dispose();
    }

    /// <summary>What an entity's proxy last counted.</summary>
    /// <param name="context">The context, to read counts that are still on the device.</param>
    /// <param name="key">The entity.</param>
    /// <returns>Pixels that passed the depth test and pixels on screen; null where no query was ever issued for it.</returns>
    public (int Pixels, int Possible)? Result(ComPtr<ID3D11DeviceContext> context, int key)
    {
        Collect(context);

        return _results.TryGetValue(key, out (int Pixels, int Possible) counted) ? counted : null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (Pair pair in _pairs.Values)
        {
            pair.Possible.Dispose();
            pair.Visible.Dispose();
        }

        _pairs.Clear();
        _bothSides.Dispose();
        _noColour.Dispose();
        _untested.Dispose();
        _tested.Dispose();
        _shape.Dispose();
    }

    private void Count(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        ComPtr<ID3D11Query> query,
        ComPtr<ID3D11DepthStencilState> depth,
        float[] viewProjection)
    {
        context.OMSetDepthStencilState(depth, 0);
        context.Begin((ID3D11Asynchronous*)query.Handle);
        _shape.DrawTriangles(device, context, _corners, viewProjection);
        context.End((ID3D11Asynchronous*)query.Handle);
    }

    /// <summary>Reads every count still on the device.</summary>
    private void Collect(ComPtr<ID3D11DeviceContext> context)
    {
        foreach (int key in _pending)
        {
            Pair pair = _pairs[key];

            _results[key] = (Read(context, pair.Visible), Read(context, pair.Possible));
        }

        _pending.Clear();
    }

    /// <summary>One query's sample count, waited for: <c>GetData</c> answers S_FALSE until the device has it.</summary>
    private static int Read(ComPtr<ID3D11DeviceContext> context, ComPtr<ID3D11Query> query)
    {
        ulong samples = 0;
        int result;

        do
        {
            result = context.GetData((ID3D11Asynchronous*)query.Handle, &samples, sizeof(ulong), 0);
            SilkMarshal.ThrowHResult(result);
        }
        while (result != 0);

        return (int)Math.Min(samples, int.MaxValue);
    }

    private struct Pair
    {
        public ComPtr<ID3D11Query> Possible;
        public ComPtr<ID3D11Query> Visible;
    }
}
