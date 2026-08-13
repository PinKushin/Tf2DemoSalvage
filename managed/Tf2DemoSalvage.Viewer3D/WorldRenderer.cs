using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>One corner of a world triangle, ready for the GPU.</summary>
/// <param name="X">Clip-space horizontal position, -1 to 1.</param>
/// <param name="Y">Clip-space vertical position, -1 to 1.</param>
/// <param name="Depth">Clip-space depth, 0 nearest; derived from the surface's height.</param>
/// <param name="U">Texture coordinate across.</param>
/// <param name="V">Texture coordinate down.</param>
/// <param name="LightU">Lightmap atlas coordinate across.</param>
/// <param name="LightV">Lightmap atlas coordinate down.</param>
/// <param name="Alpha">Blend between the material's two textures; 0 draws only the first.</param>
/// <param name="Red">Per-vertex light, one for anything that takes its light from the lightmap.</param>
/// <param name="Green">Per-vertex light.</param>
/// <param name="Blue">Per-vertex light.</param>
/// <param name="LightStep">How far along the atlas each directional lightmap sits, or zero.</param>
/// <remarks>
/// **The per-vertex colour exists for static props and nothing else.** A brush face takes its light
/// from the lightmap atlas; a model cannot, because the same model stands in many places under
/// different light, so the compiler bakes a colour per vertex per placement. Brush faces carry
/// white here, which multiplies to no change, so one shader serves both.
/// </remarks>
internal readonly record struct WorldVertex(
    float X, float Y, float Depth, float U, float V, float LightU, float LightV, float Alpha,
    float Red = 1f, float Green = 1f, float Blue = 1f, float LightStep = 0f);

/// <summary>A run of triangles sharing one texture.</summary>
/// <param name="MaterialIndex">Which material, indexed into the map's table.</param>
/// <param name="FirstVertex">Where the run starts.</param>
/// <param name="VertexCount">How many vertices it covers.</param>
internal readonly record struct WorldBatch(int MaterialIndex, int FirstVertex, int VertexCount);

/// <summary>
/// Draws a map with its own textures and its own baked lighting.
/// </summary>
/// <remarks>
/// **Two samplers and a multiply, which is what Source itself does.** The base texture gives a
/// surface its colour and the lightmap gives it its light; multiplying them is the whole shading
/// model for world geometry in a Source map, because every light was baked at compile time.
///
/// **The lightmap is not doubled here, and that took a wrong turn to establish.** Source's shaders
/// multiply an LDR lightmap by two, so doing the same looked obviously right — and produced a map
/// washed out to white. That factor applies to the raw linear samples; by the time they reach this
/// shader <c>BspLightmaps</c> has already applied the exponent and the gamma curve, so the range is
/// spent. Doubling again is the same scaling twice.
///
/// **One draw call per material, not per face.** A map has thirteen thousand faces and two hundred
/// materials, so vertices are sorted into runs sharing a texture. The lightmap atlas is bound once
/// for all of them, which is the reason it is an atlas.
///
/// **Positions arrive already in clip space**, as they do for the point renderer, because the
/// projection is <see cref="TopDownCamera"/>'s job and is tested as ordinary arithmetic rather than
/// through a GPU.
/// </remarks>
internal sealed unsafe class WorldRenderer : IDisposable
{
    /// <summary>Bytes per vertex: three of position, two of texture, two of lightmap, one blend.</summary>
    private const int VertexStride = sizeof(float) * 12;

    private const string ShaderSource = """
        struct VsIn
        {
            float3 pos : POSITION;
            float2 uv  : TEXCOORD0;
            float2 luv : TEXCOORD1;
            float  a   : TEXCOORD2;
            float3 vc  : TEXCOORD3;
            float  ls  : TEXCOORD4;
        };

        struct VsOut
        {
            float4 pos : SV_POSITION;
            float2 uv  : TEXCOORD0;
            float2 luv : TEXCOORD1;
            float  a   : TEXCOORD2;
            float3 vc  : TEXCOORD3;
            float  ls  : TEXCOORD4;
        };

        cbuffer Camera : register(b0)
        {
            row_major float4x4 viewProjection;

            // x: a debug view that replaces the texture with a flat category colour. Turning the
            //    map into "this is world, this is terrain, this is a prop, this is missing" answers
            //    in one glance what a textured picture hides.
            // y: a cutting plane in DEPTH, which is world height inverted. Everything nearer than
            //    this is discarded, so a roof can be taken off to show the room under it - the
            //    hallways into last on cp_process being the case that asked for it. Zero draws
            //    everything.
            float4 surfaceColours;
        };

        // **Per material rather than per frame.** A detail texture's scale, strength and combine
        // mode belong to the material, so this is rewritten between draws - around two hundred
        // times a frame, which is nothing next to the draw calls themselves.
        cbuffer Material : register(b1)
        {
            // x: how many times the detail tiles per tile of the base texture
            // y: how strongly it is applied
            // z: which of the twelve combine modes to use, or -1 for no detail at all
            // w: unused
            float4 detail;

            // The colour the sampled detail is multiplied by before it is combined.
            float4 detailTint;

            // x: 1 when the material has a bump map, 0 otherwise
            // y: 1 when that bump map is self-shadowing rather than a normal map
            // z, w: unused
            float4 bump;
        };

        Texture2D    albedoMap   : register(t0);
        Texture2D    lightMap    : register(t1);
        Texture2D    blendMap    : register(t2);
        Texture2D    detailMap   : register(t3);
        Texture2D    bumpMap     : register(t4);
        SamplerState wrapSampler : register(s0);
        SamplerState clampSampler: register(s1);

        // Transcribed from TextureCombine in Valve's common_ps_fxc.h. The modes are numbered by
        // the engine and the numbers appear in materials, so they stay as numbers here.
        float4 CombineDetail(float4 albedo, float4 detailColour, int mode, float blend)
        {
            if (mode == 7)
            {
                float3 selected = lerp(detailColour.r, detailColour.a, albedo.a);
                albedo.rgb *= lerp(float3(1, 1, 1), 2.0f * selected, blend);
            }
            if (mode == 0)
            {
                albedo.rgb *= lerp(float3(1, 1, 1), 2.0f * detailColour.rgb, blend);
            }
            if (mode == 1)
            {
                albedo.rgb += blend * detailColour.rgb;
            }
            if (mode == 2)
            {
                albedo.rgb = lerp(albedo.rgb, detailColour.rgb, blend * detailColour.a);
            }
            if (mode == 3)
            {
                albedo = lerp(albedo, detailColour, blend);
            }
            if (mode == 4)
            {
                albedo.rgb = lerp(albedo.rgb, detailColour.rgb, blend * (1.0f - albedo.a));
                albedo.a = detailColour.a;
            }
            if (mode == 8)
            {
                albedo = lerp(albedo, albedo * detailColour, blend);
            }
            if (mode == 9)
            {
                albedo.a = lerp(albedo.a, albedo.a * detailColour.a, blend);
            }
            if (mode == 11)
            {
                // No blend factor in this one, deliberately: Valve's line is a bare multiply.
                albedo.rgb *= dot(detailColour.rgb, 2.0f / 3.0f);
            }
            return albedo;
        }

        // Modes 5 and 6 are self-illumination and are added AFTER the lightmap, which is why they
        // are a second function rather than two more cases above.
        float3 CombineDetailAfterLighting(float3 lit, float4 detailColour, int mode, float blend)
        {
            if (mode == 5)
            {
                lit += blend * detailColour.rgb;
            }
            if (mode == 6)
            {
                float multiplier = (blend >= 0.5f) ? 1.0f / blend : 4.0f * blend;
                float offset = (blend >= 0.5f) ? 1.0f - multiplier : -0.5f * multiplier;
                lit += saturate(multiplier * detailColour.rgb + offset);
            }
            return lit;
        }

        VsOut VsMain(VsIn input)
        {
            VsOut output;
            // **World space in, clip space out.** The vertices are uploaded once in the map's own
            // coordinates and this matrix is the only thing that changes when the view does, so a
            // resize costs 64 bytes instead of rebuilding a couple of million vertices.
            output.pos = mul(float4(input.pos, 1.0f), viewProjection);
            output.uv = input.uv;
            output.luv = input.luv;
            output.a = input.a;
            output.vc = input.vc;
            output.ls = input.ls;
            return output;
        }

        // g_localBumpBasis from Valve's bumpvects.h, to the float. Hard-coded there rather than
        // computed, so copied rather than derived.
        static const float3 bumpBasis[3] =
        {
            float3( 0.81649661064147949f,  0.0f,                0.57735025882720947f),
            float3(-0.40824821591377258f,  0.70710676908493042f, 0.57735025882720947f),
            float3(-0.40824821591377258f, -0.70710676908493042f, 0.57735025882720947f),
        };

        // Mixes the three directional lightmaps for one surface normal. Transcribed from
        // lightmappedgeneric_ps2_3_x.h; the managed BumpedLight carries the same arithmetic and is
        // where it is actually tested.
        float3 CombineBumped(float3 normal, float3 first, float3 second, float3 third, bool ssbump)
        {
            if (ssbump)
            {
                // An ssbump texel already holds three weights: no dots, no squaring, and
                // deliberately no normalising, so weights summing to two give twice the light.
                return normal.x * first + normal.y * second + normal.z * third;
            }

            float3 weights;
            weights.x = saturate(dot(normal, bumpBasis[0]));
            weights.y = saturate(dot(normal, bumpBasis[1]));
            weights.z = saturate(dot(normal, bumpBasis[2]));
            weights *= weights;

            float total = weights.x + weights.y + weights.z;

            if (total <= 0.0f)
            {
                // Valve divides here unchecked and a GPU returns NaN, which spreads through the
                // frame as a pixel nothing explains. Costs one comparison.
                return float3(0.0f, 0.0f, 0.0f);
            }

            float3 mixed = weights.x * first + weights.y * second + weights.z * third;

            // **The division is what makes this a mix rather than a scale.** The three squared
            // weights come to a third for a normal straight out of the surface, so without it the
            // wall ripples with light rather than with shape.
            return mixed / total;
        }

        float4 PsMain(VsOut input) : SV_TARGET
        {
            // **Two textures mixed by the vertex's alpha, which is what terrain is.** A
            // WorldVertexTransition material carries dirt and grass, and a displacement's vertices
            // say how much of each. Where a material has only one texture the second is bound to
            // the same image, so the mix is an identity and costs a sample.
            float4 first = albedoMap.Sample(wrapSampler, input.uv);
            float4 second = blendMap.Sample(wrapSampler, input.uv);
            // **The cut is on depth, which is height.** Discarding here rather than dropping the
            // geometry means the slice moves without rebuilding anything - the camera matrix work
            // is what makes that free.
            clip(input.pos.z - surfaceColours.y);

            float4 albedo = lerp(first, second, saturate(input.a));

            // **The detail goes in before the lighting, as Valve's shader does it.** It modifies
            // the albedo - the surface's own colour - and the lightmap then multiplies the result.
            // Applied after the light instead, the pattern would sit on top of the shading rather
            // than in it, and would be as bright in shadow as in sun.
            int mode = (int)detail.z;
            float4 detailColour = detailTint * detailMap.Sample(wrapSampler, input.uv * detail.x);

            if (mode >= 0)
            {
                albedo = CombineDetail(albedo, detailColour, mode, detail.y);
            }

            // **Alpha-tested foliage, which is what a bush IS.** Source draws leaves and grates as
            // flat cards whose texture alpha cuts out the shape. Drawn opaque, the cut-out region
            // renders as its RGB - which is black - so every bush and tree became a solid black
            // card the size of its quad. Opaque materials have their alpha forced to one on upload,
            // so this clip only ever fires on materials that asked for it.
            //
            // **After the combine, not before.** Four of the twelve modes write alpha, and alpha is
            // what this reads - so clipping first would test a value the material never asked to be
            // tested, and cut away pixels the engine keeps.
            clip(albedo.a - 0.5f);

            // In the category view the vertex colour IS the answer, so the texture is dropped.
            if (surfaceColours.x > 0.5f)
            {
                return float4(input.vc, 1.0f);
            }
            float3 light;

            if (bump.x > 0.5f && input.ls > 0.0f)
            {
                // **Set 0 is not read here, and that is the trap.** When a face is bump lit the
                // engine reads sets 1, 2 and 3 - the three ARE the lighting. Treating the flat set
                // as a base with the others adding to it gives a plausible picture that is roughly
                // twice as bright and flat where it should be shaped.
                float3 first  = lightMap.Sample(clampSampler, input.luv + float2(input.ls, 0)).rgb;
                float3 second = lightMap.Sample(clampSampler, input.luv + float2(input.ls * 2, 0)).rgb;
                float3 third  = lightMap.Sample(clampSampler, input.luv + float2(input.ls * 3, 0)).rgb;

                float4 texel = bumpMap.Sample(wrapSampler, input.uv);

                // An ssbump is sampled raw; an ordinary normal map is signed and needs decoding.
                // Applying the signed decode to an ssbump sends a flat 128 to zero and the surface
                // goes black exactly where it should be evenly lit.
                float3 normal = bump.y > 0.5f ? texel.rgb : texel.rgb * 2.0f - 1.0f;

                light = CombineBumped(normal, first, second, third, bump.y > 0.5f);
            }
            else
            {
                light = lightMap.Sample(clampSampler, input.luv).rgb;
            }

            // **No doubling here.** Source's own shaders multiply an LDR lightmap by two, but that
            // applies to the raw linear samples. BspLightmaps has already taken the sample through
            // its exponent and the gamma curve into display space, so doubling again is the second
            // half of a scaling that was already applied - measured as a map washed out to white.
            // **The vertex colour is a static prop's lightmap.** It is white for everything that
            // has a real one, so this multiply is an identity for brushwork and the whole map goes
            // through one shader rather than two.
            float3 lit = albedo.rgb * light * input.vc;

            if (mode >= 0)
            {
                lit = CombineDetailAfterLighting(lit, detailColour, mode, detail.y);
            }

            return float4(lit, albedo.a);
        }
        """;

    private ComPtr<ID3D11VertexShader> _vertexShader;
    private ComPtr<ID3D11PixelShader> _pixelShader;
    private ComPtr<ID3D11InputLayout> _layout;
    private ComPtr<ID3D11Buffer> _vertices;
    private ComPtr<ID3D11Buffer> _camera;
    private ComPtr<ID3D11SamplerState> _wrapSampler;
    private ComPtr<ID3D11SamplerState> _clampSampler;

    /// <summary>Edge of the chequer drawn where a material could not be resolved.</summary>
    private const int MissingSize = 32;

    /// <summary>Squares across the chequer, as Source draws it.</summary>
    private const int MissingSquares = 4;

    /// <summary>
    /// Uploads one texture, forcing an opaque material to be opaque.
    /// </summary>
    /// <remarks>
    /// **The shader clips on alpha, so a material that never asked for that must not carry it.** A
    /// VTF's alpha channel is only meaningful when the material says <c>$alphatest</c> or
    /// <c>$translucent</c>; plenty of opaque textures store something else there, or nothing, and
    /// clipping on it would punch holes through solid walls. Forcing it here means one shader path
    /// serves both without a per-batch switch.
    /// </remarks>
    private static ComPtr<ID3D11ShaderResourceView> Upload(
        ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, MapTexture? texture)
    {
        if (texture is not { } present)
        {
            return default;
        }

        if (present.IsTransparent)
        {
            return CreateTexture(device, context, present.Width, present.Height, present.Pixels.Span);
        }

        byte[] opaque = present.Pixels.ToArray();

        for (int at = 3; at < opaque.Length; at += 4)
        {
            opaque[at] = 255;
        }

        return CreateTexture(device, context, present.Width, present.Height, opaque);
    }

    /// <summary>Builds the missing-material chequer: magenta and black, like the engine's.</summary>
    private static byte[] Missing()
    {
        byte[] pixels = new byte[MissingSize * MissingSize * 4];
        int square = MissingSize / MissingSquares;

        for (int y = 0; y < MissingSize; y++)
        {
            for (int x = 0; x < MissingSize; x++)
            {
                bool magenta = ((x / square) + (y / square)) % 2 == 0;
                int at = ((y * MissingSize) + x) * 4;

                pixels[at + 0] = magenta ? (byte)255 : (byte)0;
                pixels[at + 1] = 0;
                pixels[at + 2] = magenta ? (byte)255 : (byte)0;
                pixels[at + 3] = 255;
            }
        }

        return pixels;
    }

    /// <summary>Rasteriser state that draws both sides of a triangle.</summary>
    /// <remarks>
    /// **D3D culls back faces by default, and displacement terrain wound the other way.** The
    /// quads that come straight out of the BSP wind one way; the grid this project builds when it
    /// subdivides a displacement winds the other, so every terrain triangle was discarded and the
    /// ground of mid and second rendered as background. It looked exactly like a black texture.
    ///
    /// Culling is turned off rather than the winding corrected, because the winding is not the
    /// thing being relied on: which faces to draw is already decided by their NORMAL, in
    /// BspGeometry and MapWorldBuilder, where a downward-facing surface is dropped. Asking the
    /// rasteriser to make the same decision from vertex order is a second source of truth that can
    /// disagree with the first - and did.
    /// </remarks>
    private ComPtr<ID3D11RasterizerState> _bothSides;

    private readonly List<ComPtr<ID3D11ShaderResourceView>> _textures = [];

    /// <summary>Which materials the engine adds rather than paints, by index.</summary>
    /// <remarks>
    /// **Drawn in a second pass with additive blending, which is what the engine does.** Source
    /// returns BT_ADD for <c>$additive</c>, so a light cone brightens what it covers and its dark
    /// parts contribute nothing. Drawn opaque it is a solid black cone; skipped entirely - which
    /// this did first - the glow is simply missing. Neither is what the map says.
    ///
    /// Five of cp_process_f12's 285 materials are additive.
    /// </remarks>
    private readonly HashSet<int> _additive = [];

    /// <summary>Blend state that ADDS a fragment to what is already there.</summary>
    private ComPtr<ID3D11BlendState> _addBlend;
    private readonly List<ComPtr<ID3D11ShaderResourceView>> _blendTextures = [];
    private ComPtr<ID3D11ShaderResourceView> _lightmap;
    private ComPtr<ID3D11ShaderResourceView> _white;

    /// <summary>The detail pattern for each material, empty where it has none.</summary>
    private readonly List<ComPtr<ID3D11ShaderResourceView>> _details = [];

    /// <summary>The bump map for each material, empty where it has none.</summary>
    private readonly List<ComPtr<ID3D11ShaderResourceView>> _bumps = [];

    /// <summary>Scale, blend factor, mode and tint per material, in the shader's own layout.</summary>
    /// <remarks>
    /// **Eight floats each, built once at upload.** A mode of -1 means the material has no detail,
    /// which is what the shader tests; packing it into the same buffer keeps the draw loop free of
    /// a branch and the shader free of a second constant.
    /// </remarks>
    private readonly List<float[]> _detailParameters = [];

    /// <summary>The per-material constants, rewritten between draws.</summary>
    private ComPtr<ID3D11Buffer> _material;

    private IReadOnlyList<WorldBatch> _batches = [];

    private WorldRenderer(
        ComPtr<ID3D11VertexShader> vertexShader,
        ComPtr<ID3D11PixelShader> pixelShader,
        ComPtr<ID3D11InputLayout> layout,
        ComPtr<ID3D11SamplerState> wrapSampler,
        ComPtr<ID3D11SamplerState> clampSampler)
    {
        _vertexShader = vertexShader;
        _pixelShader = pixelShader;
        _layout = layout;
        _wrapSampler = wrapSampler;
        _clampSampler = clampSampler;
    }

    /// <summary>Whether a map has been uploaded.</summary>
    public bool HasMap => _batches.Count > 0;

    /// <summary>Compiles the shaders and creates the samplers.</summary>
    /// <param name="device">Device to create resources on.</param>
    /// <returns>The renderer.</returns>
    public static WorldRenderer Create(ComPtr<ID3D11Device> device)
    {
        using D3DCompiler compiler = D3DCompiler.GetApi();

        ComPtr<ID3D10Blob> vertexBytecode = Compile(compiler, "VsMain", "vs_5_0");
        ComPtr<ID3D10Blob> pixelBytecode = Compile(compiler, "PsMain", "ps_5_0");

        ComPtr<ID3D11VertexShader> vertexShader = default;
        SilkMarshal.ThrowHResult(device.CreateVertexShader(
            vertexBytecode.GetBufferPointer(),
            vertexBytecode.GetBufferSize(),
            ref Unsafe.NullRef<ID3D11ClassLinkage>(),
            ref vertexShader));

        ComPtr<ID3D11PixelShader> pixelShader = default;
        SilkMarshal.ThrowHResult(device.CreatePixelShader(
            pixelBytecode.GetBufferPointer(),
            pixelBytecode.GetBufferSize(),
            ref Unsafe.NullRef<ID3D11ClassLinkage>(),
            ref pixelShader));

        byte* position = (byte*)SilkMarshal.StringToPtr("POSITION");
        byte* texcoord = (byte*)SilkMarshal.StringToPtr("TEXCOORD");

        InputElementDesc[] elements =
        [
            new()
            {
                SemanticName = position,
                Format = Silk.NET.DXGI.Format.FormatR32G32B32Float,
                AlignedByteOffset = 0,
                InputSlotClass = InputClassification.PerVertexData,
            },
            new()
            {
                SemanticName = texcoord,
                SemanticIndex = 0,
                Format = Silk.NET.DXGI.Format.FormatR32G32Float,
                AlignedByteOffset = sizeof(float) * 3,
                InputSlotClass = InputClassification.PerVertexData,
            },
            new()
            {
                SemanticName = texcoord,
                SemanticIndex = 1,
                Format = Silk.NET.DXGI.Format.FormatR32G32Float,
                AlignedByteOffset = sizeof(float) * 5,
                InputSlotClass = InputClassification.PerVertexData,
            },
            new()
            {
                SemanticName = texcoord,
                SemanticIndex = 2,
                Format = Silk.NET.DXGI.Format.FormatR32Float,
                AlignedByteOffset = sizeof(float) * 7,
                InputSlotClass = InputClassification.PerVertexData,
            },
            new()
            {
                SemanticName = texcoord,
                SemanticIndex = 3,
                Format = Silk.NET.DXGI.Format.FormatR32G32B32Float,
                AlignedByteOffset = sizeof(float) * 8,
                InputSlotClass = InputClassification.PerVertexData,
            },
            new()
            {
                SemanticName = texcoord,
                SemanticIndex = 4,
                Format = Silk.NET.DXGI.Format.FormatR32Float,
                AlignedByteOffset = sizeof(float) * 11,
                InputSlotClass = InputClassification.PerVertexData,
            },
        ];

        ComPtr<ID3D11InputLayout> layout = default;

        fixed (InputElementDesc* first = elements)
        {
            SilkMarshal.ThrowHResult(device.CreateInputLayout(
                first,
                (uint)elements.Length,
                vertexBytecode.GetBufferPointer(),
                vertexBytecode.GetBufferSize(),
                ref layout));
        }

        SilkMarshal.Free((nint)position);
        SilkMarshal.Free((nint)texcoord);
        vertexBytecode.Dispose();
        pixelBytecode.Dispose();

        // **Wrap for the texture, clamp for the lightmap.** A wall repeats its texture, so its
        // coordinates run well outside 0..1 and must wrap. A lightmap coordinate never should, and
        // wrapping one would pull in a completely unrelated face's light from the far side of the
        // atlas.
        RasterizerDesc rasterizer = new()
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.None,
        };

        ComPtr<ID3D11RasterizerState> bothSides = default;
        SilkMarshal.ThrowHResult(device.CreateRasterizerState(in rasterizer, ref bothSides));

        return new WorldRenderer(
            vertexShader,
            pixelShader,
            layout,
            Sampler(device, TextureAddressMode.Wrap),
            Sampler(device, TextureAddressMode.Clamp))
        {
            _bothSides = bothSides,
        };
    }

    /// <summary>Uploads a map's geometry, textures and lighting.</summary>
    /// <param name="device">Device to create resources on.</param>
    /// <param name="context">Context, for filling the vertex buffer.</param>
    /// <param name="vertices">Every triangle corner, sorted into material runs.</param>
    /// <param name="batches">The runs.</param>
    /// <param name="assets">The map's textures and lightmap atlas.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// Uploaded once per map rather than per frame. The geometry does not move, the lighting is
    /// baked and the textures do not change, so a frame is a handful of draw calls over resources
    /// that are already resident.
    /// </remarks>
    public void UploadMap(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        IReadOnlyList<WorldVertex> vertices,
        IReadOnlyList<WorldBatch> batches,
        MapAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        UploadTextures(device, context, assets);
        UploadGeometry(device, vertices, batches);
    }

    /// <summary>Uploads a map's textures, replacing anything already there.</summary>
    /// <param name="device">Device to create the textures on.</param>
    /// <param name="context">Context to generate their mip chains on.</param>
    /// <param name="assets">The map's textures and lightmap atlas.</param>
    /// <exception cref="ArgumentNullException"><paramref name="assets"/> is null.</exception>
    /// <remarks>
    /// **Separated from the geometry because only the geometry depends on the camera.** The
    /// projection is baked into the vertices, so a resize rebuilds them - and it used to rebuild
    /// these as well: 208 textures decoded, uploaded and mipped, every time the viewport changed
    /// size. Entering full screen fires several resizes in a row, which is how a map that loads in
    /// two seconds turned into a viewer redrawing itself once a second.
    /// </remarks>
    public void UploadTextures(
        ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, MapAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        ReleaseTextures();
        TextureUploads++;

        if (_addBlend.Handle is null)
        {
            // SRC_ONE, DEST_ONE: exactly what the engine's BT_ADD does.
            BlendDesc description = default;

            description.RenderTarget[0].BlendEnable = 1;
            description.RenderTarget[0].SrcBlend = Blend.One;
            description.RenderTarget[0].DestBlend = Blend.One;
            description.RenderTarget[0].BlendOp = BlendOp.Add;
            description.RenderTarget[0].SrcBlendAlpha = Blend.One;
            description.RenderTarget[0].DestBlendAlpha = Blend.One;
            description.RenderTarget[0].BlendOpAlpha = BlendOp.Add;
            description.RenderTarget[0].RenderTargetWriteMask = (byte)ColorWriteEnable.All;

            ComPtr<ID3D11BlendState> state = default;

            SilkMarshal.ThrowHResult(device.CreateBlendState(in description, ref state));

            _addBlend = state;
        }

        // **The engine's own convention: what is missing looks wrong on purpose.** Source draws an
        // unresolved material as a magenta and black chequer, and it is the right call - a surface
        // that quietly falls back to white or to nothing is a surface nobody investigates. Several
        // defects in this project hid for hours behind exactly that: a hole and a dark patch look
        // like art, while a magenta chequer looks like a bug and gets reported.
        _white = CreateTexture(device, context, MissingSize, MissingSize, Missing());

        for (int index = 0; index < assets.Textures.Count; index++)
        {
            MapTexture? texture = assets.Textures[index];

            _textures.Add(Upload(device, context, texture));

            if (texture is { IsAdditive: true })
            {
                _additive.Add(index);
            }
        }

        foreach (MapTexture? texture in assets.BlendTextures)
        {
            _blendTextures.Add(Upload(device, context, texture));
        }

        for (int index = 0; index < assets.Details.Count; index++)
        {
            MapDetail? detail = assets.Details[index];
            MapBump? bump = index < assets.Bumps.Count ? assets.Bumps[index] : null;

            _details.Add(detail is { } present ? Upload(device, context, present.Texture) : default);
            _bumps.Add(bump is { } mapped ? Upload(device, context, mapped.Texture) : default);

            // **Mode -1 is "no detail", and it has to be a value rather than an absence.** The
            // shader reads the same constant buffer for every draw, so a material without a detail
            // texture needs something in it that says so; leaving the previous material's numbers
            // there would apply one material's grain to the next one's surface.
            // Pulled out of the array initialisers: the analyser rejects a ternary inside one, and
            // the two values are the same whichever branch builds the rest.
            float hasBump = bump is null ? 0f : 1f;
            float isSelfShadowing = bump is { IsSelfShadowing: true } ? 1f : 0f;

            _detailParameters.Add(detail is { } values
                ?
                [
                    values.Scale,
                    values.BlendFactor,
                    values.Mode,
                    0f,
                    values.Tint.Red,
                    values.Tint.Green,
                    values.Tint.Blue,
                    1f,
                    hasBump,
                    isSelfShadowing,
                    0f,
                    0f,
                ]
                :
                [
                    0f, 0f, -1f, 0f,
                    1f, 1f, 1f, 1f,
                    hasBump,
                    isSelfShadowing,
                    0f,
                    0f,
                ]);
        }

        _lightmap = CreateTexture(
            device, context, assets.Lightmaps.Width, assets.Lightmaps.Height, assets.Lightmaps.Pixels);

        // Counted, because "we now skip additive materials" is a capability and this is the output.
        ViewerLog.Write(
            "render",
            $"{_additive.Count} of {assets.Textures.Count} materials are additive, drawn in a second pass");

        // **The output, not the capability.** A detail chain that resolves nothing draws a map that
        // looks entirely reasonable, so the count of textures actually bound is the only thing that
        // distinguishes "implemented" from "working".
        ViewerLog.Write(
            "render",
            $"{_details.Count(detail => detail.Handle is not null)} materials draw with a detail texture");

        ViewerLog.Write(
            "render",
            $"{_bumps.Count(bump => bump.Handle is not null)} materials draw with a bump map");
    }

    /// <summary>Uploads a map's projected triangles, replacing anything already there.</summary>
    /// <param name="device">Device to create the vertex buffer on.</param>
    /// <param name="vertices">Every triangle corner, already in clip space.</param>
    /// <param name="batches">The runs, one per material.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public void UploadGeometry(
        ComPtr<ID3D11Device> device,
        IReadOnlyList<WorldVertex> vertices,
        IReadOnlyList<WorldBatch> batches)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(batches);

        ReleaseGeometry();

        if (vertices.Count == 0)
        {
            return;
        }

        float[] data = new float[vertices.Count * 12];
        int at = 0;

        foreach (WorldVertex vertex in vertices)
        {
            data[at++] = vertex.X;
            data[at++] = vertex.Y;
            data[at++] = vertex.Depth;
            data[at++] = vertex.U;
            data[at++] = vertex.V;
            data[at++] = vertex.LightU;
            data[at++] = vertex.LightV;
            data[at++] = vertex.Alpha;
            data[at++] = vertex.Red;
            data[at++] = vertex.Green;
            data[at++] = vertex.Blue;
            data[at++] = vertex.LightStep;
        }

        CreateVertexBuffer(device, data);

        _batches = batches;
    }

    /// <summary>Whether to combine each material's detail texture, on by default.</summary>
    /// <remarks>
    /// **Turning it off is how the feature is measured.** A detail texture is a subtle multiply, and
    /// "the map has grain now" is not something a picture can be asserted against on its own. Two
    /// renders that differ only in this switch can be, and the same switch answers the question a
    /// person asks when they cannot tell whether it is working: what does it look like without.
    /// </remarks>
    public bool DrawDetail { get; set; } = true;

    /// <summary>Whether to light bumped surfaces from their three directional lightmaps.</summary>
    /// <remarks>
    /// Off falls back to the flat set, which is what the renderer drew before any of this and is
    /// what makes the feature measurable: two renders differing only in this switch.
    /// </remarks>
    public bool DrawBumped { get; set; } = true;

    /// <summary>Whether textures have been uploaded for a map.</summary>
    public bool HasTextures => _lightmap.Handle is not null;

    /// <summary>How many times a map's textures have been decoded and uploaded.</summary>
    /// <remarks>
    /// **Counted because the defect it guards against is invisible.** Re-uploading 208 textures on
    /// every viewport resize is correct in every respect except speed: the same picture comes out,
    /// and nothing but a clock or a counter says it happened more than once. A clock measures the
    /// machine; this measures the code.
    /// </remarks>
    public int TextureUploads { get; private set; }

    /// <summary>Draws the uploaded map.</summary>
    /// <param name="context">Context to issue the draws on.</param>
    /// <remarks>
    /// The caller binds the render target and viewport, as with every renderer here, so the same
    /// code serves the swap chain and an offscreen texture without knowing which it has.
    /// </remarks>
    public void Draw(ComPtr<ID3D11DeviceContext> context)
    {
        if (_batches.Count == 0)
        {
            return;
        }

        uint stride = VertexStride;
        uint offset = 0;

        context.RSSetState(_bothSides);
        context.IASetInputLayout(_layout);
        context.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        context.IASetVertexBuffers(0, 1, ref _vertices, in stride, in offset);
        context.VSSetShader(_vertexShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetShader(_pixelShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetSamplers(0, 1, ref _wrapSampler);
        context.PSSetSamplers(1, 1, ref _clampSampler);
        // **Bound to BOTH stages, because both read it.** The vertex shader takes the matrix and
        // the pixel shader takes the category switch and the height cut. Binding it to the vertex
        // stage alone is not an error - D3D simply hands the pixel shader zeros - so the cut sat at
        // zero however hard it was pressed, and the category view drew nothing. Found by a test
        // that renders offscreen and reads a pixel, which is the only instrument that could see it.
        context.VSSetConstantBuffers(0, 1, ref _camera);
        context.PSSetConstantBuffers(0, 1, ref _camera);
        context.PSSetShaderResources(1, 1, ref _lightmap);

        EnsureMaterialBuffer(context);

        // **Opaque first, additive after.** An additive fragment brightens whatever is behind it,
        // so anything drawn later would be added to nothing.
        foreach (WorldBatch batch in _batches)
        {
            if (_additive.Contains(batch.MaterialIndex))
            {
                continue;
            }

            ComPtr<ID3D11ShaderResourceView> texture =
                batch.MaterialIndex >= 0 && batch.MaterialIndex < _textures.Count &&
                _textures[batch.MaterialIndex].Handle is not null
                    ? _textures[batch.MaterialIndex]
                    : _white;

            // The second layer, or the first again where a material has only one - so the
            // shader's mix becomes an identity rather than needing a branch.
            ComPtr<ID3D11ShaderResourceView> blend =
                batch.MaterialIndex >= 0 && batch.MaterialIndex < _blendTextures.Count &&
                _blendTextures[batch.MaterialIndex].Handle is not null
                    ? _blendTextures[batch.MaterialIndex]
                    : texture;

            // The detail pattern, or white with the mode set to "none" - the shader skips the
            // combine entirely rather than multiplying by an identity, because several of the modes
            // have no identity to multiply by.
            ComPtr<ID3D11ShaderResourceView> detail =
                batch.MaterialIndex >= 0 && batch.MaterialIndex < _details.Count &&
                _details[batch.MaterialIndex].Handle is not null
                    ? _details[batch.MaterialIndex]
                    : _white;

            ComPtr<ID3D11ShaderResourceView> bump =
                batch.MaterialIndex >= 0 && batch.MaterialIndex < _bumps.Count &&
                _bumps[batch.MaterialIndex].Handle is not null
                    ? _bumps[batch.MaterialIndex]
                    : _white;

            SetMaterial(context, batch.MaterialIndex);

            context.PSSetShaderResources(0, 1, ref texture);
            context.PSSetShaderResources(2, 1, ref blend);
            context.PSSetShaderResources(3, 1, ref detail);
            context.PSSetShaderResources(4, 1, ref bump);
            context.Draw((uint)batch.VertexCount, (uint)batch.FirstVertex);
        }

        DrawAdditive(context);
    }

    /// <summary>Sets the view for the frames that follow.</summary>
    /// <param name="device">Device to create the buffer on, the first time.</param>
    /// <param name="context">Context to upload through.</param>
    /// <param name="matrix">Sixteen floats, row major, from <see cref="TopDownCamera.ToMatrix"/>.</param>
    /// <param name="surfaceColours">Whether to draw flat category colours instead of textures.</param>
    /// <param name="heightCut">Discard anything above this height, from 0 (all) to 1 (nothing).</param>
    /// <exception cref="ArgumentException"><paramref name="matrix"/> is not sixteen floats.</exception>
    /// <remarks>
    /// **This is what a resize costs now.** The geometry is uploaded in world coordinates and never
    /// moves; changing the view rewrites one 64-byte buffer. Before this, the projection was baked
    /// into every vertex, so a new viewport meant rebuilding 2.9 million of them - 0.33 seconds,
    /// and the reason the free camera and per-player views could not exist.
    /// </remarks>
    public void SetCamera(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        float[] matrix,
        bool surfaceColours = false,
        float heightCut = 0f)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        if (matrix.Length != 16)
        {
            throw new ArgumentException("A camera matrix is sixteen floats.", nameof(matrix));
        }

        if (_camera.Handle is null)
        {
            BufferDesc description = new()
            {
                ByteWidth = sizeof(float) * 20,
                Usage = Usage.Dynamic,
                BindFlags = (uint)BindFlag.ConstantBuffer,
                CPUAccessFlags = (uint)CpuAccessFlag.Write,
            };

            ComPtr<ID3D11Buffer> buffer = default;

            SilkMarshal.ThrowHResult(device.CreateBuffer(in description, null, ref buffer));

            _camera = buffer;
        }

        // The matrix, then a float4 whose first component is the category-view switch. Constant
        // buffers are sized in whole sixteen-byte registers, so the padding is not optional.
        float[] contents =
        [
            .. matrix,
            surfaceColours ? 1f : 0f, Math.Clamp(heightCut, 0f, 1f), 0f, 0f,
        ];

        MappedSubresource mapped = default;

        SilkMarshal.ThrowHResult(
            context.Map(_camera, 0, Map.WriteDiscard, 0, ref mapped));

        fixed (float* source = contents)
        {
            System.Buffer.MemoryCopy(
                source, mapped.PData, sizeof(float) * 20, sizeof(float) * 20);
        }

        context.Unmap(_camera, 0);
    }

    /// <summary>The per-material constants a material without a detail texture gets.</summary>
    /// <remarks>
    /// Mode -1, which is the value the shader tests to skip the combine entirely. The tint is white
    /// so that a stale sample can never darken anything even if the mode were somehow read.
    /// </remarks>
    private static readonly float[] NoDetail = [0f, 0f, -1f, 0f, 1f, 1f, 1f, 1f, 0f, 0f, 0f, 0f];

    private void EnsureMaterialBuffer(ComPtr<ID3D11DeviceContext> context)
    {
        if (_material.Handle is not null)
        {
            return;
        }

        ComPtr<ID3D11Device> device = default;

        context.GetDevice(ref device);

        BufferDesc description = new()
        {
            ByteWidth = sizeof(float) * 12,
            Usage = Usage.Dynamic,
            BindFlags = (uint)BindFlag.ConstantBuffer,
            CPUAccessFlags = (uint)CpuAccessFlag.Write,
        };

        ComPtr<ID3D11Buffer> buffer = default;

        SilkMarshal.ThrowHResult(device.CreateBuffer(in description, null, ref buffer));

        _material = buffer;
        device.Dispose();
    }

    /// <summary>Uploads one material's detail constants before its draw.</summary>
    /// <remarks>
    /// **Bound to the pixel stage only**, because only the pixel shader reads it - unlike the camera
    /// buffer, which both stages read and which spent a session bound to one of them.
    /// </remarks>
    private void SetMaterial(ComPtr<ID3D11DeviceContext> context, int materialIndex)
    {
        if (_material.Handle is null)
        {
            return;
        }

        float[] contents = materialIndex >= 0 && materialIndex < _detailParameters.Count
            ? _detailParameters[materialIndex]
            : NoDetail;

        if (!DrawDetail || !DrawBumped)
        {
            // Copied rather than mutated: the stored array is the material's, and turning a switch
            // off for one frame must not edit it for every frame after.
            contents = [.. contents];

            if (!DrawDetail)
            {
                contents[2] = -1f;
            }

            if (!DrawBumped)
            {
                contents[8] = 0f;
            }
        }

        MappedSubresource mapped = default;

        SilkMarshal.ThrowHResult(context.Map(_material, 0, Map.WriteDiscard, 0, ref mapped));

        fixed (float* source = contents)
        {
            System.Buffer.MemoryCopy(source, mapped.PData, sizeof(float) * 12, sizeof(float) * 12);
        }

        context.Unmap(_material, 0);
        context.PSSetConstantBuffers(1, 1, ref _material);
    }

    /// <summary>Draws the additive materials over everything already painted.</summary>
    private void DrawAdditive(ComPtr<ID3D11DeviceContext> context)
    {
        if (_additive.Count == 0 || _addBlend.Handle is null)
        {
            return;
        }

        float* factor = stackalloc float[4] { 1f, 1f, 1f, 1f };

        context.OMSetBlendState(_addBlend, factor, 0xFFFFFFFF);

        foreach (WorldBatch batch in _batches)
        {
            if (!_additive.Contains(batch.MaterialIndex) ||
                batch.MaterialIndex >= _textures.Count ||
                _textures[batch.MaterialIndex].Handle is null)
            {
                continue;
            }

            ComPtr<ID3D11ShaderResourceView> texture = _textures[batch.MaterialIndex];

            ComPtr<ID3D11ShaderResourceView> detail =
                batch.MaterialIndex < _details.Count &&
                _details[batch.MaterialIndex].Handle is not null
                    ? _details[batch.MaterialIndex]
                    : _white;

            SetMaterial(context, batch.MaterialIndex);

            context.PSSetShaderResources(0, 1, ref texture);
            context.PSSetShaderResources(2, 1, ref texture);
            context.PSSetShaderResources(3, 1, ref detail);
            context.Draw((uint)batch.VertexCount, (uint)batch.FirstVertex);
        }

        // Back to ordinary painting, or every later frame keeps adding.
        context.OMSetBlendState(default(ComPtr<ID3D11BlendState>), factor, 0xFFFFFFFF);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _addBlend.Dispose();
        ReleaseMap();
        _material.Dispose();
        _camera.Dispose();
        _bothSides.Dispose();
        _clampSampler.Dispose();
        _wrapSampler.Dispose();
        _layout.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();
    }

    private void ReleaseMap()
    {
        ReleaseTextures();
        ReleaseGeometry();
    }

    private void ReleaseTextures()
    {
        foreach (ComPtr<ID3D11ShaderResourceView> texture in
                 _textures.Concat(_blendTextures).Concat(_details).Concat(_bumps)
                     .Where(texture => texture.Handle is not null))
        {
            texture.Dispose();
        }

        _textures.Clear();
        _blendTextures.Clear();
        _details.Clear();
        _bumps.Clear();
        _detailParameters.Clear();
        _additive.Clear();

        if (_lightmap.Handle is not null)
        {
            _lightmap.Dispose();
            _lightmap = default;
        }

        if (_white.Handle is not null)
        {
            _white.Dispose();
            _white = default;
        }
    }

    private void ReleaseGeometry()
    {
        if (_vertices.Handle is not null)
        {
            _vertices.Dispose();
            _vertices = default;
        }

        _batches = [];
    }

    private void CreateVertexBuffer(ComPtr<ID3D11Device> device, float[] data)
    {
        BufferDesc description = new()
        {
            ByteWidth = (uint)(data.Length * sizeof(float)),
            Usage = Usage.Immutable,
            BindFlags = (uint)BindFlag.VertexBuffer,
        };

        fixed (float* first = data)
        {
            SubresourceData initial = new() { PSysMem = first };

            SilkMarshal.ThrowHResult(device.CreateBuffer(in description, in initial, ref _vertices));
        }

    }

    /// <summary>Creates a texture with a full mip chain and a view onto it.</summary>
    /// <remarks>
    /// **The mip chain is the whole point, and its absence was visible.** Uploaded with a single
    /// level, a 512-pixel texture is sampled at one texel per pixel however small the triangle is —
    /// and an overhead view of a whole map draws terrain triangles a few pixels across. Each pixel
    /// then takes an arbitrary texel, which aliases into dark noise rather than into an average.
    /// It got conspicuously worse the moment displacements were subdivided into real terrain,
    /// because that turned a handful of large quads into thousands of tiny ones.
    ///
    /// Generated by the GPU rather than uploaded from the VTF's own chain. Valve's chain is right
    /// there in the file and using it would save the generation, but it needs every level uploaded
    /// separately and the decoder currently returns one; this is the smaller change and the
    /// difference is a few milliseconds at load.
    ///
    /// The cost is that the texture can no longer be immutable — generating mips writes to it — so
    /// it is Default usage with a render-target bind, which is what GenerateMips requires.
    /// </remarks>
    private static ComPtr<ID3D11ShaderResourceView> CreateTexture(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        int width,
        int height,
        ReadOnlySpan<byte> pixels)
    {
        Texture2DDesc description = new()
        {
            Width = (uint)width,
            Height = (uint)height,

            // Zero means "every level down to 1x1", which the driver fills in.
            MipLevels = 0,
            ArraySize = 1,
            Format = Silk.NET.DXGI.Format.FormatR8G8B8A8Unorm,
            SampleDesc = new Silk.NET.DXGI.SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)(BindFlag.ShaderResource | BindFlag.RenderTarget),
            MiscFlags = (uint)ResourceMiscFlag.GenerateMips,
        };

        ComPtr<ID3D11Texture2D> texture = default;
        SilkMarshal.ThrowHResult(device.CreateTexture2D(
            in description, ref Unsafe.NullRef<SubresourceData>(), ref texture));

        // Level zero is uploaded; the rest are generated from it.
        fixed (byte* first = pixels)
        {
            context.UpdateSubresource(texture, 0, (Box*)null, first, (uint)(width * 4), 0u);
        }

        ComPtr<ID3D11ShaderResourceView> view = default;
        SilkMarshal.ThrowHResult(device.CreateShaderResourceView(
            texture, ref Unsafe.NullRef<ShaderResourceViewDesc>(), ref view));

        context.GenerateMips(view);

        texture.Dispose();
        return view;
    }

    private static ComPtr<ID3D11SamplerState> Sampler(
        ComPtr<ID3D11Device> device, TextureAddressMode address)
    {
        SamplerDesc description = new()
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = address,
            AddressV = address,
            AddressW = address,
            MaxLOD = float.MaxValue,
        };

        ComPtr<ID3D11SamplerState> sampler = default;
        SilkMarshal.ThrowHResult(device.CreateSamplerState(in description, ref sampler));

        return sampler;
    }

    /// <summary>Compiles one entry point of the world shader.</summary>
    /// <remarks>
    /// **The source is converted to bytes first, and that is not incidental.** Passing a C# string
    /// through <c>GetPinnableReference</c> hands the compiler UTF-16: it reads 's', then a zero
    /// byte, and stops. The message is
    /// <c>(1,1): error X3000: unrecognized identifier 's'</c> — a complaint about the first letter
    /// of the first word, which reads like a broken shader rather than a broken encoding.
    /// </remarks>
    private static ComPtr<ID3D10Blob> Compile(D3DCompiler compiler, string entry, string profile)
    {
        ComPtr<ID3D10Blob> bytecode = default;
        ComPtr<ID3D10Blob> errors = default;

        byte[] source = System.Text.Encoding.ASCII.GetBytes(ShaderSource);

        fixed (byte* text = source)
        {
            int result = compiler.Compile(
                text,
                (nuint)source.Length,
                (byte*)null,
                null,
                ref Unsafe.NullRef<ID3DInclude>(),
                entry,
                profile,
                0,
                0,
                ref bytecode,
                ref errors);

            if (result < 0)
            {
                // The compiler's own message, not just an HRESULT: it names the line and the
                // reason, and losing that turns a typo into a hex code.
                string message = errors.Handle is not null
                    ? SilkMarshal.PtrToString((nint)errors.GetBufferPointer()) ?? "no detail"
                    : "no detail";

                errors.Dispose();

                throw new InvalidOperationException(
                    $"Compiling {entry} ({profile}) failed: {message}");
            }
        }

        errors.Dispose();
        return bytecode;
    }
}
