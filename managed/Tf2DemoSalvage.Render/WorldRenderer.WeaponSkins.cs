using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.Logging;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Diagnostics;

namespace Tf2DemoSalvage.Render;

/// <summary>
/// `CWeaponSkinProxy` at the bind (c_tf_player.cpp:3396-3707, B754): a material running `"WeaponSkin"` takes its entity's
/// war paint composite as `$basetexture`, composited on the GPU the first time it is asked for at a size.
/// </summary>
internal sealed unsafe partial class WorldRenderer
{
    /// <summary>The materials whose proxies include `WeaponSkin`, and each one's base texture's actual size.</summary>
    private readonly Dictionary<int, (int Width, int Height)> _weaponSkinMaterials = [];

    /// <summary>What a composite reads, by name under `materials/`, from the map load.</summary>
    private IReadOnlyDictionary<string, MapTexture> _compositorTextures = new Dictionary<string, MapTexture>();

    /// <summary>
    /// Each composite made, by its cache name (`TextureName`, findings 111 §4.2) — or a default handle where it failed, which
    /// is asked once: `ECRS_Error` "just show[s] the current base texture forever" (:3586-3592).
    /// </summary>
    private readonly Dictionary<string, ComPtr<ID3D11ShaderResourceView>> _composites = new(StringComparer.Ordinal);

    private CompositorRenderer? _compositor;

    /// <summary>The proxy's materials and the compositor's sources, from a map load.</summary>
    private void UploadWeaponSkins(MapAssets assets)
    {
        ReleaseWeaponSkins();
        _compositorTextures = assets.CompositorTextures;

        for (int material = 0; material < assets.Proxies.Count && material < assets.Textures.Count; material++)
        {
            foreach (MaterialProxy proxy in assets.Proxies[material])
            {
                // `Init` fails without a `$basetexture` (:3460-3463), and then the proxy never binds.
                if (proxy.Name.Equals("WeaponSkin", StringComparison.OrdinalIgnoreCase) && assets.Textures[material] is { } texture)
                {
                    _weaponSkinMaterials[material] = (texture.Width, texture.Height);
                }
            }
        }
    }

    /// <summary>Composites what this draw's `WeaponSkin` materials will bind, before any of its state is set.</summary>
    /// <remarks>
    /// **Here rather than at the bind** because a composite is its own passes into its own targets: run inside the batch loop
    /// it would leave the loop's targets, viewport and states behind. The output merger and viewport are restored; the
    /// shaders, samplers and constants are the model pass's own to bind, which it does next (<c>BindPipeline</c>).
    /// </remarks>
    private void PrepareWeaponSkins(
        ComPtr<ID3D11DeviceContext> context,
        IReadOnlyList<WorldBatch> batches,
        WeaponSkin skin,
        IReadOnlyDictionary<int, int>? skinSwap,
        string? overrideMaterial)
    {
        List<(string Name, CompositorPlan? Plan)>? missing = null;

        foreach (WorldBatch batch in batches)
        {
            if (CompositorFor(Resolved(batch, skinSwap, overrideMaterial), skin) is { } made && !_composites.ContainsKey(made.Name))
            {
                (missing ??= []).Add((made.Name, made.Compositor.Plan()));

                // Asked once, whether it succeeds or not.
                _composites[made.Name] = default;
            }
        }

        if (missing is null)
        {
            return;
        }

        SavedOutput saved = SavedOutput.Of(context);

        try
        {
            foreach ((string name, CompositorPlan? plan) in missing)
            {
                ComPtr<ID3D11ShaderResourceView> view = plan is null ? default : CompositeOrNothing(context, plan, name);

                _composites[name] = view;

                if (view.Handle is null)
                {
                    DecodeLog.Lost("render", $"war paint {name} did not composite; its weapon keeps its own texture");
                }
            }
        }
        finally
        {
            saved.Restore(context);
        }
    }

    /// <summary>
    /// One composite, or a default handle where the device refused it: `ECRS_Error` "just show[s] the current base texture
    /// forever" (c_tf_player.cpp:3586-3592), so a failure is the stock texture and a logged line, never a lost frame.
    /// </summary>
    private ComPtr<ID3D11ShaderResourceView> CompositeOrNothing(ComPtr<ID3D11DeviceContext> context, CompositorPlan plan, string name)
    {
        try
        {
            _compositor ??= CompositorRenderer.Create(_device);
            return _compositor.Composite(
                _device, context, plan, source => _compositorTextures.TryGetValue(source, out MapTexture texture) ? texture : null);
        }
        catch (Exception failure) when (failure is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            _render.LogWarning(failure, "war paint {Name} failed to composite", name);
            return default;
        }
    }

    /// <summary>The composite this batch's material binds, or a default handle where it binds its own `$basetexture`.</summary>
    private ComPtr<ID3D11ShaderResourceView> WeaponSkinTexture(int material, WeaponSkin skin) =>
        CompositorFor(material, skin) is { } made && _composites.TryGetValue(made.Name, out ComPtr<ID3D11ShaderResourceView> view)
            ? view
            : default;

    /// <summary>The batch's material as the draw resolves it: the skin's swap, then the whole-model override.</summary>
    private int Resolved(WorldBatch batch, IReadOnlyDictionary<int, int>? skinSwap, string? overrideMaterial)
    {
        int material = skinSwap is not null && skinSwap.TryGetValue(batch.MaterialSlot, out int swapped) ? swapped : batch.MaterialIndex;
        return overrideMaterial is not null && _overrideMaterials.TryGetValue(overrideMaterial, out int forced) ? forced : material;
    }

    /// <summary>
    /// The composite a `WeaponSkin` material asks for: the base texture's actual size, two mips smaller unless the item is
    /// the local player's (:3488-3508); null for a material without the proxy or a recipe the compositor refuses.
    /// </summary>
    private (string Name, TextureCompositor Compositor)? CompositorFor(int material, WeaponSkin skin)
    {
        if (!_weaponSkinMaterials.TryGetValue(material, out (int Width, int Height) size))
        {
            return null;
        }

        (int width, int height) = skin.SizeFor(size.Width, size.Height);

        // Built once per request and size: the parse and the seeded draws are the same every frame.
        if (!_compositors.TryGetValue((skin, width, height), out (string Name, TextureCompositor Compositor)? made))
        {
            made = TextureCompositor.Create(skin.Recipe, width, height, skin.Team, skin.Seed) is { } compositor
                ? (compositor.TextureName(skin.Name, 0u), compositor)
                : null;
            _compositors[(skin, width, height)] = made;
        }

        return made;
    }

    private readonly Dictionary<(WeaponSkin Skin, int Width, int Height), (string Name, TextureCompositor Compositor)?> _compositors = [];

    private void ReleaseWeaponSkins()
    {
        _compositors.Clear();
        foreach (ComPtr<ID3D11ShaderResourceView> view in _composites.Values)
        {
            view.Dispose();
        }

        _composites.Clear();
        _weaponSkinMaterials.Clear();
        _compositor?.Dispose();
        _compositor = null;
    }

    /// <summary>The output merger's targets, states and viewport, as a composite finds them.</summary>
    private readonly struct SavedOutput
    {
        private readonly ComPtr<ID3D11RenderTargetView> _target;
        private readonly ComPtr<ID3D11DepthStencilView> _depth;
        private readonly ComPtr<ID3D11DepthStencilState> _depthState;
        private readonly uint _stencil;
        private readonly ComPtr<ID3D11BlendState> _blend;
        private readonly ComPtr<ID3D11RasterizerState> _raster;
        private readonly Viewport _viewport;
        private readonly uint _viewports;

        private SavedOutput(
            ComPtr<ID3D11RenderTargetView> target, ComPtr<ID3D11DepthStencilView> depth, ComPtr<ID3D11DepthStencilState> depthState,
            uint stencil, ComPtr<ID3D11BlendState> blend, ComPtr<ID3D11RasterizerState> raster, Viewport viewport, uint viewports)
        {
            (_target, _depth, _depthState, _stencil, _blend, _raster, _viewport, _viewports) =
                (target, depth, depthState, stencil, blend, raster, viewport, viewports);
        }

        public static SavedOutput Of(ComPtr<ID3D11DeviceContext> context)
        {
            ComPtr<ID3D11RenderTargetView> target = default;
            ComPtr<ID3D11DepthStencilView> depth = default;
            context.OMGetRenderTargets(1, ref target, ref depth);

            ComPtr<ID3D11DepthStencilState> depthState = default;
            uint stencil = 0;
            context.OMGetDepthStencilState(ref depthState, ref stencil);

            ComPtr<ID3D11BlendState> blend = default;
            context.OMGetBlendState(ref blend, null, null);

            ComPtr<ID3D11RasterizerState> raster = default;
            context.RSGetState(ref raster);

            uint viewports = 1;
            Viewport viewport = default;
            context.RSGetViewports(ref viewports, ref viewport);

            return new SavedOutput(target, depth, depthState, stencil, blend, raster, viewport, viewports);
        }

        public void Restore(ComPtr<ID3D11DeviceContext> context)
        {
            // The pinned overload (`PinnedComCalls`, B706): never a `ref` to a ComPtr Silk hands native code unpinned.
            context.OMSetRenderTarget(_target, _depth);
            context.OMSetDepthStencilState(_depthState, _stencil);
            float* factor = stackalloc float[4] { 1f, 1f, 1f, 1f };
            context.OMSetBlendState(_blend, factor, 0xFFFFFFFF);
            context.RSSetState(_raster);

            // As many as were bound — none included, which unbinds the compositor's.
            context.RSSetViewports(_viewports, in _viewport);

            _target.Dispose();
            _depth.Dispose();
            _depthState.Dispose();
            _blend.Dispose();
            _raster.Dispose();
        }
    }
}
