using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Render;

/// <summary><c>CViewRender::PerformScreenOverlay</c> — the local player's screen overlay, drawn over the finished frame.</summary>
internal sealed unsafe partial class WorldRenderer
{
    private readonly Dictionary<string, (ComPtr<ID3D11ShaderResourceView> Normal, ComPtr<ID3D11ShaderResourceView> Tint)> _overlayTextures =
        new(System.StringComparer.OrdinalIgnoreCase);

    private ComPtr<ID3D11VertexShader> _overlayVertex;
    private ComPtr<ID3D11Buffer> _overlayConstants;
    private ComPtr<ID3D11DepthStencilState> _overlayDepth;

    /// <summary>Draws one screen overlay over whatever the bound target holds.</summary>
    /// <param name="context">The context, with the frame's target and viewport bound.</param>
    /// <param name="overlay">The material the slot holds.</param>
    /// <param name="seconds">`curtime`, for its time-driven proxies.</param>
    /// <returns>Whether anything was drawn; false for an overlay this port draws as nothing.</returns>
    /// <remarks>
    /// **Every TF2 overlay this draws is a <c>Refract</c> material**, which needs the power-of-two frame buffer: the
    /// engine copies the frame (<c>UpdateRefractTexture( x, y, w, h, true )</c>) and draws a screen rectangle with it —
    /// the same arithmetic as a model's refract batch, with no fog (the rectangle sits at depth zero, where Valve's
    /// range fog is zero). Blended as every Refract material is, by the normal map's alpha
    /// (<c>InitParamsRefract_DX9</c> sets <c>MATERIAL_VAR_TRANSLUCENT</c>); depth is not part of a screen overlay.
    /// </remarks>
    public bool DrawScreenOverlay(ComPtr<ID3D11DeviceContext> context, ScreenOverlayMaterial overlay, double seconds)
    {
        System.ArgumentNullException.ThrowIfNull(overlay);

        if (overlay.Refract is not { } refract)
        {
            return false;
        }

        if (!_overlayTextures.TryGetValue(overlay.Name, out (ComPtr<ID3D11ShaderResourceView> Normal, ComPtr<ID3D11ShaderResourceView> Tint) textures))
        {
            textures = (
                Upload(_device, context, refract.NormalMap, srgb: false),
                refract.RefractTintTexture is { } tint ? Upload(_device, context, tint) : default);
            _overlayTextures[overlay.Name] = textures;
        }

        if (_overlayVertex.Handle is null)
        {
            using D3DCompiler compiler = D3DCompiler.GetApi();
            ComPtr<ID3D10Blob> bytecode = Compile(compiler, "VsScreenOverlay", "vs_5_0");

            SilkMarshal.ThrowHResult(_device.CreateVertexShader(
                bytecode.GetBufferPointer(), bytecode.GetBufferSize(), ref Unsafe.NullRef<ID3D11ClassLinkage>(), ref _overlayVertex));
            bytecode.Dispose();

            BufferDesc buffer = new()
            {
                ByteWidth = 8 * sizeof(float),
                Usage = Usage.Dynamic,
                BindFlags = (uint)BindFlag.ConstantBuffer,
                CPUAccessFlags = (uint)CpuAccessFlag.Write,
            };

            SilkMarshal.ThrowHResult(_device.CreateBuffer(in buffer, null, ref _overlayConstants));

            DepthStencilDesc depth = default;

            depth.DepthEnable = 0;
            SilkMarshal.ThrowHResult(_device.CreateDepthStencilState(in depth, ref _overlayDepth));
        }

        (float amount, (float Red, float Green, float Blue) tintColour, TextureTransform bump) = overlay.Bind(seconds);

        CopyFrameForRefract(context);
        BindRefractModel(context, (refract with { RefractAmount = amount, RefractTint = tintColour, Fogged = false }, textures.Normal, textures.Tint));

        MappedSubresource mapped = default;

        SilkMarshal.ThrowHResult(context.Map(_overlayConstants, 0, Map.WriteDiscard, 0, ref mapped));

        float* into = (float*)mapped.PData;

        (into[0], into[1], into[2], into[3]) = bump.Row0;
        (into[4], into[5], into[6], into[7]) = bump.Row1;

        context.Unmap(_overlayConstants, 0);

        float* blendFactor = stackalloc float[4] { 1f, 1f, 1f, 1f };

        context.IASetInputLayout(default(ComPtr<ID3D11InputLayout>));
        context.IASetPrimitiveTopology(Silk.NET.Core.Native.D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        context.VSSetShader(_overlayVertex, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.VSSetConstantBuffers(6, 1, ref _overlayConstants);
        context.RSSetState(_bothSides);
        context.OMSetDepthStencilState(_overlayDepth, 0);
        context.OMSetBlendState(_alphaBlend, blendFactor, 0xFFFFFFFF);
        context.Draw(3, 0);

        ComPtr<ID3D11ShaderResourceView> none = default;

        context.PSSetShaderResources(13, 1, ref none);
        ResetBlend(context);
        BindPipeline(context);

        return true;
    }

    /// <summary>Releases the overlay pass's own resources.</summary>
    private void ReleaseScreenOverlays()
    {
        foreach ((ComPtr<ID3D11ShaderResourceView> normal, ComPtr<ID3D11ShaderResourceView> tint) in _overlayTextures.Values)
        {
            normal.Dispose();
            tint.Dispose();
        }

        _overlayTextures.Clear();
        _overlayVertex.Dispose();
        _overlayConstants.Dispose();
        _overlayDepth.Dispose();
    }
}
