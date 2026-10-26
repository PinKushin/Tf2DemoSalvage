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
/// **The counting draw is squeezed, not untested**: the same depth test in a viewport whose depth runs 0 to 0.01, so
/// something nearer than a hundredth of the proxy's depth hides the count as it hides the proxy.
///
/// **<see cref="Result"/> takes what the query last held.** *Read from published source* (the comment in
/// `CPixelVisibilityQuery::ResetOcclusionQueries`): "if the query isn't done yet, it will use the last queried value",
/// and a query never read answers below zero — which `GetFractionVisible` treats as failed, skipping the next draw.
/// *Interpolated*: that the engine's read does not flush; `shaderapidx9` is not published. A test target, which has
/// one frame and no next one, may ask to wait.
/// </remarks>
public sealed unsafe class OcclusionQueries : IDisposable
{
    /// <summary>A proxy's quad as two triangles.</summary>
    private const int CornersPerProxy = 6;

    private readonly Dictionary<int, Pair> _pairs = [];
    private Vector3[] _corners = [];
    private readonly WorldLineRenderer _shape;
    private ComPtr<ID3D11DepthStencilState> _tested;
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

        if (proxies.Count == 0)
        {
            return;
        }

        float* factor = stackalloc float[4] { 1f, 1f, 1f, 1f };

        // Culling is the one state no later pass sets for itself, so it is handed back as it was found.
        ComPtr<ID3D11RasterizerState> culling = default;

        context.RSGetState(ref culling);
        context.OMSetBlendState(_noColour, factor, 0xFFFFFFFF);
        context.OMSetDepthStencilState(_tested, 0);
        context.RSSetState(_bothSides);

        // `pRenderContext->DepthRange( 0.0f, 0.01f )` for the counting queries, `( 0.0f, 1.0f )` after.
        uint one = 1;
        Viewport whole = default;

        context.RSGetViewports(ref one, ref whole);

        Viewport squeezed = whole;

        squeezed.MinDepth = 0f;
        squeezed.MaxDepth = 0.01f;

        // **Every proxy's quad in one buffer, uploaded and bound once** (B597): each query draws its own six corners by
        // offset. Per query this was a fresh array, a vertex map, a camera map and six binds — 0.37 ms of the render
        // thread for 376 one-quad draws — and the GPU sees the same triangles under the same states either way.
        int corners = proxies.Count * CornersPerProxy;

        if (_corners.Length < corners)
        {
            _corners = new Vector3[corners];
        }

        for (int p = 0; p < proxies.Count; p++)
        {
            PixelVisibilityProxy proxy = proxies[p];
            int at = p * CornersPerProxy;

            _corners[at] = proxy.First;
            _corners[at + 1] = proxy.Second;
            _corners[at + 2] = proxy.Third;
            _corners[at + 3] = proxy.First;
            _corners[at + 4] = proxy.Third;
            _corners[at + 5] = proxy.Fourth;
        }

        _shape.BindTriangles(device, context, _corners.AsSpan(0, corners), viewProjection);

        for (int p = 0; p < proxies.Count; p++)
        {
            PixelVisibilityProxy proxy = proxies[p];

            if (!_pairs.TryGetValue(proxy.Key, out Pair? pair))
            {
                QueryDesc description = new() { Query = Query.Occlusion };

                pair = new Pair();
                SilkMarshal.ThrowHResult(device.CreateQuery(in description, ref pair.Possible));
                SilkMarshal.ThrowHResult(device.CreateQuery(in description, ref pair.Visible));
                _pairs[proxy.Key] = pair;
            }

            // What it holds now, before it is begun again and loses it.
            Poll(context, pair, wait: false);

            // `IssueCountingQuery`, then `IssueQuery`.
            context.RSSetViewports(1, in squeezed);
            Count(context, pair.Possible, p * CornersPerProxy);
            context.RSSetViewports(1, in whole);
            Count(context, pair.Visible, p * CornersPerProxy);
            pair.Begun = true;
        }

        context.RSSetState(culling);
        culling.Dispose();
    }

    /// <summary>What an entity's proxy last counted.</summary>
    /// <param name="context">The context, to read counts that are still on the device.</param>
    /// <param name="key">The entity.</param>
    /// <param name="wait">Ask until the device answers, where there is no later frame to ask in.</param>
    /// <returns>
    /// Pixels that passed the depth test and pixels on screen, as last read; null where neither query has answered
    /// yet — the engine's count below zero.
    /// </returns>
    public (int Pixels, int Possible)? Result(ComPtr<ID3D11DeviceContext> context, int key, bool wait = false)
    {
        if (!_pairs.TryGetValue(key, out Pair? pair))
        {
            return null;
        }

        Poll(context, pair, wait);

        return pair.VisibleCount < 0 || pair.PossibleCount < 0 ? null : (pair.VisibleCount, pair.PossibleCount);
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
        _tested.Dispose();
        _shape.Dispose();
    }

    private static void Count(ComPtr<ID3D11DeviceContext> context, ComPtr<ID3D11Query> query, int first)
    {
        context.Begin((ID3D11Asynchronous*)query.Handle);
        WorldLineRenderer.DrawRange(context, first, CornersPerProxy);
        context.End((ID3D11Asynchronous*)query.Handle);
    }

    /// <summary>Takes each query's count if the device has it; otherwise leaves the last one.</summary>
    private static void Poll(ComPtr<ID3D11DeviceContext> context, Pair pair, bool wait)
    {
        // A query never begun has nothing to give, and asking it is an invalid call.
        if (!pair.Begun)
        {
            return;
        }

        pair.VisibleCount = Read(context, pair.Visible, wait) ?? pair.VisibleCount;
        pair.PossibleCount = Read(context, pair.Possible, wait) ?? pair.PossibleCount;
    }

    /// <summary>One query's sample count, or null while the device has none: <c>GetData</c> answers S_FALSE until it does.</summary>
    private static int? Read(ComPtr<ID3D11DeviceContext> context, ComPtr<ID3D11Query> query, bool wait)
    {
        ulong samples = 0;
        int result;

        do
        {
            result = context.GetData((ID3D11Asynchronous*)query.Handle, &samples, sizeof(ulong), 0);
            SilkMarshal.ThrowHResult(result);
        }
        while (result != 0 && wait);

        return result == 0 ? (int)Math.Min(samples, int.MaxValue) : null;
    }

    /// <summary>A proxy's two queries and what each last answered; below zero until it has.</summary>
    private sealed class Pair
    {
        public ComPtr<ID3D11Query> Possible;
        public ComPtr<ID3D11Query> Visible;
        public int PossibleCount = -1;
        public int VisibleCount = -1;
        public bool Begun;
    }
}
