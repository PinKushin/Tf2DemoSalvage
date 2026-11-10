using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Render;

/// <summary><c>CViewRender::PerformScreenOverlay</c> — the local player's screen overlay, drawn over the finished frame.</summary>
internal sealed unsafe partial class WorldRenderer
{
    private readonly Dictionary<string, (ComPtr<ID3D11ShaderResourceView>[] Normal, ComPtr<ID3D11ShaderResourceView> Tint)> _overlayTextures =
        new(System.StringComparer.OrdinalIgnoreCase);

    private ComPtr<ID3D11VertexShader> _overlayVertex;
    private ComPtr<ID3D11Buffer> _overlayConstants;
    private ComPtr<ID3D11DepthStencilState> _overlayDepth;
    private ComPtr<ID3D11PixelShader> _overlayFadeShader;

    /// <summary>Draws one screen overlay over whatever the bound target holds.</summary>
    /// <param name="context">The context, with the frame's target and viewport bound.</param>
    /// <param name="overlay">The material the slot holds.</param>
    /// <param name="seconds">`curtime`, for its time-driven proxies.</param>
    /// <returns>Whether anything was drawn; false for an overlay this port draws as nothing.</returns>
    /// <remarks>
    /// **A <c>Refract</c> overlay needs the power-of-two frame buffer**: the engine copies the frame
    /// (<c>UpdateRefractTexture( x, y, w, h, true )</c>) and draws a screen rectangle with it — the same arithmetic as a
    /// model's refract batch, with no fog (the rectangle sits at depth zero, where Valve's range fog is zero). Blended as
    /// every Refract material is, by the normal map's alpha (<c>InitParamsRefract_DX9</c> sets
    /// <c>MATERIAL_VAR_TRANSLUCENT</c>); depth is not part of a screen overlay. **Any other overlay is
    /// <c>ViewDrawFade</c>'s** (viewrender.cpp:1242-1246) — <see cref="ScreenOverlayMaterial.Fade"/>.
    /// </remarks>
    /// <param name="underwater">
    /// Whether this is <c>DrawUnderwaterOverlay</c>'s slot, whose overlay that is not <c>Refract</c> is a plain full-screen
    /// rectangle under the material's own blend (`viewrender.cpp:1332-1338`, B660), where the player's slot draws
    /// <c>ViewDrawFade</c>.
    /// </param>
    public bool DrawScreenOverlay(
        ComPtr<ID3D11DeviceContext> context, ScreenOverlayMaterial overlay, double seconds, bool underwater = false)
    {
        System.ArgumentNullException.ThrowIfNull(overlay);

        MapTexture? flat = underwater ? overlay.Plain : overlay.Fade;

        if (overlay.Refract is null && flat is null)
        {
            return false;
        }

        EnsureOverlayPipeline();

        TextureTransform bump = TextureTransform.Identity;
        uint viewports = 1;
        Viewport viewport = default;

        context.RSGetViewports(ref viewports, ref viewport);

        int width = (int)viewport.Width;
        int height = (int)viewport.Height;

        // The frame copy's rectangle, `0, 0, sw - 1, sh - 1, sw, sh` (viewrender.cpp:1228-1240, :1318-1331). The fade keeps
        // it too: `ViewDrawFade`'s quad is engine.dll's, its texcoords not read (*interpolated*).
        (float Left, float Top, float Right, float Bottom) edges = ScreenRectangle.Edges(
            width, height, 0f, 0f, FrameCopySize - 1, FrameCopySize - 1, FrameCopySize, FrameCopySize);
        ComPtr<ID3D11BlendState> blend = _overlayBlend;

        if (overlay.Refract is { } refract)
        {
            if (!_overlayTextures.TryGetValue(overlay.Name, out (ComPtr<ID3D11ShaderResourceView>[] Normal, ComPtr<ID3D11ShaderResourceView> Tint) textures))
            {
                IEnumerable<MapTexture> frames = overlay.NormalFrames.Count > 0 ? overlay.NormalFrames : [refract.NormalMap];

                textures = (
                    [.. frames.Select(frame => Upload(_device, context, frame, srgb: false))],
                    refract.RefractTintTexture is { } tint ? Upload(_device, context, tint) : default);
                _overlayTextures[overlay.Name] = textures;
            }

            (float amount, (float Red, float Green, float Blue) tintColour, bump) = overlay.Bind(seconds);

            // `BindTexture( NORMALMAP, BUMPFRAME )`, BUMPFRAME the AnimatedTexture proxy's output.
            ComPtr<ID3D11ShaderResourceView> normal = textures.Normal[System.Math.Min(overlay.NormalFrameAt(seconds), textures.Normal.Length - 1)];

            CopyFrameForRefract(context);
            BindRefractModel(context, (refract with { RefractAmount = amount, RefractTint = tintColour, Fogged = false }, normal, textures.Tint));
        }
        else
        {
            // Keyed apart: one material can be both slots' (the fade's texture and the plain rectangle's are the same file,
            // but the error material has only the plain one).
            string key = underwater ? overlay.Name + PlainKey : overlay.Name;

            if (!_overlayTextures.TryGetValue(key, out (ComPtr<ID3D11ShaderResourceView>[] Normal, ComPtr<ID3D11ShaderResourceView> Tint) textures))
            {
                textures = ([Upload(_device, context, flat!.Value)], default);
                _overlayTextures[key] = textures;
            }

            ComPtr<ID3D11ShaderResourceView> fade = textures.Normal[0];

            context.PSSetShader(_overlayFadeShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
            context.PSSetShaderResources(0, 1, ref fade);

            if (underwater)
            {
                // `DrawScreenSpaceRectangle( pOverlayMat, 0, 0, w, h, 0, 0, 1, 1, 1, 1 )`: texel 0's centre at the first
                // pixel and texel 1's at the last, so the texture runs from about 0.5 to 1.5 and wraps, under the
                // material's `SetDefaultBlendingShadowState` (`BlendType_t`, BaseShader.h).
                edges = ScreenRectangle.Edges(width, height, 0f, 0f, 1f, 1f, 1, 1);
                blend = PlainBlend(flat!.Value);
            }
        }

        MappedSubresource mapped = default;

        SubmitCounters.Upload(); SilkMarshal.ThrowHResult(context.Map(_overlayConstants, 0, Map.WriteDiscard, 0, ref mapped));

        float* into = (float*)mapped.PData;

        (into[0], into[1], into[2], into[3]) = bump.Row0;
        (into[4], into[5], into[6], into[7]) = bump.Row1;
        (into[8], into[9], into[10], into[11]) = edges;

        context.Unmap(_overlayConstants, 0);

        float* blendFactor = stackalloc float[4] { 1f, 1f, 1f, 1f };

        context.IASetInputLayout(default(ComPtr<ID3D11InputLayout>));
        context.IASetPrimitiveTopology(Silk.NET.Core.Native.D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        context.VSSetShader(_overlayVertex, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.VSSetConstantBuffers(6, 1, ref _overlayConstants);
        context.RSSetState(_bothSides);
        context.OMSetDepthStencilState(_overlayDepth, 0);
        context.OMSetBlendState(blend, blendFactor, 0xFFFFFFFF);
        SubmitCounters.Draw(); context.Draw(3, 0);

        ComPtr<ID3D11ShaderResourceView> none = default;

        context.PSSetShaderResources(13, 1, ref none);
        ResetBlend(context);
        BindPipeline(context);

        return true;
    }

    /// <summary>The power-of-two frame copy's edge, which the refract rectangle counts its texels against.</summary>
    private const int FrameCopySize = 1024;

    /// <summary>The cache key suffix of a plain rectangle's texture.</summary>
    private const string PlainKey = "\nplain";

    /// <summary>
    /// <c>SetDefaultBlendingShadowState</c> for a plain overlay: <c>BT_BLENDADD</c> or <c>BT_ADD</c> when additive,
    /// <c>BT_BLEND</c> when translucent, else none. *Not carried:* an opaque draw's destination alpha, which the engine's
    /// shader may fill with depth; this one leaves it.
    /// </summary>
    private ComPtr<ID3D11BlendState> PlainBlend(MapTexture texture)
    {
        if (texture.IsAdditive)
        {
            return texture.IsTranslucent ? _overlayBlendAdd : _overlayAdd;
        }

        return texture.IsTranslucent ? _overlayBlend : _overlayOpaque;
    }

    /// <summary>
    /// <c>BT_BLEND</c>, the overlay's own: the world's is made with the map's textures, and an overlay can be drawn over a
    /// target that has loaded none.
    /// </summary>
    private ComPtr<ID3D11BlendState> _overlayBlend;
    private ComPtr<ID3D11BlendState> _overlayOpaque;
    private ComPtr<ID3D11BlendState> _overlayAdd;
    private ComPtr<ID3D11BlendState> _overlayBlendAdd;

    /// <summary>The overlay's vertex shader, constants, depth state and fade shader, made once.</summary>
    private void EnsureOverlayPipeline()
    {
        if (_overlayVertex.Handle is not null)
        {
            return;
        }

        using D3DCompiler compiler = D3DCompiler.GetApi();
        ComPtr<ID3D10Blob> bytecode = Compile(compiler, "VsScreenOverlay", "vs_5_0");

        SilkMarshal.ThrowHResult(_device.CreateVertexShader(
            bytecode.GetBufferPointer(), bytecode.GetBufferSize(), ref Unsafe.NullRef<ID3D11ClassLinkage>(), ref _overlayVertex));
        bytecode.Dispose();

        _overlayFadeShader = PixelShader(_device, compiler, "PsScreenFade");

        BufferDesc buffer = new()
        {
            ByteWidth = 12 * sizeof(float),
            Usage = Usage.Dynamic,
            BindFlags = (uint)BindFlag.ConstantBuffer,
            CPUAccessFlags = (uint)CpuAccessFlag.Write,
        };

        SilkMarshal.ThrowHResult(_device.CreateBuffer(in buffer, null, ref _overlayConstants));

        DepthStencilDesc depth = default;

        depth.DepthEnable = 0;
        SilkMarshal.ThrowHResult(_device.CreateDepthStencilState(in depth, ref _overlayDepth));

        BlendDesc translucent = BlendStates.Translucent;
        BlendDesc opaque = BlendStates.OpaqueColour;

        SilkMarshal.ThrowHResult(_device.CreateBlendState(in translucent, ref _overlayBlend));
        BlendDesc add = BlendStates.Additive;
        BlendDesc blendAdd = BlendStates.BlendAdd;

        SilkMarshal.ThrowHResult(_device.CreateBlendState(in opaque, ref _overlayOpaque));
        SilkMarshal.ThrowHResult(_device.CreateBlendState(in add, ref _overlayAdd));
        SilkMarshal.ThrowHResult(_device.CreateBlendState(in blendAdd, ref _overlayBlendAdd));
    }

    /// <summary>Drops the overlay textures with the map's: the cache is keyed by name, and the next map may ship its own
    /// material under a name this one used (its <c>$underwateroverlay</c>, from its pakfile).</summary>
    private void ReleaseOverlayTextures()
    {
        foreach ((ComPtr<ID3D11ShaderResourceView>[] normal, ComPtr<ID3D11ShaderResourceView> tint) in _overlayTextures.Values)
        {
            foreach (ComPtr<ID3D11ShaderResourceView> frame in normal)
            {
                frame.Dispose();
            }

            tint.Dispose();
        }

        _overlayTextures.Clear();
    }

    private void ReleaseScreenOverlays()
    {
        ReleaseOverlayTextures();
        _overlayVertex.Dispose();
        _overlayConstants.Dispose();
        _overlayDepth.Dispose();
        _overlayFadeShader.Dispose();
        _overlayBlend.Dispose();
        _overlayOpaque.Dispose();
        _overlayAdd.Dispose();
        _overlayBlendAdd.Dispose();
    }
}
