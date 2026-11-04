using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Render;

/// <summary>The Direct3D 11 calls a <c>ref</c> field reaches, pinned for the call (B706).</summary>
/// <remarks>
/// **Silk.NET's `ref ComPtr&lt;T&gt;` overloads do not pin.** Each passes `GetAddressOf()`, which is
/// `(T**)Unsafe.AsPointer(ref Unsafe.AsRef(in this))` (Silk.NET 2.23.0, `ComPtr`1.cs`), so `ref _camera` hands native code the
/// address of a field of a heap object with nothing holding that object still. A compacting collection on any other thread
/// then leaves the native call reading or writing where the object used to be: a set reads a rubbish handle, a create writes
/// the new one over whatever lives there now and leaves the field empty. B706 holds the crash and the experiment.
///
/// **Why these overloads catch every site with no site changed**: C# looks for extension methods from the innermost enclosing
/// namespace outward, and `Tf2DemoSalvage.Render` is inside the file-level `using Silk.NET.Direct3D11`, so this class is
/// asked first. A call that matches nothing here falls through to Silk.NET silently, which is what
/// `UnpinnedComPtrConformanceTests` is for: it reads the IL and refuses a heap address reaching Silk.NET.
///
/// A `ref local` is on the stack and was never at risk, so only what a field reaches is here. `OMSetRenderTarget` takes its
/// view by value, in place of `GetAddressOf()`, which no overload can intercept and the conformance test refuses outright.
/// </remarks>
internal static unsafe class PinnedComCalls
{
    public static void VSSetConstantBuffers(this ComPtr<ID3D11DeviceContext> context, uint slot, uint count, ref ComPtr<ID3D11Buffer> buffers)
    {
        fixed (ComPtr<ID3D11Buffer>* pinned = &buffers)
        {
            context.Handle->VSSetConstantBuffers(slot, count, (ID3D11Buffer**)pinned);
        }
    }

    public static void PSSetConstantBuffers(this ComPtr<ID3D11DeviceContext> context, uint slot, uint count, ref ComPtr<ID3D11Buffer> buffers)
    {
        fixed (ComPtr<ID3D11Buffer>* pinned = &buffers)
        {
            context.Handle->PSSetConstantBuffers(slot, count, (ID3D11Buffer**)pinned);
        }
    }

    public static void PSSetShaderResources(this ComPtr<ID3D11DeviceContext> context, uint slot, uint count, ref ComPtr<ID3D11ShaderResourceView> views)
    {
        fixed (ComPtr<ID3D11ShaderResourceView>* pinned = &views)
        {
            context.Handle->PSSetShaderResources(slot, count, (ID3D11ShaderResourceView**)pinned);
        }
    }

    public static void PSSetSamplers(this ComPtr<ID3D11DeviceContext> context, uint slot, uint count, ref ComPtr<ID3D11SamplerState> samplers)
    {
        fixed (ComPtr<ID3D11SamplerState>* pinned = &samplers)
        {
            context.Handle->PSSetSamplers(slot, count, (ID3D11SamplerState**)pinned);
        }
    }

    public static void IASetVertexBuffers(
        this ComPtr<ID3D11DeviceContext> context, uint slot, uint count, ref ComPtr<ID3D11Buffer> buffers, in uint strides, in uint offsets)
    {
        fixed (ComPtr<ID3D11Buffer>* pinned = &buffers)
        fixed (uint* stride = &strides)
        fixed (uint* offset = &offsets)
        {
            context.Handle->IASetVertexBuffers(slot, count, (ID3D11Buffer**)pinned, stride, offset);
        }
    }

    /// <summary>Binds one render target, taken by value so its address is on this stack. Every bind in the renderer goes through here.</summary>
    /// <param name="context">The context.</param>
    /// <param name="target">The target.</param>
    /// <param name="depth">The depth view, which may be empty.</param>
    public static void OMSetRenderTarget(
        this ComPtr<ID3D11DeviceContext> context, ComPtr<ID3D11RenderTargetView> target, ComPtr<ID3D11DepthStencilView> depth) =>
        context.Handle->OMSetRenderTargets(1u, &target.Handle, depth.Handle);

    public static int CreateBuffer(this ComPtr<ID3D11Device> device, in BufferDesc description, SubresourceData* initial, ref ComPtr<ID3D11Buffer> buffer)
    {
        fixed (ComPtr<ID3D11Buffer>* pinned = &buffer)
        {
            return device.Handle->CreateBuffer(in description, initial, (ID3D11Buffer**)pinned);
        }
    }

    public static int CreateBuffer(this ComPtr<ID3D11Device> device, in BufferDesc description, ref readonly SubresourceData initial, ref ComPtr<ID3D11Buffer> buffer)
    {
        fixed (SubresourceData* data = &initial)
        {
            return device.CreateBuffer(in description, data, ref buffer);
        }
    }

    public static int CreateTexture2D(this ComPtr<ID3D11Device> device, in Texture2DDesc description, SubresourceData* initial, ref ComPtr<ID3D11Texture2D> texture)
    {
        fixed (ComPtr<ID3D11Texture2D>* pinned = &texture)
        {
            return device.Handle->CreateTexture2D(in description, initial, (ID3D11Texture2D**)pinned);
        }
    }

    public static int CreateTexture2D(this ComPtr<ID3D11Device> device, in Texture2DDesc description, ref readonly SubresourceData initial, ref ComPtr<ID3D11Texture2D> texture)
    {
        fixed (SubresourceData* data = &initial)
        {
            return device.CreateTexture2D(in description, data, ref texture);
        }
    }

    public static int CreateShaderResourceView<TResource>(
        this ComPtr<ID3D11Device> device, ComPtr<TResource> resource, ShaderResourceViewDesc* description, ref ComPtr<ID3D11ShaderResourceView> view)
        where TResource : unmanaged, IComVtbl<TResource>
    {
        fixed (ComPtr<ID3D11ShaderResourceView>* pinned = &view)
        {
            return device.Handle->CreateShaderResourceView((ID3D11Resource*)resource.Handle, description, (ID3D11ShaderResourceView**)pinned);
        }
    }

    public static int CreateShaderResourceView<TResource>(
        this ComPtr<ID3D11Device> device, ComPtr<TResource> resource, ref readonly ShaderResourceViewDesc description, ref ComPtr<ID3D11ShaderResourceView> view)
        where TResource : unmanaged, IComVtbl<TResource>
    {
        fixed (ShaderResourceViewDesc* described = &description)
        {
            return device.CreateShaderResourceView(resource, described, ref view);
        }
    }

    public static int CreateRenderTargetView<TResource>(
        this ComPtr<ID3D11Device> device, ComPtr<TResource> resource, RenderTargetViewDesc* description, ref ComPtr<ID3D11RenderTargetView> view)
        where TResource : unmanaged, IComVtbl<TResource>
    {
        fixed (ComPtr<ID3D11RenderTargetView>* pinned = &view)
        {
            return device.Handle->CreateRenderTargetView((ID3D11Resource*)resource.Handle, description, (ID3D11RenderTargetView**)pinned);
        }
    }

    public static int CreateRenderTargetView<TResource>(
        this ComPtr<ID3D11Device> device, ComPtr<TResource> resource, ref readonly RenderTargetViewDesc description, ref ComPtr<ID3D11RenderTargetView> view)
        where TResource : unmanaged, IComVtbl<TResource>
    {
        fixed (RenderTargetViewDesc* described = &description)
        {
            return device.CreateRenderTargetView(resource, described, ref view);
        }
    }

    public static int CreateDepthStencilView<TResource>(
        this ComPtr<ID3D11Device> device, ComPtr<TResource> resource, ref readonly DepthStencilViewDesc description, ref ComPtr<ID3D11DepthStencilView> view)
        where TResource : unmanaged, IComVtbl<TResource>
    {
        fixed (ComPtr<ID3D11DepthStencilView>* pinned = &view)
        fixed (DepthStencilViewDesc* described = &description)
        {
            return device.Handle->CreateDepthStencilView((ID3D11Resource*)resource.Handle, described, (ID3D11DepthStencilView**)pinned);
        }
    }

    public static int CreateDepthStencilState(this ComPtr<ID3D11Device> device, in DepthStencilDesc description, ref ComPtr<ID3D11DepthStencilState> state)
    {
        fixed (ComPtr<ID3D11DepthStencilState>* pinned = &state)
        {
            return device.Handle->CreateDepthStencilState(in description, (ID3D11DepthStencilState**)pinned);
        }
    }

    public static int CreateBlendState(this ComPtr<ID3D11Device> device, in BlendDesc description, ref ComPtr<ID3D11BlendState> state)
    {
        fixed (ComPtr<ID3D11BlendState>* pinned = &state)
        {
            return device.Handle->CreateBlendState(in description, (ID3D11BlendState**)pinned);
        }
    }

    public static int CreateRasterizerState(this ComPtr<ID3D11Device> device, in RasterizerDesc description, ref ComPtr<ID3D11RasterizerState> state)
    {
        fixed (ComPtr<ID3D11RasterizerState>* pinned = &state)
        {
            return device.Handle->CreateRasterizerState(in description, (ID3D11RasterizerState**)pinned);
        }
    }

    public static int CreateSamplerState(this ComPtr<ID3D11Device> device, in SamplerDesc description, ref ComPtr<ID3D11SamplerState> state)
    {
        fixed (ComPtr<ID3D11SamplerState>* pinned = &state)
        {
            return device.Handle->CreateSamplerState(in description, (ID3D11SamplerState**)pinned);
        }
    }

    public static int CreateQuery(this ComPtr<ID3D11Device> device, in QueryDesc description, ref ComPtr<ID3D11Query> query)
    {
        fixed (ComPtr<ID3D11Query>* pinned = &query)
        {
            return device.Handle->CreateQuery(in description, (ID3D11Query**)pinned);
        }
    }

    public static int CreateVertexShader(this ComPtr<ID3D11Device> device, void* code, nuint length, ref ID3D11ClassLinkage linkage, ref ComPtr<ID3D11VertexShader> shader)
    {
        fixed (ComPtr<ID3D11VertexShader>* pinned = &shader)
        fixed (ID3D11ClassLinkage* linked = &linkage)
        {
            return device.Handle->CreateVertexShader(code, length, linked, (ID3D11VertexShader**)pinned);
        }
    }

    public static int CreatePixelShader(this ComPtr<ID3D11Device> device, void* code, nuint length, ref ID3D11ClassLinkage linkage, ref ComPtr<ID3D11PixelShader> shader)
    {
        fixed (ComPtr<ID3D11PixelShader>* pinned = &shader)
        fixed (ID3D11ClassLinkage* linked = &linkage)
        {
            return device.Handle->CreatePixelShader(code, length, linked, (ID3D11PixelShader**)pinned);
        }
    }

    public static int CreateInputLayout(
        this ComPtr<ID3D11Device> device, InputElementDesc* first, uint count, void* code, nuint length, ref ComPtr<ID3D11InputLayout> layout)
    {
        fixed (ComPtr<ID3D11InputLayout>* pinned = &layout)
        {
            return device.Handle->CreateInputLayout(first, count, code, length, (ID3D11InputLayout**)pinned);
        }
    }
}
