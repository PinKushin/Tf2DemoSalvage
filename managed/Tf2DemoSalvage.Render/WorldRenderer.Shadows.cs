using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;

using Tf2DemoSalvage.Scene.ProjectedShadows;

namespace Tf2DemoSalvage.Render;

/// <summary>
/// Render-to-texture shadows (B702, finding 107): the 1024² <c>_rt_Shadows</c> atlas each caster is drawn into with the
/// <c>ShadowBuild</c> shader, and the world's shadow decals drawn with the <c>Shadow</c> shader after its decals.
/// </summary>
internal sealed unsafe partial class WorldRenderer
{
    /// <summary>
    /// `shadowbuildtexture_ps2x.fxc` and `shadow_ps2x.fxc` / `shadow_vs20.fxc`, over the world shader's own vertex input,
    /// skinning and camera.
    /// </summary>
    private const string ShadowShaderText = """

        // **Render-to-texture shadows** (B702, finding 107). One constant block serves both passes: the caster's
        // view-projection into its atlas cell and its translucent material's base transform for ShadowBuild, the shadow
        // colour and the atlas's texel size for Shadow.
        cbuffer ShadowPass : register(b8)
        {
            row_major float4x4 casterViewProjection;
            float4 casterTransform0;
            float4 casterTransform1;
            float4 shadowColour;   // rgb $color, linear (SetPixelShaderConstantGammaToLinear)
            float4 shadowTexel;    // xy one over the bound texture's size — the atlas, or the blob's: cTextureJitter
        };

        Texture2D    shadowAtlas   : register(t17);
        SamplerState shadowSampler : register(s2);

        struct ShadowBuildOut
        {
            float4 pos : SV_POSITION;
            float2 uv  : TEXCOORD0;
        };

        // **unlitgeneric_vs20 under ShadowBuild** (shadowbuild_dx9.cpp): the model posed as its own draw poses it — baked
        // frames blended, or skinned — and placed by the caster's view instead of the camera's. **No flex**: `DrawModelShadow`
        // draws with `STUDIORENDER_DRAW_NO_FLEXES | STUDIORENDER_DRAW_NO_SHADOWS` (flags 0x24, `engine.dll` `FUN_1800ebd40`).
        ShadowBuildOut VsShadowBuild(VsIn input)
        {
            ShadowBuildOut output;
            float3 posed = lerp(input.pos, input.nextPos, frameBlend.x);

            if (skinning.x >= 1.0f)
            {
                posed = SkinPosition(input.pos, input.bones, input.weights, skinning.x);
            }

            float4 world = mul(float4(posed, 1.0f), model);
            float4 coordinate = float4(input.uv, 0.0f, 1.0f);

            output.pos = mul(world, casterViewProjection);
            output.uv = float2(dot(coordinate, casterTransform0), dot(coordinate, casterTransform1));
            return output;
        }

        // `ClearBuffers( true, false )` on a cell's viewport (`clientshadowmgr.cpp:3768-3769`): the clear colour (255, 255,
        // 255, 0) over the viewport alone, as a triangle that covers it.
        float4 VsShadowClear(uint id : SV_VertexID) : SV_POSITION
        {
            return float4(id == 2 ? 3.0f : -1.0f, id == 1 ? 3.0f : -1.0f, 0.0f, 1.0f);
        }

        float4 PsShadowClear() : SV_TARGET
        {
            return float4(1.0f, 1.0f, 1.0f, 0.0f);
        }

        // `shadowbuildtexture_ps2x.fxc:22-28`: white, alpha the texture's alpha times the vertex alpha. The texture is the
        // translucent material's base texture, else the fullbright lightmap; the modulation is (1, 1, 1, 1).
        float4 PsShadowBuild(ShadowBuildOut input) : SV_TARGET
        {
            return float4(1.0f, 1.0f, 1.0f, albedoMap.Sample(wrapSampler, input.uv).a);
        }

        struct ShadowIn
        {
            float3 pos  : POSITION;
            float2 uv   : TEXCOORD0;
            float  fade : TEXCOORD1;
        };

        struct ShadowOut
        {
            float4 pos  : SV_POSITION;
            float2 uv   : TEXCOORD0;
            float  fade : TEXCOORD1;
            float3 wpos : TEXCOORD2;
            float  clip : SV_ClipDistance0;
        };

        // `shadow_vs20.fxc`: world-space corners, an identity model.
        ShadowOut VsShadow(ShadowIn input)
        {
            ShadowOut output;

            output.pos = mul(float4(input.pos, 1.0f), viewProjection);
            output.uv = input.uv;
            output.fade = input.fade;
            output.wpos = input.pos;
            output.clip = dot(float4(input.pos, 1.0f), clipPlane);
            return output;
        }

        // `shadow_ps2x.fxc:35-62`: five taps one texel of the BOUND texture apart (`shadow.cpp:95-109`) averaged — the atlas
        // for a cell's corners, `decals/simpleshadow` for a blob's, one draw each — the vertex alpha subtracted (the fade),
        // the colour lerped in from white, then fogged toward white by (1 - f)^4 — CalcPixelFogFactor's range fog,
        // unsquared — and written with no tone-map scale.
        float4 PsShadow(ShadowOut input) : SV_TARGET
        {
            float2 across = shadowTexel.xy;
            float2 diagonal = float2(shadowTexel.x, -shadowTexel.y);
            float coverage = (shadowAtlas.Sample(shadowSampler, input.uv).a +
                shadowAtlas.Sample(shadowSampler, input.uv + across).a +
                shadowAtlas.Sample(shadowSampler, input.uv - across).a +
                shadowAtlas.Sample(shadowSampler, input.uv + diagonal).a +
                shadowAtlas.Sample(shadowSampler, input.uv - diagonal).a) * 0.2f;

            coverage = saturate(coverage - input.fade);

            float3 result = 1.0f + ((coverage * shadowColour.rgb) - coverage);

            if (fogColour.w > 0.5f)
            {
                float projZ = mul(float4(input.wpos, 1.0f), viewProjection).z;
                float fogFactor = saturate(min(fogParams.z, (projZ * fogParams.w) - fogParams.x));

                result = 1.0f - ((1.0f - result) * pow(1.0f - fogFactor, 4.0f));
            }

            return float4(result, 1.0f);
        }
        """;

    /// <summary>The atlas's side, the allocator's page.</summary>
    private const int ShadowAtlasSize = ShadowTextureAllocator.PageSize;

    /// <summary>The <c>ShadowPass</c> constants' slot, both stages.</summary>
    private const uint ShadowConstantsSlot = 8;

    /// <summary>The shadow texture's shader-resource slot in the world pass: the atlas, or the blob's texture.</summary>
    private const uint ShadowAtlasSlot = 17;

    /// <summary>The atlas sampler's slot.</summary>
    private const uint ShadowSamplerSlot = 2;

    /// <summary>Floats in the <c>ShadowPass</c> block: a matrix and four float4s.</summary>
    private const int ShadowConstantFloats = 32;

    /// <summary>A shadow decal corner's stride: position, texture coordinate, fade.</summary>
    private const int ShadowVertexFloats = 6;

    private ComPtr<ID3D11Texture2D> _shadowAtlas;
    private ComPtr<ID3D11RenderTargetView> _shadowAtlasTarget;
    private ComPtr<ID3D11ShaderResourceView> _shadowAtlasView;
    private ComPtr<ID3D11VertexShader> _shadowBuildVertex;
    private ComPtr<ID3D11PixelShader> _shadowBuildPixel;
    private ComPtr<ID3D11VertexShader> _shadowClearVertex;
    private ComPtr<ID3D11PixelShader> _shadowClearPixel;

    /// <summary>Whether the atlas has never been cleared — <c>m_bRenderTargetNeedsClear</c>, set when it is made.</summary>
    private bool _shadowAtlasNeedsClear;
    private ComPtr<ID3D11VertexShader> _shadowVertex;
    private ComPtr<ID3D11PixelShader> _shadowPixel;
    private ComPtr<ID3D11InputLayout> _shadowLayout;
    private ComPtr<ID3D11Buffer> _shadowConstants;
    private ComPtr<ID3D11BlendState> _shadowBuildBlend;
    private ComPtr<ID3D11BlendState> _shadowBlend;
    private ComPtr<ID3D11SamplerState> _shadowSampler;
    private ComPtr<ID3D11DepthStencilState> _shadowBuildDepth;
    private ComPtr<ID3D11Buffer> _shadowDecalBuffer;
    private int _shadowDecalCapacity;

    /// <summary>The decal buffer's lists, one a view with surfaces of its own and one for the brush entities.</summary>
    internal enum ShadowList
    {
        /// <summary>The main view's, which the water views but the reflection draw too: their world list is its (B711).</summary>
        World,

        /// <summary>The monitor view's.</summary>
        Monitor,

        /// <summary>The 3D sky view's.</summary>
        Sky,

        /// <summary>The brush entities', shared by every view.</summary>
        Brushes,

        /// <summary>The water reflection's, from its own world list (B711).</summary>
        Reflection,

        /// <summary>The monitor camera's water reflection's, from its own world list (B711).</summary>
        MonitorReflection,
    }

    /// <summary>Each list's first corner, count, and how many of them are blobs', each run written after its cell corners.</summary>
    private readonly (int First, int Count, int Blobs)[] _shadowLists = new (int, int, int)[6];

    /// <summary>Which list the world pass draws: the main view's, the monitor's while <see cref="DrawMonitor"/> runs, or a
    /// reflection's while its view draws (B711); null for none.</summary>
    private ShadowList? _shadowWorldList = ShadowList.World;

    /// <summary>The blob texture as last handed over, and its view and size, uploaded once per texture.</summary>
    private MapTexture? _shadowBlobSource;
    private ComPtr<ID3D11ShaderResourceView> _shadowBlobView;
    private (int Width, int Height) _shadowBlobSize = (16, 16);

    /// <summary>Each model path's shadow-LOD path, made once rather than concatenated per caster per frame.</summary>
    private readonly Dictionary<string, string> _shadowLodPathOf = new(StringComparer.OrdinalIgnoreCase);
    private readonly float[] _shadowContents = new float[ShadowConstantFloats];

    /// <summary>How many shadow decal corners the world pass has drawn, ever — counted at the draw, for a test.</summary>
    internal long ShadowDecalCornersDrawn { get; private set; }

    /// <summary>
    /// Binds the <c>ShadowBuild</c> pass — <c>ComputeShadowTextures</c>' setup (`clientshadowmgr.cpp:4023-4048`): additive
    /// alpha, no depth, no cull (`engine/shadowbuild.vmt`'s <c>$nocull 1</c>).
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="colour">The shadow colour, linear, for the world pass.</param>
    /// <remarks>
    /// **The atlas keeps what it holds from frame to frame**: it is cleared whole to white with zero alpha once, when it is
    /// made (<c>m_bRenderTargetNeedsClear</c>, `:4039-4044`), and each cell drawn this frame clears its own viewport first
    /// (<see cref="SetShadowCell"/>). A cell not drawn shows what it last held, as the engine's does.
    /// </remarks>
    public void BeginShadowTextures(ComPtr<ID3D11DeviceContext> context, Vector3 colour)
    {
        EnsureShadowPipeline();

        context.OMSetRenderTarget(_shadowAtlasTarget, default(ComPtr<ID3D11DepthStencilView>));

        if (_shadowAtlasNeedsClear)
        {
            float* white = stackalloc float[4] { 1f, 1f, 1f, 0f };

            context.ClearRenderTargetView(_shadowAtlasTarget, white);
            _shadowAtlasNeedsClear = false;
        }

        BindShadowBuild(context);

        (_shadowContents[24], _shadowContents[25], _shadowContents[26], _shadowContents[27]) = (colour.X, colour.Y, colour.Z, 0f);
        (_shadowContents[28], _shadowContents[29]) = (1f / ShadowAtlasSize, 1f / ShadowAtlasSize);
    }

    /// <summary>The <c>ShadowBuild</c> pass's state: additive alpha, no depth, no cull.</summary>
    private void BindShadowBuild(ComPtr<ID3D11DeviceContext> context)
    {
        float* blendFactor = stackalloc float[4] { 1f, 1f, 1f, 1f };

        BindPipeline(context);
        context.VSSetShader(_shadowBuildVertex, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetShader(_shadowBuildPixel, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.VSSetConstantBuffers(ShadowConstantsSlot, 1, ref _shadowConstants);
        context.PSSetConstantBuffers(ShadowConstantsSlot, 1, ref _shadowConstants);
        context.RSSetState(_bothSides);
        context.OMSetDepthStencilState(_shadowBuildDepth, 0);
        context.OMSetBlendState(_shadowBuildBlend, blendFactor, 0xFFFFFFFF);
    }

    /// <summary>
    /// The blob's texture — `decals/simpleshadow`'s base texture (<c>m_SimpleShadow</c>, `clientshadowmgr.cpp:1285`),
    /// loaded sRGB as the <c>Shadow</c> shader loads its base (`shadow.cpp:47`) — uploaded when it changes.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <param name="context">The context.</param>
    /// <param name="blob">The texture, or null for none, when no blob draws.</param>
    /// <remarks>
    /// Its five taps are one of ITS texels apart, `GetActualWidth` and `GetActualHeight` (`shadow.cpp:95-109`), where the
    /// shader's own default is 16 for a material with none.
    /// </remarks>
    public void SetShadowBlob(ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, MapTexture? blob)
    {
        if (Nullable.Equals(blob, _shadowBlobSource) && (blob is null || _shadowBlobView.Handle is not null))
        {
            return;
        }

        _shadowBlobView.Dispose();
        _shadowBlobView = default;
        _shadowBlobSource = blob;
        _shadowBlobSize = (16, 16);

        if (blob is { } texture)
        {
            _shadowBlobView = UploadTexture(device, context, texture);
            _shadowBlobSize = (texture.Width, texture.Height);
        }
    }

    /// <summary>Points the pass at one shadow's cell — <c>DrawRenderToTextureShadow</c>'s viewport and view (`:3763-3772`).</summary>
    /// <param name="context">The context.</param>
    /// <param name="shadow">The shadow.</param>
    /// <remarks>
    /// **Offset half a pixel** so the cell's corners land on pixel centres, which is where Direct3D 9 puts a viewport's
    /// edges and where the world pass's texture coordinates (<see cref="ShadowCell.TexOrigin"/>) sample.
    /// </remarks>
    public void SetShadowCell(ComPtr<ID3D11DeviceContext> context, CastShadow shadow)
    {
        ArgumentNullException.ThrowIfNull(shadow);

        Viewport viewport = new(shadow.Cell.X + 0.5f, shadow.Cell.Y + 0.5f, shadow.Cell.Width, shadow.Cell.Height, 0f, 1f);
        context.RSSetViewports(1, in viewport);

        // "Clear the selected viewport only (don't need to clear depth)" (`:3767-3769`): the clear colour over the cell's
        // pixels, unblended — the half-pixel viewport covers exactly its texels' centres.
        context.IASetInputLayout(default(ComPtr<ID3D11InputLayout>));
        context.VSSetShader(_shadowClearVertex, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetShader(_shadowClearPixel, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.OMSetBlendState(default(ComPtr<ID3D11BlendState>), (float*)null, 0xFFFFFFFF);
        SubmitCounters.Draw();
        context.Draw(3, 0);
        BindShadowBuild(context);

        shadow.CasterViewProjection().AsSpan().CopyTo(_shadowContents);
    }

    /// <summary>
    /// Draws one model into the bound cell — <c>DrawModelShadow</c> (`engine.dll` `FUN_1800ebd40`): every batch its body
    /// shows, with its translucent and alpha-tested materials' base-texture alpha and white elsewhere.
    /// </summary>
    /// <param name="context">The context, the pass begun and a cell set.</param>
    /// <param name="instance">The model, its bones already sent.</param>
    /// <remarks>
    /// **`$translucent_material`** (`studiorender.dll` `FUN_1800041c0`, disassembled): the mesh's own material when it
    /// answers <c>IsTranslucent()</c> or <c>IsAlphaTested()</c>, so a cut-out adds its texture alpha unthresholded.
    /// Translucent here is the blended set — interpolated for <c>IsTranslucent()</c>, which was not read for additive.
    /// </remarks>
    public void DrawShadowModel(ComPtr<ID3D11DeviceContext> context, ModelInstance instance)
    {
        // **The model's shadow LOD when it has one** (`DrawModelShadowSetup`, `engine.dll` `FUN_1800eba80`), packed under
        // its own path; the same frames, runs and bones, fewer triangles.
        if (!_shadowLodPathOf.TryGetValue(instance.ModelPath, out string? path))
        {
            path = instance.ModelPath + Scene.PropModels.ShadowLodSuffix;
            _shadowLodPathOf[instance.ModelPath] = path;
        }

        if (!_modelBuffers.ContainsKey(path))
        {
            path = instance.ModelPath;
        }

        if (!_modelBuffers.TryGetValue(path, out ComPtr<ID3D11Buffer> vertexBuffer) || vertexBuffer.Handle is null)
        {
            return;
        }

        uint stride = VertexStride;
        uint offset = 0;

        context.IASetVertexBuffers(0, 1, ref vertexBuffer, in stride, in offset);
        BindColours(context, null);

        // The zero stream: the shader reads no flex here, and the slot must hold something the layout can read.
        BindFlex(context, string.Empty, null, null);
        SetModel(context, instance.Matrix, blend: instance.Blend, bones: instance.Bones?.Count ?? 0);

        bool uploaded = false;

        foreach (WorldBatch batch in ModelBatches(path, instance.Frame))
        {
            if (instance.BodyParts is { Count: > 0 } parts && !Shows(parts, batch.BodyPart, batch.BodyModel, instance.Body))
            {
                continue;
            }

            int material = instance.SkinSwap is not null && instance.SkinSwap.TryGetValue(batch.MaterialSlot, out int swapped)
                ? swapped
                : batch.MaterialIndex;

            // `$translucent_material` is the mesh's own when `IsTranslucent() || IsAlphaTested()` (`studiorender.dll`
            // `FUN_1800041c0`, `0x1800042cb`-`0x18000432f`), else the fullbright lightmap.
            bool translucent = (_translucent.Contains(material) || _alphaTested.Contains(material)) &&
                material >= 0 && material < _textures.Count && _textures[material].Handle is not null;

            ComPtr<ID3D11ShaderResourceView> texture = translucent ? AnimationFrame(material, _textures[material]) : _flatWhite;

            if (WriteShadowTransform(translucent ? material : -1) || !uploaded)
            {
                UploadShadowConstants(context);
                uploaded = true;
            }

            context.PSSetShaderResources(0, 1, ref texture);
            SubmitCounters.Draw();
            context.Draw((uint)batch.VertexCount, (uint)batch.FirstVertex);
        }
    }

    /// <summary>Ends the pass: the world pipeline, blend and rasteriser back, so the caller restores its target, viewport and
    /// depth state.</summary>
    /// <param name="context">The context.</param>
    public void EndShadowTextures(ComPtr<ID3D11DeviceContext> context)
    {
        ResetBlend(context);
        BindPipeline(context);
        context.RSSetState(Raster(_bothSides));
    }

    /// <summary>
    /// Replaces the frame's shadow decals: the main view's, the monitor's, the 3D sky's, each water reflection's — each cut
    /// from the surfaces that view draws (`RenderShadows` in each view's `R_DrawWorldLists`, finding 107) — and the brush
    /// entities', in one buffer, in <see cref="ShadowList"/> order.
    /// </summary>
    /// <param name="device">Device to grow the buffer on.</param>
    /// <param name="context">Context to write it through.</param>
    /// <param name="frame">The frame's shadows.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    public void UploadShadowDecals(ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, ShadowFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        ReadOnlySpan<IReadOnlyList<ShadowVertex>> lists =
            [frame.World, frame.Monitor, frame.Sky, frame.Brushes, frame.Reflection, frame.MonitorReflection];
        int total = 0;

        for (int list = 0; list < lists.Length; list++)
        {
            _shadowLists[list] = (total, lists[list].Count, 0);
            total += lists[list].Count;
        }

        if (total == 0)
        {
            return;
        }

        if (total > _shadowDecalCapacity)
        {
            _shadowDecalBuffer.Dispose();
            _shadowDecalBuffer = default;

            // Doubled, so a growing frame reallocates a handful of times rather than every frame.
            _shadowDecalCapacity = Math.Max(total, _shadowDecalCapacity * 2);

            BufferDesc description = new()
            {
                ByteWidth = (uint)(_shadowDecalCapacity * ShadowVertexFloats * sizeof(float)),
                Usage = Usage.Dynamic,
                BindFlags = (uint)BindFlag.VertexBuffer,
                CPUAccessFlags = (uint)CpuAccessFlag.Write,
            };

            SilkMarshal.ThrowHResult(device.CreateBuffer(in description, null, ref _shadowDecalBuffer));
        }

        MappedSubresource mapped = default;

        SubmitCounters.Upload();
        SilkMarshal.ThrowHResult(context.Map(_shadowDecalBuffer, 0, Map.WriteDiscard, 0, ref mapped));

        float* into = (float*)mapped.PData;

        for (int list = 0; list < lists.Length; list++)
        {
            (int first, int count, _) = _shadowLists[list];

            _shadowLists[list] = (first, count, WriteShadowCorners(into + (first * ShadowVertexFloats), lists[list]));
        }

        context.Unmap(_shadowDecalBuffer, 0);
    }

    /// <summary>
    /// One run's corners, its cells' first and its blobs' after — <c>RenderShadows</c> draws one list per material
    /// (`FUN_18012ae80`), so a run is two draws.
    /// </summary>
    /// <returns>How many are blobs'.</returns>
    private static int WriteShadowCorners(float* into, IReadOnlyList<ShadowVertex> vertices)
    {
        int cells = WriteShadowCorners(into, vertices, blob: false);

        return WriteShadowCorners(into + (cells * ShadowVertexFloats), vertices, blob: true);
    }

    /// <summary>The corners that are, or are not, blobs', in order.</summary>
    /// <returns>How many were written.</returns>
    private static int WriteShadowCorners(float* into, IReadOnlyList<ShadowVertex> vertices, bool blob)
    {
        int written = 0;

        for (int i = 0; i < vertices.Count; i++)
        {
            ShadowVertex vertex = vertices[i];

            if (vertex.Blob != blob)
            {
                continue;
            }

            float* corner = into + (written * ShadowVertexFloats);

            (corner[0], corner[1], corner[2]) = (vertex.Position.X, vertex.Position.Y, vertex.Position.Z);
            (corner[3], corner[4], corner[5]) = (vertex.TexCoord.X, vertex.TexCoord.Y, vertex.Fade);
            written++;
        }

        return written;
    }

    /// <summary>
    /// The shadow decals on brush entities — each brush model's own <c>RenderProjectedTextures</c> after its surfaces and
    /// decals (`engine.dll` `FUN_1800e05d0`), drawn here after every opaque model and the entity decals: the same
    /// multiply, depth-tested without writing, so the order among opaque draws does not change a pixel.
    /// </summary>
    /// <param name="context">The device context, after the entity decals. The caller restores its own depth state.</param>
    public void DrawEntityShadows(ComPtr<ID3D11DeviceContext> context) => DrawShadowsOf(context, ShadowList.Brushes);

    /// <summary>
    /// The world's shadow decals — <c>CShadowMgr::RenderShadows</c> (`FUN_18012ae80`), which <c>R_DrawWorldLists</c> calls
    /// after the world's decals and overlays: the <c>Shadow</c> material, <c>$decal 1</c>, so polygon-offset and depth-tested
    /// without writing, blended <c>ZERO, SRC_COLOR</c> (`shadow.cpp`).
    /// </summary>
    /// <param name="context">The context, inside the world pass.</param>
    /// <remarks>
    /// **The blend is linear**: `Shadow` writes with sRGB on, and the frame's sRGB view blends decoded values — unlike the
    /// modulating decals, which write with sRGB off and are drawn through the gamma twin (B641).
    /// </remarks>
    private void DrawShadowDecals(ComPtr<ID3D11DeviceContext> context)
    {
        if (_shadowWorldList is { } list)
        {
            DrawShadowsOf(context, list);
        }
    }

    /// <summary>
    /// The 3D sky view's shadow decals, after its world — `CSkyboxView::DrawInternal`'s `DrawWorld` (`viewrender.cpp:4926`),
    /// whose `R_DrawWorldLists` runs `RenderShadows` over the sky's own surfaces.
    /// </summary>
    private void DrawSkyShadowDecals(ComPtr<ID3D11DeviceContext> context) => DrawShadowsOf(context, ShadowList.Sky);

    /// <summary>One of the buffer's lists through <see cref="DrawShadowRange"/>.</summary>
    private void DrawShadowsOf(ComPtr<ID3D11DeviceContext> context, ShadowList list)
    {
        (int first, int count, int blobs) = _shadowLists[(int)list];

        DrawShadowRange(context, first, count, blobs);
    }

    /// <summary>
    /// One run of the shadow decal buffer through the <c>Shadow</c> pass: its cells' corners over the atlas, then its blobs'
    /// over `decals/simpleshadow` — the two materials' lists, each with its own texture's texel for the taps.
    /// </summary>
    /// <remarks>
    /// *Interpolated:* cells before blobs. The engine walks its material lists in an order not read; the multiply is
    /// commutative, so it moves a pixel only by rounding where the two overlap.
    /// </remarks>
    private void DrawShadowRange(ComPtr<ID3D11DeviceContext> context, int first, int count, int blobs)
    {
        if (count == 0 || _shadowDecalBuffer.Handle is null || _shadowAtlasView.Handle is null ||
            _decalOffset.Handle is null || _decalDepth.Handle is null)
        {
            return;
        }

        uint stride = ShadowVertexFloats * sizeof(float);
        uint offset = 0;
        float* blendFactor = stackalloc float[4] { 1f, 1f, 1f, 1f };

        context.IASetInputLayout(_shadowLayout);
        context.IASetVertexBuffers(0, 1, ref _shadowDecalBuffer, in stride, in offset);
        context.VSSetShader(_shadowVertex, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetShader(_shadowPixel, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.VSSetConstantBuffers(ShadowConstantsSlot, 1, ref _shadowConstants);
        context.PSSetConstantBuffers(ShadowConstantsSlot, 1, ref _shadowConstants);
        context.PSSetSamplers(ShadowSamplerSlot, 1, ref _shadowSampler);
        context.RSSetState(Raster(_decalOffset));
        context.OMSetDepthStencilState(_decalDepth, 0);
        context.OMSetBlendState(_shadowBlend, blendFactor, 0xFFFFFFFF);

        int cells = count - blobs;

        if (cells > 0)
        {
            DrawShadowList(context, _shadowAtlasView, (ShadowAtlasSize, ShadowAtlasSize), first, cells);
        }

        // No blob texture (no install): the blob material has nothing to draw with, so nothing is drawn.
        if (blobs > 0 && _shadowBlobView.Handle is not null)
        {
            DrawShadowList(context, _shadowBlobView, _shadowBlobSize, first + cells, blobs);
        }

        // The atlas is a render target again next frame: unbound here so it is never both.
        ComPtr<ID3D11ShaderResourceView> none = default;

        context.PSSetShaderResources(ShadowAtlasSlot, 1, ref none);
        ResetBlend(context);
        BindPipeline(context);

        uint worldStride = VertexStride;

        context.IASetVertexBuffers(0, 1, ref _vertices, in worldStride, in offset);
        context.RSSetState(Raster(_bothSides));
    }

    /// <summary>One material's list: its texture bound, the taps one of its texels apart, its corners drawn.</summary>
    private void DrawShadowList(
        ComPtr<ID3D11DeviceContext> context, ComPtr<ID3D11ShaderResourceView> texture, (int Width, int Height) size, int first, int count)
    {
        (_shadowContents[28], _shadowContents[29]) = (1f / size.Width, 1f / size.Height);
        UploadShadowConstants(context);
        context.PSSetShaderResources(ShadowAtlasSlot, 1, ref texture);

        SubmitCounters.Draw();
        context.Draw((uint)count, (uint)first);
        ShadowDecalCornersDrawn += count;
    }

    /// <summary>Writes a translucent material's base transform into the constants, identity for none.</summary>
    /// <param name="material">The material, or −1 for the fullbright texture.</param>
    /// <returns>Whether the rows changed.</returns>
    private bool WriteShadowTransform(int material)
    {
        (float, float, float, float) row0 = (1f, 0f, 0f, 0f);
        (float, float, float, float) row1 = (0f, 1f, 0f, 0f);

        if (material >= 0 && material < _detailParameters.Count && _detailParameters[material] is { } parameters &&
            parameters.Length >= BaseTransformRow1 + 4)
        {
            row0 = (parameters[BaseTransformRow0], parameters[BaseTransformRow0 + 1], parameters[BaseTransformRow0 + 2], parameters[BaseTransformRow0 + 3]);
            row1 = (parameters[BaseTransformRow1], parameters[BaseTransformRow1 + 1], parameters[BaseTransformRow1 + 2], parameters[BaseTransformRow1 + 3]);
        }

        bool changed = (_shadowContents[16], _shadowContents[17], _shadowContents[18], _shadowContents[19]) != row0 ||
            (_shadowContents[20], _shadowContents[21], _shadowContents[22], _shadowContents[23]) != row1;

        (_shadowContents[16], _shadowContents[17], _shadowContents[18], _shadowContents[19]) = row0;
        (_shadowContents[20], _shadowContents[21], _shadowContents[22], _shadowContents[23]) = row1;

        return changed;
    }

    private void UploadShadowConstants(ComPtr<ID3D11DeviceContext> context)
    {
        MappedSubresource mapped = default;

        SubmitCounters.Upload();
        SilkMarshal.ThrowHResult(context.Map(_shadowConstants, 0, Map.WriteDiscard, 0, ref mapped));
        _shadowContents.AsSpan().CopyTo(new Span<float>(mapped.PData, ShadowConstantFloats));
        context.Unmap(_shadowConstants, 0);
    }

    /// <summary>The atlas, both passes' shaders, the decal layout, the constants and the states, made once.</summary>
    private void EnsureShadowPipeline()
    {
        if (_shadowAtlas.Handle is not null)
        {
            return;
        }

        Texture2DDesc atlas = new()
        {
            Width = ShadowAtlasSize,
            Height = ShadowAtlasSize,
            MipLevels = 1,
            ArraySize = 1,
            Format = Silk.NET.DXGI.Format.FormatR8G8B8A8Unorm,
            SampleDesc = new Silk.NET.DXGI.SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)(BindFlag.RenderTarget | BindFlag.ShaderResource),
        };

        SilkMarshal.ThrowHResult(_device.CreateTexture2D(in atlas, null, ref _shadowAtlas));
        SilkMarshal.ThrowHResult(_device.CreateRenderTargetView(_shadowAtlas, null, ref _shadowAtlasTarget));
        SilkMarshal.ThrowHResult(_device.CreateShaderResourceView(_shadowAtlas, null, ref _shadowAtlasView));
        _shadowAtlasNeedsClear = true;

        using D3DCompiler compiler = D3DCompiler.GetApi();

        _shadowBuildVertex = VertexShader(compiler, "VsShadowBuild", out ComPtr<ID3D10Blob> buildBytecode);
        buildBytecode.Dispose();
        _shadowBuildPixel = PixelShader(_device, compiler, "PsShadowBuild");
        _shadowClearVertex = VertexShader(compiler, "VsShadowClear", out ComPtr<ID3D10Blob> clearBytecode);
        clearBytecode.Dispose();
        _shadowClearPixel = PixelShader(_device, compiler, "PsShadowClear");
        _shadowVertex = VertexShader(compiler, "VsShadow", out ComPtr<ID3D10Blob> decalBytecode);
        _shadowPixel = PixelShader(_device, compiler, "PsShadow");

        byte* position = (byte*)SilkMarshal.StringToPtr("POSITION");
        byte* texcoord = (byte*)SilkMarshal.StringToPtr("TEXCOORD");

        try
        {
            InputElementDesc[] elements =
            [
                new() { SemanticName = position, Format = Silk.NET.DXGI.Format.FormatR32G32B32Float, AlignedByteOffset = 0 },
                new() { SemanticName = texcoord, SemanticIndex = 0, Format = Silk.NET.DXGI.Format.FormatR32G32Float, AlignedByteOffset = 12 },
                new() { SemanticName = texcoord, SemanticIndex = 1, Format = Silk.NET.DXGI.Format.FormatR32Float, AlignedByteOffset = 20 },
            ];

            fixed (InputElementDesc* described = elements)
            {
                SilkMarshal.ThrowHResult(_device.CreateInputLayout(
                    described, (uint)elements.Length, decalBytecode.GetBufferPointer(), decalBytecode.GetBufferSize(), ref _shadowLayout));
            }
        }
        finally
        {
            SilkMarshal.Free((nint)position);
            SilkMarshal.Free((nint)texcoord);
            decalBytecode.Dispose();
        }

        BufferDesc constants = new()
        {
            ByteWidth = ShadowConstantFloats * sizeof(float),
            Usage = Usage.Dynamic,
            BindFlags = (uint)BindFlag.ConstantBuffer,
            CPUAccessFlags = (uint)CpuAccessFlag.Write,
        };

        SilkMarshal.ThrowHResult(_device.CreateBuffer(in constants, null, ref _shadowConstants));

        // ShadowBuild: `EnableAlphaBlending( SHADER_BLEND_ONE, SHADER_BLEND_ONE )` with alpha writes — BT_ADD.
        BlendDesc build = BlendStates.Additive;
        BlendDesc shadow = BlendStates.Shadow;

        SilkMarshal.ThrowHResult(_device.CreateBlendState(in build, ref _shadowBuildBlend));
        SilkMarshal.ThrowHResult(_device.CreateBlendState(in shadow, ref _shadowBlend));

        SamplerDesc sampler = new()
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            MaxLOD = float.MaxValue,
        };

        SilkMarshal.ThrowHResult(_device.CreateSamplerState(in sampler, ref _shadowSampler));

        DepthStencilDesc depth = default;

        SilkMarshal.ThrowHResult(_device.CreateDepthStencilState(in depth, ref _shadowBuildDepth));
    }

    private ComPtr<ID3D11VertexShader> VertexShader(D3DCompiler compiler, string entry, out ComPtr<ID3D10Blob> bytecode)
    {
        bytecode = Compile(compiler, entry, "vs_5_0");

        ComPtr<ID3D11VertexShader> shader = default;

        SilkMarshal.ThrowHResult(_device.CreateVertexShader(
            bytecode.GetBufferPointer(), bytecode.GetBufferSize(), ref Unsafe.NullRef<ID3D11ClassLinkage>(), ref shader));

        return shader;
    }

    private void DisposeShadows()
    {
        _shadowAtlasView.Dispose();
        _shadowAtlasTarget.Dispose();
        _shadowAtlas.Dispose();
        _shadowBuildVertex.Dispose();
        _shadowBuildPixel.Dispose();
        _shadowClearVertex.Dispose();
        _shadowClearPixel.Dispose();
        _shadowVertex.Dispose();
        _shadowPixel.Dispose();
        _shadowLayout.Dispose();
        _shadowConstants.Dispose();
        _shadowBuildBlend.Dispose();
        _shadowBlend.Dispose();
        _shadowSampler.Dispose();
        _shadowBuildDepth.Dispose();
        _shadowDecalBuffer.Dispose();
        _shadowBlobView.Dispose();
    }
}
