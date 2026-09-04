using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Logging;

namespace Tf2DemoSalvage.Scene;

/// <summary>A decoded texture ready to upload.</summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="Image">The image and its format, ready for the GPU.</param>
/// <param name="IsTransparent">Whether the material is cut out by a threshold.</param>
/// <param name="IsAdditive">Whether the engine ADDS this material rather than painting it.</param>
/// <param name="IsTranslucent">Whether it is BLENDED with what is behind it instead.</param>
/// <param name="SelfIllum">Tint for the self-illuminated part, or null when there is none.</param>
/// <param name="IsModulate">
/// Whether the material MULTIPLIES what is behind it rather than covering it — Source's Modulate
/// shader, which declares neither $translucent nor $additive and so was read as opaque, painting
/// over exactly what it exists to shade.
/// </param>
/// <param name="IsModulateTwice">Whether that multiply doubles, so mid grey changes nothing.</param>
/// <param name="Thumbnail">
/// The VTF's own low-resolution copy of itself, which <c>mat_showlowresimage</c> draws in place of
/// the material. Null when the file carried none, which is legal.
/// </param>
/// <param name="TintsByBaseAlpha">
/// <c>$blendtintbybasealpha</c>: whether the modulation lands only where the base texture's ALPHA
/// says, which is what colours a painted hat's band rather than the whole hat (B331).
/// </param>
/// <param name="TintOverBase">
/// <c>$blendtintcoloroverbase</c>, Valve's <c>g_fTintReplacementControl</c> — 0 multiplies the tint
/// into the albedo and keeps its detail, 1 replaces the masked region with the flat colour.
/// </param>
/// <param name="ColourFactor">
/// <c>$color</c> on its own — the factor <c>$color2</c> multiplies against — because TF2's paint
/// proxy REPLACES <c>$color2</c> and <see cref="MapTexture.Modulation"/> is their product (B330).
/// </param>
/// <param name="TintBase">
/// <c>$colortint_base</c>: the colour a tintable item wears unpainted, and the second input to the
/// <c>SelectFirstIfNonZero</c> every one of them pairs with <c>ItemTintColor</c>. Null for a
/// material that is not tintable.
/// </param>
/// <param name="IsDecal">
/// Whether the material MARKS a surface rather than being one — <c>$decal</c>,
/// <c>MATERIAL_VAR_DECAL</c>. Carried per material because that is where the engine keeps render
/// state: a shader declares its own in a <c>SHADOW_STATE</c> block and the material system applies
/// it when the material is bound, so no pass inherits anything from the pass before it (B135).
/// </param>
/// <param name="IsNoCull">
/// Whether the material draws from both sides. $nocull sets MATERIAL_VAR_NOCULL in the engine
/// (imaterial.h:369) and shaders test it per material; everything else culls back faces.
/// </param>
/// <param name="MultipliesTextures">
/// Whether the material's two textures are MULTIPLIED rather than mixed by vertex alpha. That is
/// UnLitTwoTexture, whose pixel shader is baseColor * baseColor2 * g_DiffuseModulation.
/// </param>
/// <param name="IsHalfLambert">
/// Whether DIRECT light wraps around the surface -- Valve's (N.L * 0.5 + 0.5) squared, which keeps
/// a surface facing away from a light from going black. The ambient cube is unaffected.
/// </param>
/// <param name="AlphaTestReference">
/// The alpha value at and above which an alpha-tested texel is kept, or 0 when the material named
/// none and the shader API's own default of half applies. Valve overrides the reference only when
/// the material states one above zero.
/// </param>
/// <param name="Modulation">
/// The colour and alpha the whole material is scaled by — <c>$color</c> times <c>$color2</c>, and
/// <c>$alpha</c>. White and opaque for the great majority of materials, which name neither. This is
/// the REST value of the modulation a material proxy animates, so a material with a proxy has it
/// overwritten each frame and one without keeps this.
/// </param>
/// <remarks>
/// **Alpha tested and translucent are different operations and never both.** A cut-out surface is
/// drawn in the opaque pass and needs no ordering; a blended one has to be drawn afterwards, back
/// to front, without writing depth. Source decides between them explicitly, and alpha test wins.
/// </remarks>
public readonly record struct MapTexture(
    int Width,
    int Height,
    TextureImage Image,
    bool IsTransparent,
    bool IsAdditive = false,
    bool IsTranslucent = false,
    (float Red, float Green, float Blue)? SelfIllum = null,

    bool IsModulate = false,
    bool IsModulateTwice = false,
    bool IsNoCull = false,
    bool MultipliesTextures = false,
    bool IsHalfLambert = false,
    float AlphaTestReference = 0f,
    (float Red, float Green, float Blue, float Alpha)? Modulation = null,

    // **Whether this material MARKS a surface rather than being one.** $decal, MATERIAL_VAR_DECAL.
    // Carried per material because that is where the engine keeps render state — a shader declares
    // its own in a SHADOW_STATE block and the material system applies it on bind, so no pass ever
    // inherits anything (B135).
    bool IsDecal = false,

    // **The VTF's own thumbnail, for `mat_showlowresimage`.** Present only on a base texture and
    // only when the file carried one — a VTF is allowed not to, so null means "nothing to show"
    // rather than "not loaded yet".
    MapThumbnail? Thumbnail = null,

    // **Where the modulation lands, which TF2's paint makes load-bearing** (B331).
    // `$blendtintbybasealpha` tints only where the base texture's ALPHA says — a hat's band rather
    // than the whole hat — and `$blendtintcoloroverbase` lerps between multiplying the tint in and
    // replacing the albedo with it. Both default to the branch every other material takes.
    bool TintsByBaseAlpha = false,
    float TintOverBase = 0f,

    // **`$color` alone and `$colortint_base`, for TF2's paint proxies** (B330). The chain ends by
    // REPLACING `$color2`, and `Modulation` above is the resting product of the two colours — which
    // cannot be taken apart afterwards without dividing by a legally zero value. Null tint base
    // means the material is not tintable, which is nearly all of them.
    (float Red, float Green, float Blue) ColourFactor = default,
    (float Red, float Green, float Blue)? TintBase = null);

/// <summary>The tiny copy of itself that a VTF stores ahead of its mip chain.</summary>
/// <param name="Width">From <c>lowResImageWidth</c>; 16 or less in every shipped texture measured.</param>
/// <param name="Height">From <c>lowResImageHeight</c>.</param>
/// <param name="Image">Decoded to RGBA, like every other texture here.</param>
/// <remarks>
/// **What <c>mat_showlowresimage</c> draws in place of the material.** It is a distinct asset rather
/// than the smallest mip: a separate DXT1 image the compiler wrote, always DXT1 whatever the
/// texture's own format is (`VtfLowResolutionConformanceTests` measures a Dxt5 texture with a Dxt1
/// thumbnail). Showing the last mip instead would look similar and answer a different question.
/// </remarks>
public readonly record struct MapThumbnail(int Width, int Height, TextureImage Image);

/// <summary>A material's detail texture and the numbers that say how to combine it.</summary>
/// <param name="Texture">The detail pattern itself.</param>
/// <param name="Scale">How many times it tiles per tile of the base texture, across and down.</param>
/// <param name="BlendFactor">How strongly it is applied.</param>
/// <param name="Mode">Which of the twelve combine modes to use.</param>
/// <param name="Tint">The colour the sampled detail is multiplied by first.</param>
/// <remarks>
/// **The mode here is the engine's, not the material's.** If the detail texture's own VTF carries
/// the self-shadowing bump flag, the engine overrides <c>$detailblendmode</c> — so this is resolved
/// once, at load, rather than left for the renderer to work out per frame.
/// </remarks>
public readonly record struct MapDetail(
    MapTexture Texture,
    (float U, float V) Scale,
    float BlendFactor,
    int Mode,
    (float Red, float Green, float Blue) Tint);

/// <summary>A material's bump map and how to read it.</summary>
/// <param name="Texture">The normal map, or the self-shadowing weights.</param>
/// <param name="IsSelfShadowing">Whether it stores three light weights rather than a direction.</param>
/// <remarks>
/// **The two are indistinguishable by looking and combine completely differently.** A normal map is
/// decoded as <c>xyz * 2 - 1</c> and drives squared dot products against the bump basis; a
/// self-shadowing one is sampled raw and its channels ARE the weights. On cp_process_final it is 14
/// against 13, so neither can be treated as the special case.
/// </remarks>
public readonly record struct MapBump(MapTexture Texture, bool IsSelfShadowing);

/// <summary>How a material shades whatever cubemap it reflects.</summary>
/// <param name="Tint">The colour the sample is multiplied by; white unless <c>$envmaptint</c>.</param>
/// <param name="Contrast">
/// How far the reflection is pushed toward its own square. **Zero is normal**, which is the
/// opposite end from <paramref name="Saturation"/>.
/// </param>
/// <param name="Saturation">
/// How much colour the reflection keeps. **One is normal**, zero is greyscale.
/// </param>
/// <param name="MaskedByBaseAlpha">
/// Whether the base texture's alpha masks the reflection — <c>inverted</c>, so an opaque texel
/// reflects least, and the material then has no transparency because the channel is spent.
/// </param>
/// <remarks>
/// **Separate from the cube itself because the engine keeps them separate**, and because a model
/// needs exactly this half without the other. <c>$envmap</c> names WHICH cubemap and
/// <c>$envmaptint</c>, <c>$envmapcontrast</c> and <c>$envmapsaturation</c> say how to shade it —
/// four distinct <c>SHADER_PARAM</c>s. A brush face knows both at load, because vbsp patched the
/// texture name into its material; a model knows only the shading, because <c>$envmap</c> still
/// says the literal <c>env_cubemap</c> and which cube that means depends on where the model stands.
/// </remarks>
/// <param name="Fresnel">
/// How much of the reflection survives head-on. **One is a mirror and is the default**, which makes
/// the Schlick term a constant; zero is water. Always one for a model, because
/// <c>VertexLitGeneric</c> has no Fresnel term at all.
/// </param>
/// <param name="MaskedByNormalMapAlpha">
/// Whether the bump map's alpha masks the reflection — **not inverted**, so an alpha of 1 reflects
/// most. The opposite sense from <paramref name="MaskedByBaseAlpha"/>, and mutually exclusive with
/// it: a bumped material cannot use the base-alpha mask at all, so this is the one TF2's models use.
/// </param>
public readonly record struct MapEnvmapShading(
    (float Red, float Green, float Blue) Tint,
    float Contrast,
    float Saturation,
    bool MaskedByBaseAlpha,
    float Fresnel,
    bool MaskedByNormalMapAlpha);

/// <summary>A material's specular highlight, <c>$phong</c>.</summary>
/// <param name="Exponent">How tight the highlight is; 5 by default, which is broad.</param>
/// <param name="Boost">
/// How far it is pushed past the light's own brightness. **One calibration with the mask**, which
/// the parameter's own declaration says: "specular mask channel should be authored to account for
/// this".
/// </param>
/// <param name="Fresnel">
/// <c>$phongfresnelranges</c>, **already encoded** as <c>((mid-min)*2, mid, (max-mid)*2)</c> the way
/// the shader wants it. The raw triple is silently wrong rather than obviously wrong.
/// </param>
/// <param name="Tint">The colour the highlight is multiplied by; white unless <c>$phongtint</c>.</param>
/// <param name="MaskedByBaseAlpha">
/// Whether the mask is the base texture's alpha rather than the bump map's. The flag also asserts
/// there is no normal map at all.
/// </param>
/// <remarks>
/// **330 materials on cp_process ask for this, and it is why every model reads dull.** TF2's
/// characters and weapons take most of their definition from a highlight that moves with the light,
/// and a viewer without it draws them as flat colour.
/// </remarks>
/// <param name="Rim">The rim light along the silhouette, or null for a material without one.</param>
public readonly record struct MapPhong(
    float Exponent,
    float Boost,
    (float Low, float Mid, float High) Fresnel,
    (float Red, float Green, float Blue) Tint,
    bool MaskedByBaseAlpha,
    MapRimLight? Rim = null);

/// <summary>The light along a model's silhouette, <c>$rimlight</c>.</summary>
/// <param name="Exponent">How tightly it hugs the edge; 4 by default, against phong's 5.</param>
/// <param name="Boost">
/// How much of the surroundings the rim picks up. It scales the half that comes from the ambient
/// cube rather than from the light, which is what lets a model catch its surroundings on the edge
/// with no direct light on it.
/// </param>
/// <remarks>
/// **Nested inside <see cref="MapPhong"/> because it cannot exist without it**, and that is the
/// engine's own dispatch rather than a simplification here: rim lighting lives in the Skin shader,
/// which <c>VertexLitGeneric</c> reaches only when <c>$phong</c> is set. A material asking for a rim
/// and no phong gets neither.
///
/// **It is folded in with <c>max</c>, not added** — Valve's comment says why: *"Fold rim lighting
/// into specular term by using the max so that we don't really add light twice"*. Adding
/// double-counts on the silhouette of anything shiny, which is exactly where both terms peak.
/// </remarks>
public readonly record struct MapRimLight(float Exponent, float Boost);

/// <summary>A material's baked reflection: six cube faces and how to shade them.</summary>
/// <param name="Faces">
/// The six cube directions in Valve's order, which is <c>+X, −X, +Y, −Y, +Z, −Z</c> — the same
/// order D3D's <c>TextureCube</c> wants, so this uploads as-is. The file's seventh face is a
/// fallback spheremap and is not here.
/// </param>
/// <param name="Shading">The material's <c>$envmap*</c> parameters.</param>
/// <remarks>
/// **Not deduplicated, deliberately.** 51 of cp_process_final's materials reference 43 cubemaps, so
/// interning would save eight copies of a 24 KB image — under 200 KB against a lightmap atlas of
/// 2048x3485. Keeping one per material makes this list parallel to every other in
/// <see cref="MapAssets"/>, which is the property the renderer indexes on.
/// </remarks>
public readonly record struct MapCubemap(
    IReadOnlyList<MapTexture> Faces,
    MapEnvmapShading Shading);

/// <summary>One cubemap the map baked, and where it stands.</summary>
/// <param name="Placement">The lump's own record, carried whole.</param>
/// <param name="Faces">The six cube directions, in the same order as <see cref="MapCubemap"/>.</param>
/// <remarks>
/// **The map's cubemaps as PLACEMENTS rather than as one material's reflection**, which is what a
/// model needs. A brush face's cubemap was chosen by vbsp and baked into its material name, so the
/// renderer never has to know where any of them are. A model's is chosen at draw time from where it
/// stands, so it does — see <c>BspCubemaps.Closest</c>.
///
/// The lump record is carried rather than copied out into loose coordinates, so nothing downstream
/// has to rebuild one — and rebuilding one means inventing a <c>Size</c>, where 0 is an escape value
/// meaning "the default" rather than a size.
/// </remarks>
public readonly record struct MapPlacedCubemap(
    BspCubemap Placement,
    IReadOnlyList<MapTexture> Faces);

/// <summary>Everything one material resolved to.</summary>
/// <param name="Texture">The base texture, or null when it could not be found.</param>
/// <param name="Blend">The second layer of a blend material, or null.</param>
/// <param name="Detail">The detail pattern, or null.</param>
/// <param name="Bump">The bump map, or null.</param>
/// <param name="Proxies">The proxies the material runs, evaluated per bind.</param>
/// <param name="Declared">Every parameter the VMT named, for reporting the unimplemented ones.</param>
/// <param name="Shader">
/// The shader the material names. Carried for the census: a shader decides what its parameters
/// MEAN, and Modulate declared nothing unfamiliar while drawing entirely differently.
/// </param>
/// <param name="Cubemap">The baked reflection this material names, or null.</param>
/// <param name="LocalReflection">
/// How to shade the map's own cubemap, for a material that asks for the literal <c>env_cubemap</c>
/// and so has no cubemap of its own. Null for every other material, including one that reflects
/// nothing — the two are distinguished because they draw differently.
/// </param>
/// <param name="Phong">The specular highlight this material asks for, or null.</param>
/// <param name="LightWarp">The authored lighting ramp, or null for a linear falloff.</param>
/// <param name="SelfIllumMask">
/// Which parts light themselves, or null — in which case the base map's ALPHA says, which is the
/// engine's own fallback rather than "nothing glows" (B327).
/// </param>
/// <remarks>
/// A record rather than a longer and longer tuple: at four members the positional form stops
/// saying which is which at the call site, and two of these are the same type.
/// </remarks>
public readonly record struct ResolvedMaterial(
    MapTexture? Texture,
    MapTexture? Blend,
    MapDetail? Detail,
    MapBump? Bump,
    IReadOnlyList<MaterialProxy>? Proxies = null,
    IReadOnlyCollection<string>? Declared = null,
    string Shader = "",
    MapCubemap? Cubemap = null,
    MapEnvmapShading? LocalReflection = null,
    MapPhong? Phong = null,
    MapTexture? LightWarp = null,
    MapTexture? SelfIllumMask = null);

// GameArchives moved to Tf2DemoSalvage.Content.Assets on 2026-08-22 (D53's sibling): every other
// reader of the game's files already lived there, and sound needs it now as well as the renderer.

/// <summary>
/// Everything needed to draw one map as the game draws it.
/// </summary>
/// <remarks>
/// **Resolution order is the map first, then the game.** A community map ships overrides of stock
/// materials in its own pakfile, and the game's copy is not the one it was built against.
///
/// **A missing texture is normal and must stay cheap.** Of cp_process_final's 211 materials, three
/// resolve to nothing even with the game installed — and on a machine without TF2, most would.
/// Those faces draw with the material's <c>reflectivity</c> instead, which is the average colour the
/// map compiler recorded from the texture: not the texture, but the right colour, and free.
/// </remarks>
public sealed class MapAssets
{
    private MapAssets(
        IReadOnlyList<MapTexture?> textures,
        IReadOnlyList<MapTexture?> blendTextures,
        IReadOnlyList<MapDetail?> details,
        IReadOnlyList<MapBump?> bumps,
        IReadOnlyList<MapCubemap?> cubemaps,
        IReadOnlyList<IReadOnlyList<MaterialProxy>> proxies,
        IReadOnlyList<BspMaterial> materials,
        IReadOnlyList<string> shaders,
        LightmapAtlas lightmaps,
        IReadOnlyList<PropVertex> props,
        int resolved,
        int missing)
    {
        Shaders = shaders;
        Textures = textures;
        BlendTextures = blendTextures;
        Details = details;
        Bumps = bumps;
        Cubemaps = cubemaps;
        Proxies = proxies;
        Materials = materials;
        Lightmaps = lightmaps;
        Props = props;
        Resolved = resolved;
        Missing = missing;
    }

    /// <summary>
    /// Materials that replace a whole model's own, keyed by their VMT path (B325).
    /// </summary>
    /// <remarks>
    /// **The engine's `ForcedMaterialOverride`, which is not a skin swap.** A skin picks a different
    /// entry from the model's OWN material table; this replaces every one of them with a single
    /// material the model never mentions —
    /// <c>m_MaterialOverride.Init( materialOverrideFilename, TEXTURE_GROUP_CLIENT_EFFECTS )</c> in
    /// `C_TFRagdoll::CreateTFRagdoll` (`c_tf_player.cpp:972`), bound by
    /// `modelrender->ForcedMaterialOverride( pOverrideMaterial )`
    /// (`c_baseanimating.cpp:3438`).
    ///
    /// **Resolved at map load rather than on demand, and the reason is where the loaders live.**
    /// `Resolve` needs the pakfile and the game archives, which exist only while a map is being
    /// read — `MapAssets` keeps neither. Two textures is a negligible eager cost against retaining
    /// both readers for the life of a map, and it avoids a lazy cache whose first READ is a write
    /// (`docs/memory/a-lazy-cache-makes-reading-a-write.md`).
    ///
    /// **A missing entry is normal.** These live in the game install, so a machine without TF2
    /// resolves none of them and a corpse keeps its own materials — which is the same thing the
    /// engine does when `m_MaterialOverride` fails to init.
    ///
    /// **An INDEX into the ordinary material table, not a texture, and the difference is the whole
    /// point.** A material is not its base map: gold's look is mostly
    /// `$envmap cubemaps/cubemap_gold001` with `$envmaptint [1.5 1.2 .2]`, `$phongboost` and a rim
    /// term, and ice adds a bump, a phong warp and a light warp. The first version of this held a
    /// <c>MapTexture</c> per path and swapped only slot 0, so a golden corpse would have drawn a
    /// flat 32-pixel swatch with the PLAYER material's cubemap, phong and detail still applied —
    /// the shape of a divergence that looks implemented. Appending them to the table the map's own
    /// and every model's materials already share means the whole material follows: every texture
    /// slot, every shader constant, the proxies, the blend state and the depth state.
    /// </remarks>
    public IReadOnlyDictionary<string, int> OverrideMaterials { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every material the scene may ask for by name rather than by index.</summary>
    /// <remarks>
    /// **A fixed list, because the engine's are fixed.** `CreateTFRagdoll` names exactly two paths
    /// as string literals; nothing computes one. Keeping the list here rather than accepting an
    /// arbitrary path means a map load cannot be made to read whatever a demo asks for.
    /// </remarks>
    private static readonly string[] OverrideMaterialPaths =
    [
        "models/player/shared/gold_player",
        "models/player/shared/ice_player",
    ];

    /// <summary>Appends the whole-model override materials to the table, if the install has them.</summary>
    /// <param name="assets">Where resolution is reported.</param>
    /// <param name="table">The material table every face and every model already indexes into.</param>
    /// <param name="pak">The map's own pakfile, which is searched first.</param>
    /// <param name="archives">The game's archives.</param>
    /// <param name="maximumTextureSize">The upload limit, as for every other material.</param>
    /// <returns>The table index each path took, keyed by the name the scene asks with.</returns>
    /// <remarks>
    /// **Appended last, after the brushwork and after every model**, because the table's order is
    /// load-bearing for everything before it: a face indexes it and a model's batches index it. Two
    /// entries on the end change nothing already in it.
    ///
    /// **A `BspMaterial` is fabricated for each, and its reflectivity is not read from anywhere.**
    /// That field is the map's own radiosity input, taken from the BSP's texdata lump; an override
    /// bounces no light because it is never on a brush face. Zero is the honest value rather than a
    /// guess — see `docs/memory/sentinels-conflate-unknown-with-answer.md` for the case against
    /// inventing one.
    /// </remarks>
    private static Dictionary<string, int> LoadOverrideMaterials(
        ILogger assets,
        MaterialTable table,
        PakFile pak,
        GameArchives archives,
        int maximumTextureSize)
    {
        Dictionary<string, int> loaded = new(StringComparer.OrdinalIgnoreCase);

        foreach (string path in OverrideMaterialPaths)
        {
            // `report: false` because a machine without TF2 misses all of them, and one warning per
            // map load per material would bury the failures that matter.
            ResolvedMaterial material =
                Resolve(assets, path, pak, archives, maximumTextureSize, report: false);

            if (material.Texture is null)
            {
                continue;
            }

            loaded[path + ".vmt"] = table.Add(
                new BspMaterial(path, (0f, 0f, 0f), material.Texture.Value.Width,
                    material.Texture.Value.Height),
                material);
        }

        assets.LogInformation(
            "{Message}",
            $"{loaded.Count} of {OverrideMaterialPaths.Length} whole-model override materials " +
            $"resolved{(loaded.Count == 0 ? string.Empty : $" at {string.Join(", ", loaded.Select(entry => $"{entry.Key}={entry.Value}"))}")}");

        return loaded;
    }

    /// <summary>The map's placed models, in world space, three corners per triangle.</summary>
    /// <remarks>
    /// **Their materials continue the map's own table**, so a prop's material index indexes
    /// <see cref="Textures"/> exactly like a brush face's. That is what lets one renderer draw both.
    /// </remarks>
    public IReadOnlyList<PropVertex> Props { get; }

    /// <summary>Entity models, in their own coordinates, keyed by path.</summary>
    /// <remarks>
    /// Model space rather than world space, unlike <see cref="Props"/>: a static prop stands where
    /// the map put it and can be baked, while an entity moves and is posed by a matrix in the
    /// shader.
    /// </remarks>
    public IReadOnlyDictionary<string, PropModels.ModelFrames> EntityModels { get; private init; } =
        new Dictionary<string, PropModels.ModelFrames>(StringComparer.OrdinalIgnoreCase);

    /// <summary>One model's triangles, or null for anything this load did not find.</summary>
    /// <param name="path">The model path, as the demo names it.</param>
    /// <returns>The frames, or null.</returns>
    /// <remarks>
    /// **The lookup lives with the dictionary it reads**, which is the whole of why it moved:
    /// <c>MainForm.ModelGeometry</c> knew that geometry is <see cref="EntityModels"/> keyed by path,
    /// and a second frontend would have had to know it too (D90).
    ///
    /// A miss answers null rather than throwing, and <see cref="EntityModelSet"/> remembers it
    /// rather than asking again every frame — the miss was already reported once, at load, where a
    /// missing asset is worth reading.
    /// </remarks>
    public PropModels.ModelFrames? Geometry(string path) =>
        EntityModels.TryGetValue(path, out PropModels.ModelFrames? frames) ? frames : null;

    /// <summary>Each material's VMT shader name, e.g. <c>LightmappedGeneric</c> or <c>Water</c>.</summary>
    /// <remarks>
    /// **Carried so "no base texture" can be read correctly** (B62). For most shaders a null texture
    /// is a failure and the engine's magenta chequer is the right answer; for <c>Water</c> it is not,
    /// because water declares none by design. Only the shader name distinguishes them.
    /// </remarks>
    public IReadOnlyList<string> Shaders { get; }

    /// <summary>One decoded texture per material, null where none was found.</summary>
    public IReadOnlyList<MapTexture?> Textures { get; }

    /// <summary>The second layer of a blend material, null for the great majority that have none.</summary>
    /// <remarks>
    /// **This is where grass comes from.** A <c>WorldVertexTransition</c> material names two
    /// textures — on cp_process_final, <c>dirtground009</c> and <c>grass_07</c> — and a
    /// displacement's per-vertex alpha mixes them. Sampling only the first draws every outdoor
    /// surface as bare dirt, which is exactly how the map looked.
    /// </remarks>
    public IReadOnlyList<MapTexture?> BlendTextures { get; }

    /// <summary>The detail pattern for each material, null for those without one.</summary>
    /// <remarks>
    /// **A detail texture is what stops a wall looking like a flat colour.** It is a small tiling
    /// pattern - concrete grain, brick speckle, noise - multiplied into the base texture at four
    /// times its frequency by default, and it is the difference between a surface and a swatch.
    /// </remarks>
    public IReadOnlyList<MapDetail?> Details { get; }

    /// <summary>The bump map for each material, null for those without one.</summary>
    /// <remarks>
    /// **A bump map does not change a surface's colour, it changes which of its four lightmaps are
    /// read.** vrad stored light arriving from three directions; the normal map says which way each
    /// pixel of the surface faces, and the three are mixed accordingly. That is what makes a flat
    /// wall look like brick rather than like a photograph of one.
    /// </remarks>
    public IReadOnlyList<MapBump?> Bumps { get; }

    /// <summary>The baked reflection for each material, null for those without one.</summary>
    /// <remarks>
    /// **The map already decided which cubemap each surface reflects**, so this needs no
    /// nearest-by-position search. vbsp patched every reflecting brush face's material at compile
    /// time to name the exact cubemap it baked; this reads that name.
    ///
    /// Null covers three different situations that all draw the same: a material that reflects
    /// nothing, a material asking for the literal <c>env_cubemap</c> (which is
    /// <see cref="LocalReflections"/> instead, chosen per draw), and a cubemap that failed to
    /// decode. The first is much the commonest — on cp_process_final, 51 of 410.
    /// </remarks>
    public IReadOnlyList<MapCubemap?> Cubemaps { get; }

    /// <summary>
    /// How each material shades the map's own cubemap, null for those that do not ask for it.
    /// </summary>
    /// <remarks>
    /// **The model half of <c>$envmap</c>, and it is a different question from
    /// <see cref="Cubemaps"/> rather than a variant of it.** A brush face's cubemap was chosen by
    /// vbsp at compile time and baked into the material's name, so the name is the whole answer. A
    /// model's material still says the literal <c>env_cubemap</c>, which <c>VertexLitGeneric</c>
    /// keeps and resolves at runtime against whatever the engine has bound as local — so the
    /// material supplies only the shading, and the cube comes from <see cref="PlacedCubemaps"/> by
    /// where the model stands.
    /// </remarks>
    public IReadOnlyList<MapEnvmapShading?> LocalReflections { get; private init; } = [];

    /// <summary>Every cubemap the map baked, decoded, with where each one stands.</summary>
    /// <remarks>
    /// Empty when the map bakes none, when it packs none of the bakes, or when nothing could be
    /// decoded — all of which are legal and draw matte.
    /// </remarks>
    public IReadOnlyList<MapPlacedCubemap> PlacedCubemaps { get; private init; } = [];

    /// <summary>Valve's measurement grid, drawn under the category colours, or null.</summary>
    public MapTexture? DevGrid { get; private init; }

    /// <summary>Valve's luxel grid, drawn at lightmap coordinates for <c>mat_luxels</c>, or null.</summary>
    public MapTexture? LuxelGrid { get; private init; }

    /// <summary>What Valve draws in place of a model that will not load.</summary>
    /// <remarks>
    /// <c>game/server/props.cpp:245</c> — <c>SetModelName( AllocPooledString( "models/error.mdl" ) )</c>
    /// — and <c>detailobjectsystem.cpp:1603</c> loads the same for a detail prop. A solid mesh
    /// rather than a chequer, because a chequer needs a surface and a missing model has none.
    /// </remarks>
    public const string ErrorModel = "models/error.mdl";

    /// <summary>The specular highlight for each material, null for those without one.</summary>
    /// <remarks>
    /// **330 of cp_process's materials ask for this**, which made it the largest single unimplemented
    /// parameter for as long as it was one. A model without it reads as flat colour, because TF2's
    /// characters take most of their shape from a highlight that moves with the light rather than
    /// from their diffuse texture.
    /// </remarks>
    public IReadOnlyList<MapPhong?> Phong { get; private init; } = [];

    /// <summary>The authored lighting ramp for each material, null for a linear falloff.</summary>
    /// <remarks>
    /// **308 of cp_process's materials name one.** It is a one-dimensional texture indexed by the
    /// diffuse term, and it is much of why TF2 reads as illustrated rather than photographed — the
    /// artist draws the falloff instead of accepting Lambert's.
    /// </remarks>
    public IReadOnlyList<MapTexture?> LightWarps { get; private init; } = [];

    /// <summary>Which parts of each material light themselves, where a texture says so.</summary>
    /// <remarks>
    /// <c>$selfillummask</c>, null for every material that has none — which is nearly all of them,
    /// and for those the BASE MAP'S ALPHA decides it instead. That fallback is the important half:
    /// the engine writes the two as one expression,
    ///
    /// <code>
    /// vSelfIllumMask = lerp( baseColor.aaa, vSelfIllumMask, g_SelfIllumMaskControl );
    /// </code>
    ///
    /// (<c>vertexlit_and_unlit_generic_ps2x.fxc:442</c>), where the control is 1 exactly when a mask
    /// texture is bound. So this list does not add a feature so much as replace an input to one that
    /// already worked (B327).
    ///
    /// **53 of the 30,684 materials TF2 ships declare one**, every single one inside a
    /// <c>&gt;=DX90</c> block — so none of them was visible at all until those blocks were read
    /// (B326). The census tripwire is what surfaced it, in the same run.
    /// </remarks>
    public IReadOnlyList<MapTexture?> SelfIllumMasks { get; private init; } = [];

    /// <summary>The proxies each material runs, empty for the great majority that run none.</summary>
    /// <remarks>
    /// **Evaluated per BIND rather than per frame**, which is what the engine does:
    /// <c>IMaterialProxy</c> has <c>Init</c>, <c>OnBind</c> and <c>Release</c> and no tick at all.
    /// So a material drawn twice evaluates twice and one that is off screen evaluates never — the
    /// cost follows what is drawn rather than what the map contains.
    /// </remarks>
    public IReadOnlyList<IReadOnlyList<MaterialProxy>> Proxies { get; }

    /// <summary>The map's texture table, for reflectivity where a texture is missing.</summary>
    public IReadOnlyList<BspMaterial> Materials { get; }

    /// <summary>What this map asked for that is not implemented, and how many materials want it.</summary>
    /// <remarks>
    /// **Covers brushwork AND props**, which is the distinction B81 was: the census reported clean
    /// for months while never examining a prop material. Exposed so a test can assert the set rather
    /// than a person noticing a log line — the failure that cost B55 an hour and B83 four
    /// hypotheses was never missing information, it was information nothing acted on.
    /// </remarks>
    public IReadOnlyDictionary<string, int> UnimplementedParameters { get; private init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Shaders this map names that this project does not reproduce.</summary>
    public IReadOnlyCollection<string> UnimplementedShaders { get; private init; } = [];

    /// <summary>How many of <see cref="Materials"/> came from the map's own brushwork.</summary>
    /// <remarks>Everything past this index is a prop or model material, appended after them.</remarks>
    public int BrushMaterialCount { get; private init; }

    /// <summary>Placements whose baked lighting existed and this project would not apply.</summary>
    /// <remarks>
    /// **Empty is the only acceptable state, and a test says so rather than a log.** A refusal means
    /// a map shipped baked lighting for a prop and this project declined it, so the prop draws with
    /// white vertex colours and is indistinguishable from one the compiler never lit.
    ///
    /// **On the result rather than in a static, and that was a real bug.** The first version was a
    /// static on <c>PropModels</c> written by every load. The full-suite run has these fixtures in
    /// parallel, so the value belonged to whichever map finished last and a test asserting on it
    /// was reading another test's map — passing alone and failing in the gate, which is the shape
    /// of every shared-mutable-state defect.
    /// </remarks>
    public IReadOnlyList<string> RefusedPropLighting { get; private init; } = [];

    /// <summary>Every face's baked lighting, packed into one image.</summary>
    public LightmapAtlas Lightmaps { get; }

    /// <summary>How many materials resolved to a texture.</summary>
    public int Resolved { get; }

    /// <summary>How many did not.</summary>
    public int Missing { get; }

    /// <summary>Loads a map's textures and lighting.</summary>
    /// <param name="map">The map's bytes.</param>
    /// <param name="archives">The game's archives.</param>
    /// <param name="entityModels">Model paths the demo uses, loaded with the map so the textures upload once.</param>
    /// <param name="wornModels">Of those, the ones bone-merged onto another entity, which must be skinned.</param>
    /// <param name="maximumTextureSize">Largest texture edge to decode; zero for full size.</param>
    /// <param name="brushModels">
    /// The map's own brush entities, keyed <c>*N</c>, already built from its models lump. Passed
    /// in rather than read here because they are cut from the same surface list the world is built
    /// from, and reading that list twice is the expensive half of loading a map.
    /// </param>
    /// <param name="lightAt">
    /// The light reaching a point, for props whose baked vertex lighting is absent or refused. The
    /// engine lights those from the light cache rather than leaving them unlit (B123); passed in
    /// because the caller reads the leaves and ambient samples before any asset is loaded.
    /// </param>
    /// <param name="loggers">Where loading reports what it could not use, or null for nowhere.</param>
    /// <returns>The assets.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidDataException">The map's lumps are malformed.</exception>
    public static MapAssets Load(
        ReadOnlyMemory<byte> map,
        GameArchives archives,
        int maximumTextureSize,
        IReadOnlyCollection<string>? entityModels = null,
        IReadOnlyCollection<string>? wornModels = null,
        Func<LightmapAtlas, IReadOnlyDictionary<string, PropModels.ModelFrames>>? brushModels = null,
        Func<float, float, float, PointLighting>? lightAt = null,
        ILoggerFactory? loggers = null)
    {
        ArgumentNullException.ThrowIfNull(archives);

        // **A factory rather than a logger, because this reaches into PropModels and EntityModels**
        // and both report under their own areas (D83). Optional with a null-object default: most
        // callers of this are tests that want the assets and not the commentary.
        ILoggerFactory factory = loggers ?? NullLoggerFactory.Instance;
        ILogger assets = factory.CreateLogger("assets");

        // **Packed here rather than at the constructor call, because the brush entities need it.**
        // A door's faces are lit by vrad exactly as the world's are (vrad.cpp:703) and their samples
        // sit in this same atlas, so the geometry cannot be built until the atlas exists — which is
        // why `brushModels` is a factory taking one rather than a finished dictionary (B131).
        LightmapAtlas lightmaps = PackLighting(assets, map);

        PakFile pak = PakFile.ReadFrom(map);
        List<BspMaterial> materials = [.. BspMaterials.Read(map)];

        // **One table rather than six parallel lists**, because they kept getting out of step. A
        // prop's materials continue this same table, and PropModels appended to three of the six —
        // so every model material lost its detail texture, bump map, cubemap and proxies, padded
        // afterwards with nulls. See MaterialTable.
        MaterialTable table = new();
        int resolved = 0;
        int missing = 0;

        IDisposable materialTiming = assets.Time("resolving materials");

        // What the map asked for that this project does not implement, accumulated across both
        // sources so a test can assert the whole picture rather than reading two log lines.
        Dictionary<string, int> census = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> shaderCensus = new(StringComparer.OrdinalIgnoreCase);

        // **Resolved in parallel, written by index.** Each material is an independent chain of
        // VMT, patch and VTF, and both content sources are read-only once opened: VpkArchive opens
        // a fresh stream per read and PakFile reads an in-memory buffer, so neither has shared
        // mutable state.
        //
        // Index-addressed rather than appended, because the material table's ORDER is load-bearing
        // - every face in the map indexes into it. Appending from several threads would shuffle it
        // and repaint the map with the wrong textures, differently on each run.
        ResolvedMaterial[] found = new ResolvedMaterial[materials.Count];

        Parallel.For(0, materials.Count, index =>
            found[index] = Resolve(assets, materials[index].Name, pak, archives, maximumTextureSize));

        for (int index = 0; index < found.Length; index++)
        {
            ResolvedMaterial material = found[index];

            table.Add(materials[index], material);

            if (material.Texture is null)
            {
                missing++;
            }
            else
            {
                resolved++;
            }
        }

        // **What the map asked for that this renderer does not do.** Logged unconditionally,
        // because the alternative was measured: every material on cp_process resolved, so the log
        // stayed silent while every control point drew as a black disc, and the one gap that
        // mattered - $envmap on 43 of 189 materials, B55 - took an hour of throwaway probes.
        //
        // A report built only from failures reads clean while every instance quietly falls back.
        // **And the SHADERS, which the census never counted.** A material's shader decides what its
        // parameters mean, so a shader this project does not reproduce is a bigger gap than any
        // single parameter — and it hides better, because it need not declare anything unfamiliar.
        // Modulate is the case that proved it: it multiplies the framebuffer purely by being
        // Modulate, passed the parameter census in silence, and drew every capture point as a dark
        // slab until someone stood in front of one.
        //
        // Both halves are reported per SOURCE — brushwork, then props and models — because they are
        // drawn by different paths and a combined number would hide which of them is missing what.
        void ReportCensus(string source, IReadOnlyCollection<ResolvedMaterial> resolved)
        {
            IReadOnlyList<(string Parameter, int Materials)> parameters = MaterialCensus.Unimplemented(
                resolved.Select(material => material.Declared ?? []));

            // **Kept, not just printed.** The census answered this question correctly for months
            // and the answer only ever reached a log, so B55 spent an hour rediscovering it and
            // B83 four hypotheses. A number a test can assert is a different thing from a number
            // someone might read.
            foreach ((string parameter, int count) in parameters)
            {
                census[parameter] = census.GetValueOrDefault(parameter) + count;
            }

            foreach (string shader in MaterialCensus
                .UnimplementedShaders(resolved.Select(material => material.Shader))
                .Select(entry => entry.Shader))
            {
                shaderCensus.Add(shader);
            }

            assets.LogInformation(
                "{Message}",
                parameters.Count == 0
                    ? $"every parameter the {source} materials declare is implemented"
                    : $"{parameters.Count} unimplemented parameters across {resolved.Count} " +
                        $"{source} materials: " +
                        string.Join(", ", parameters.Select(entry => $"{entry.Parameter} x{entry.Materials}")));

            IReadOnlyList<(string Shader, int Materials)> shaders = MaterialCensus.UnimplementedShaders(
                resolved.Select(material => material.Shader));

            assets.LogInformation(
                "{Message}",
                shaders.Count == 0
                    ? $"every shader the {source} materials name is implemented"
                    : $"{shaders.Count} unimplemented shaders across {resolved.Count} " +
                        $"{source} materials: " +
                        string.Join(", ", shaders.Select(entry => $"{entry.Shader} x{entry.Materials}")));
        }
        ReportCensus("brushwork", found);

        // **Props after the brushwork, deliberately.** They extend the same material table, so
        // every index the BSP already handed out keeps its meaning and the new ones continue from
        // the end. Inserting them first would renumber every face in the map.
        int brushMaterials = materials.Count;

        materialTiming.Dispose();

        // **Every material a prop or a model resolves, gathered for the census.** The census above
        // covers the BRUSHWORK only, because props register their materials afterwards through
        // their own path — and the two shaders that cost this project a session and a half,
        // Modulate and UnLitTwoTexture, are prop materials on cappoint_hologram.mdl. The report
        // therefore read clean while a capture point drew as a dark slab, which is the one failure
        // a census exists to prevent (B81).
        //
        // Collected in the closure rather than by threading a list through five signatures: the
        // resolver already returns the shader and the declared keys, and this is the one place both
        // prop paths pass through.
        List<ResolvedMaterial> propMaterials = [];

        ResolvedMaterial? ResolveProp(string path)
        {
            ResolvedMaterial? resolved = Resolve(assets, path, pak, archives, maximumTextureSize, report: false);

            if (resolved is { } material)
            {
                propMaterials.Add(material);
            }

            return resolved;
        }

        IDisposable propTiming = assets.Time("loading props");

        // **Carried on the result rather than in a static**, which is the second time that
        // distinction has bitten in this file. A static written by every map load is meaningless
        // the moment two loads overlap: the full suite runs these fixtures in parallel and the
        // value belonged to whichever load finished last, so a test asserting on it was reading
        // another test's map. Found by the gate rather than by any individual run, which is what a
        // full-suite run is for.
        List<string> refusedLighting = [];

        // **The logger, which this call omitted from the day it was written (B229).** It was the
        // only caller of an `ILogger? props = null` overload, so the entire static-prop path — four
        // categories of finding, every refused lighting file by name, and the two warnings that
        // name a model whose mesh cannot resolve a material — went to a `NullLogger`. The same
        // area is handed to `LoadFrames` twenty lines below for entity models, which is why the
        // viewer log looked populated while the half being investigated was silent.
        IReadOnlyList<PropVertex> props = PropModels.Load(
            factory.CreateLogger("props"),
            map,
            pak,
            archives,
            table,
            ResolveProp,
            refusedLighting,
            lightAt);

        propTiming.Dispose();

        // **Entity models are loaded here, with the map's own props, and that is the point.**
        // Their materials go into the same table, so the textures upload once with everything in
        // them. Loading a model during playback instead would mean growing the texture array
        // mid-match and re-uploading it, which is a hitch exactly where the viewer is trying to
        // look smooth.
        //
        // Every model the demo uses is already known: the timeline is built before anything is
        // drawn, which is the same trade this project makes everywhere - know it all up front,
        // and playback costs nothing. TF2 launches a listen server to play a demo, so the budget
        // here is generous.
        Dictionary<string, PropModels.ModelFrames> models = new(StringComparer.OrdinalIgnoreCase);

        // **The map's own brush entities, which have no file to load.** A door is `*12`, a run of
        // faces in this same BSP, so its geometry is built from the map rather than resolved
        // through the archives — but once built it is a model like any other, and joins the table
        // the entity path already looks in. That is the whole of the wiring: no second lookup, no
        // second draw path.
        //
        // Added before the studio loop rather than after, so a demo that somehow names `*12` as a
        // model path cannot have a failed file load overwrite real geometry with an empty entry.
        if (brushModels?.Invoke(lightmaps) is { Count: > 0 } brushes)
        {
            foreach ((string name, PropModels.ModelFrames geometry) in brushes)
            {
                models[name] = geometry;
            }

            assets.LogInformation(
                "{Message}",
                $"{brushes.Count} brush entities built from the map's models lump");
        }

        if (entityModels is { Count: > 0 })
        {
            using IDisposable modelTiming = assets.Time("loading entity models");

            int loaded = 0;

            // **Loaded lazily, and only if something is actually missing.** A map where every model
            // resolves should not pay for reading one it will never draw, and an install that lacks
            // error.mdl should not have that reported until the moment it would have mattered.
            PropModels.ModelFrames? error = null;
            bool triedError = false;

            foreach (string path in entityModels)
            {
                PropModels.ModelFrames? frames = PropModels.LoadFrames(
                    factory.CreateLogger("props"),
                    path,
                    pak,
                    archives,
                    table,
                    ResolveProp,

                    // **Worn models are skinned regardless of how cheap they are.** A bone-merged
                    // item has no transform of its own; it is placed entirely by its wearer's
                    // skeleton, so baking away its bones leaves nothing to hang it from.
                    mustSkin: wornModels?.Contains(path) == true);

                if (frames is { Geometry.Count: > 0 } && frames.Geometry[0].Count > 0)
                {
                    models[path] = frames;
                    loaded++;
                    continue;
                }

                // **Valve substitutes a model rather than drawing nothing, and so does this now.**
                // `game/server/props.cpp:245` does `SetModelName( AllocPooledString(
                // "models/error.mdl" ) )` when a prop's model is missing, and
                // `detailobjectsystem.cpp:1603` loads the same. The asymmetry against a missing
                // MATERIAL is deliberate on Valve's part: an unresolved material has a surface to
                // put a chequer on, and a model that failed to load has no surface at all, so the
                // only way to report it is to put something there.
                //
                // Drawing nothing is the failure mode this project already has a memory about — a
                // hole reads as art direction and nobody investigates it, while something wrong and
                // loud gets reported. Until now the count was logged and the screen said nothing.
                if (!triedError)
                {
                    triedError = true;

                    error = PropModels.LoadFrames(
                        factory.CreateLogger("props"),
                        ErrorModel, pak, archives, table, ResolveProp, mustSkin: false);

                    if (error is not { Geometry.Count: > 0 })
                    {
                        error = null;

                        assets.LogWarning(
                            "{Message}",
                            $"{ErrorModel} did not load either, so a missing model draws nothing");
                    }
                }

                if (error is { } stand)
                {
                    models[path] = stand;

                    assets.LogWarning(
                        "{Model} did not load; drawing Valve's error model in its place", path);
                }
            }

            // Four categories again. "N of M loaded" answers HAVE and nothing else — it does not
            // name which of the M are missing, and an entity model that fails to load is a player
            // or a weapon that simply is not drawn, which looks like the demo not containing one.
            assets.LogInformation(
                "{Message}",
                $"ASKED FOR {entityModels.Count} entity models; HAVE {loaded}; " +
                $"MISSING {entityModels.Count - loaded}");

            foreach (string absent in entityModels.Where(path => !models.ContainsKey(path)))
            {
                assets.LogInformation("entity model not loaded: {Model}", absent);
            }
        }

        // **The two whole-model overrides, appended after everything that indexes the table** — a
        // corpse's gold and ice, which no map and no model names, so nothing above would ever pull
        // them in (B325).
        Dictionary<string, int> overrides =
            LoadOverrideMaterials(assets, table, pak, archives, maximumTextureSize);

        // **And now the props and models, which the census could not see (B81).** Reported
        // separately from the brushwork rather than merged into it: the two are drawn by different
        // paths and a gap in one says nothing about the other, so a combined number would hide
        // which half is missing what.
        ReportCensus("prop and model", propMaterials);

        // **Five padding loops used to stand here and they were the bug.** Each filled a list with
        // nulls up to the texture count, because prop materials were appended to three lists and
        // not the rest — so the padding was not padding, it was every model material's detail,
        // bump, cubemap and proxies being thrown away and replaced with nothing. The comments even
        // said so: "prop materials are appended after the brushwork, so their detail slots have to
        // be too".
        //
        // MaterialTable.Add appends all seven at once, so there is nothing left to pad and no way
        // for a caller to create the gap again.

        // **One inventory line covering all four questions**, because the individual counts below
        // each answer a different one and none of them says whether the whole stage worked. The
        // shape is deliberate and standing: ASKED FOR / HAVE / PRODUCED / MISSING. A log reporting
        // only what failed reads clean while every material quietly falls back to its base texture,
        // which is how 42 of 189 materials declaring an unimplemented $envmap went unnoticed for an
        // hour (B55), and how four refused prop lighting files hid inside an ordinary total (B83).
        int textured = table.Textures.Count(texture => texture is not null);

        // **Counted apart, so the MISSING figure means "broken"** (B62). A `Water` material has no
        // base texture and never should; folding it into the missing count makes a healthy map read
        // as a faulty one, and leaves the count disagreeing with the named list below it — which is
        // an instrument contradicting itself, the fault this same line was just fixed for.
        int water = 0;

        for (int index = 0; index < table.Count; index++)
        {
            if (table.Textures[index] is null &&
                table.Shaders[index].Equals("Water", StringComparison.OrdinalIgnoreCase))
            {
                water++;
            }
        }

        string byDesign = water switch
        {
            0 => string.Empty,
            1 => ", and 1 Water material that declares none by design",
            _ => $", and {water} Water materials that declare none by design",
        };

        assets.LogInformation(
            "{Message}",
            $"ASKED FOR {table.Count} materials ({brushMaterials} the map's own, " +
            $"{table.Count - brushMaterials} from props); " +
            $"HAVE {textured} with a base texture; " +
            $"PRODUCED {table.Details.Count(detail => detail is not null)} with a detail texture, " +
            $"{table.Bumps.Count(bump => bump is not null)} with a bump map; " +
            $"MISSING {table.Count - textured - water} with no base texture resolved" + byDesign);

        // **NAMES the ones that failed, because the count alone cost an hour.** The inventory above
        // said "MISSING 1" and the renderer said "1 will draw as the missing-material chequer at
        // 377"; between them they gave a number, an index and no way to reach the material — so the
        // only route to it was to guess from a warning that had stopped being printed. A count says
        // a thing is wrong and a name says what to open.
        //
        // Unbounded on purpose: a map with forty broken materials wants forty lines, and capping a
        // diagnostic by a report count is the rule this project already keeps.
        if (table.Count != textured)
        {
            for (int index = 0; index < table.Count; index++)
            {
                // **`Water` is exempt, because it declares no `$basetexture` by design** (B62).
                // Reporting it as missing is the same false claim the chequer made, one layer up:
                // an inventory that names a healthy material as broken teaches a reader to
                // disbelieve the line.
                if (table.Textures[index] is null &&
                    !table.Shaders[index].Equals("Water", StringComparison.OrdinalIgnoreCase))
                {
                    assets.LogWarning(
                        "{Message}",
                        $"material {index} '{table.Materials[index].Name}' resolved no base " +
                        "texture, so it will draw as the missing-material chequer");
                }
            }
        }

        // **Measured rather than assumed.** A detail chain that loads nothing still draws a
        // perfectly reasonable map, so the count is the only thing that says it is working.
        assets.LogInformation(
            "{Count} materials carry a detail texture",
            table.Details.Count(detail => detail is not null));

        // **Measured, not assumed.** A bump chain that resolves nothing still draws a perfectly
        // reasonable map, because every bumped face already has a correct flat lightmap.
        assets.LogInformation(
            "{Message}",
            $"{table.Bumps.Count(bump => bump is not null)} materials carry a bump map, " +
            $"{table.Bumps.Count(bump => bump is { IsSelfShadowing: true })} of them self-shadowing");

        // **Measured, not assumed**, for the same reason as the detail and bump lines above: a
        // cubemap chain that resolves nothing still draws a perfectly reasonable map, just a matte
        // one — which is the state this has been in since the project started (B55).
        assets.LogInformation(
            "{Count} materials carry a baked cubemap",
            table.Cubemaps.Count(cubemap => cubemap is not null));

        // **The model half, reported separately because it fails separately.** A material asking for
        // the literal `env_cubemap` has no cubemap of its own and takes one of the map's placements
        // by where it stands, so it is absent from the count above however well it is working. The
        // line above read 123 on cp_badlands while every reflective PROP on the map — the capture
        // points included — silently reflected nothing, and no number said so.
        assets.LogInformation(
            "{Message}",
            $"{table.LocalReflections.Count(shading => shading is not null)} materials reflect the " +
            "map's own cubemap, chosen per draw by position");

        // **Measured for the same reason, and this is the number that says the entity path works.**
        // Model materials used to arrive with none, because they were appended to three lists and
        // padded into the rest.
        assets.LogInformation(
            "{Count} materials run a proxy",
            table.Proxies.Count(list => list.Count > 0));

        return new MapAssets(
            table.Textures,
            table.BlendTextures,
            table.Details,
            table.Bumps,
            table.Cubemaps,
            table.Proxies,
            table.Materials,
            table.Shaders,
            lightmaps,
            props,
            resolved,
            missing)
        {
            OverrideMaterials = overrides,
            EntityModels = models,
            UnimplementedParameters = census,
            UnimplementedShaders = shaderCensus,
            BrushMaterialCount = brushMaterials,
            RefusedPropLighting = refusedLighting,
            LocalReflections = table.LocalReflections,
            PlacedCubemaps = LoadPlacedCubemaps(assets, map, pak, maximumTextureSize),
            Phong = table.Phong,
            LightWarps = table.LightWarps,
            SelfIllumMasks = table.SelfIllumMasks,
            DevGrid = LoadDevGrid(assets, archives, maximumTextureSize),

            // The 2D skybox, from worldspawn's `skyname` — every map has one, because `sv_skyname`
            // carries a default and a map silent on the key inherits it (B303).
            SkyFaces = LoadSky(
                assets,
                archives,
                pak,
                maximumTextureSize,
                BspEntities.SkyName(BspEntities.ReadFrom(map))),

            // Valve's own luxel grid, for mat_luxels. Same loader, different candidates — it ships
            // only in the Half-Life 2 archives, which TF2's gameinfo.txt mounts after its own.
            LuxelGrid = LoadDebugTexture(
                assets,
                archives, maximumTextureSize, "materials/debug/debugluxels.vtf"),
        };
    }

    /// <summary>Valve's measurement grid, for the category view, or null if the game lacks it.</summary>
    /// <remarks>
    /// **Valve's own texture rather than one generated here**, on the owner's direction: "if our
    /// placeholders match valves, and our colors match valves then things become easily compared and
    /// you only have one legend to remember". A capture from this viewer and a shot of the same
    /// place in Hammer or in the game's dev mode then read the same way.
    ///
    /// **Candidates in order, and the order is deliberate.** `dev_measuregeneric01` is the classic
    /// orange-and-grey grid and ships with Half-Life 2, whose archives TF2's own `gameinfo.txt`
    /// mounts after its own — so it is reachable without TF2 shipping it. TF2 ships team-coloured
    /// variants, which are the fallback: they carry the same printed dimensions and differ only in
    /// hue, and the hue is overwritten by the category tint anyway.
    ///
    /// Null when none of them resolve, which the renderer treats as "flat colours, as before". A
    /// missing debug texture must not stop a map drawing.
    /// </remarks>
    /// <summary>The map's 2D skybox, six faces in <c>SkyboxGeometry</c>'s order.</summary>
    /// <remarks>
    /// **Loaded apart from the map's material table because a sky material is not IN it.** The
    /// BSP's texdata names what the brushes are textured with — sky brushes carry
    /// <c>tools/toolsskybox</c> — and the sky itself comes from <c>worldspawn</c>'s <c>skyname</c>,
    /// which is a keyvalue rather than a surface.
    ///
    /// **Through the VMT, not by guessing the VTF's name.** `sky_harvest_01up` declares
    /// <c>$basetexture "skybox/sky_harvest_01_up"</c> — an extra underscore the material name does
    /// not have — and its four sides all declare the SAME texture, `sky_harvest_01_side`. A loader
    /// that appended the suffix to a texture path would miss every one of them.
    /// </remarks>
    public IReadOnlyList<MapTexture?> SkyFaces { get; private init; } = [];

    /// <summary>Reads the six sky faces, or an empty list when the map's sky will not load.</summary>
    /// <remarks>
    /// **All six or none.** A box missing a face shows the clear colour through a hole, which reads
    /// as a rendering fault rather than as a missing asset — and a partial sky is not something the
    /// engine can produce.
    /// </remarks>
    private static MapTexture?[] LoadSky(
        ILogger assets,
        GameArchives archives,
        PakFile pak,
        int maximumTextureSize,
        string skyName)
    {
        string[] materials = BspEntities.SkyFaces(skyName);
        MapTexture?[] faces = new MapTexture?[materials.Length];

        for (int face = 0; face < materials.Length; face++)
        {
            byte[]? vmt;

            try
            {
                // **The map's OWN archive first, because a community map ships its own sky.**
                // `cp_fulgur` names `sky_island_02`, which is not in TF2's content at all — it is
                // packed into the BSP. Reading only the game's VPKs answered "no sky" for it, and
                // the all-six-or-none rule then drew nothing: a correct refusal to a question asked
                // in the wrong place.
                vmt = pak.ReadFile($"materials/{materials[face]}.vmt")
                    ?? archives.Read($"materials/{materials[face]}.vmt");
            }
            catch (Exception failure) when (failure is IOException or InvalidDataException)
            {
                assets.LogWarning(failure, "reading the sky material {Name}", materials[face]);
                return [];
            }

            if (vmt is null || VmtMaterial.Parse(vmt).PrimaryTexture is not { } texture)
            {
                assets.LogWarning(
                    "the sky material {Name} is missing or names no texture", materials[face]);

                return [];
            }

            // **Both archives again, and for the same map.** A community sky's VMT and its VTF are
            // packed together, so finding the material in the pak and then looking for its texture
            // only in the game's VPKs would fail on the second half of every custom sky.
            faces[face] = LoadPackedTexture(
                assets, archives, pak, maximumTextureSize, $"materials/{texture}.vtf");

            if (faces[face] is null)
            {
                assets.LogWarning(
                    "the sky texture {Texture} for {Name} would not load",
                    texture,
                    materials[face]);

                return [];
            }
        }

        // **Said on SUCCESS, because a silent success and a call that never happened look the
        // same** — which is exactly how the first run of this was read. The sizes are here because
        // they are what says the six are DIFFERENT images: `sky_harvest_01` shares one texture
        // across all four sides and uses a 1x1 for its floor, so a face-order fault is invisible on
        // that map and would need a sky with four distinct sides to show.
        assets.LogInformation(
            "2D skybox '{Sky}': {Faces} faces, {Sizes}",
            skyName,
            faces.Length,
            string.Join(
                ", ",
                Array.ConvertAll(
                    faces,
                    face => face is { } present
                        ? $"{present.Width}x{present.Height}"
                        : "missing")));

        return faces;
    }

    private static MapTexture? LoadDevGrid(
        ILogger assets, GameArchives archives, int maximumTextureSize) =>
        LoadDebugTexture(
            assets,
            archives,
            maximumTextureSize,
            "materials/dev/dev_measuregeneric01.vtf",
            "materials/dev/dev_measuregeneric01blu.vtf",
            "materials/dev/dev_measurewall01blu.vtf");

    /// <summary>Loads the first of Valve's debug textures that resolves, or null.</summary>
    /// <remarks>
    /// **Absent is not a failure.** These are editor and developer assets: a game install has them,
    /// a content-only or dedicated-server copy may not. Losing one costs a diagnostic view, so it
    /// must not interrupt opening a demo — but a file that exists and will not decode is a different
    /// thing and says so.
    /// </remarks>
    private static MapTexture? LoadDebugTexture(
        ILogger assets, GameArchives archives, int maximumTextureSize, params string[] candidates)
    {
        foreach (string name in candidates)
        {
            byte[]? file;

            try
            {
                file = archives.Read(name);
            }
            catch (Exception failure) when (failure is IOException or InvalidDataException)
            {
                assets.LogWarning(failure, "reading {Name}", name);
                continue;
            }

            if (file is null)
            {
                continue;
            }

            try
            {
                VtfTexture decoded = VtfTexture.Read(file, maximumTextureSize);

                assets.LogInformation(
                    "{Message}",
                    $"debug texture {name} ({decoded.Width}x{decoded.Height} {decoded.Format})");

                return new MapTexture(decoded.Width, decoded.Height, decoded.Image, false);
            }
            catch (InvalidDataException failure)
            {
                assets.LogWarning(failure, "decoding {Name}", name);
            }
        }

        assets.LogWarning(
            "{Message}",
            $"none of {candidates.Length} debug textures resolved ({string.Join(", ", candidates)}); " +
            "the view that uses it falls back");

        return null;
    }

    /// <summary>Reads one texture from the map's own archive first, then the game's.</summary>
    /// <remarks>
    /// **The order matters and it is the game's own.** `gameinfo.txt` mounts the map's pakfile
    /// ahead of the VPKs, which is what lets a community map override a stock asset — and what lets
    /// it ship a sky the game has never heard of. `cp_fulgur` names `sky_island_02`, absent from
    /// TF2's content entirely.
    /// </remarks>
    private static MapTexture? LoadPackedTexture(
        ILogger assets, GameArchives archives, PakFile pak, int maximumTextureSize, string path)
    {
        byte[]? file;

        try
        {
            file = pak.ReadFile(path) ?? archives.Read(path);
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException)
        {
            assets.LogWarning(failure, "reading {Name}", path);
            return null;
        }

        if (file is null)
        {
            return null;
        }

        try
        {
            VtfTexture decoded = VtfTexture.Read(file, maximumTextureSize);

            return new MapTexture(decoded.Width, decoded.Height, decoded.Image, false);
        }
        catch (InvalidDataException failure)
        {
            assets.LogWarning(failure, "decoding {Name}", path);

            return null;
        }
    }

    private static LightmapAtlas PackLighting(ILogger assets, ReadOnlyMemory<byte> map)
    {
        using (assets.Time("reading and packing lightmaps"))
        {
            return LightmapAtlas.PackAll(BspLightmaps.ReadAll(map));
        }
    }

    /// <summary>Follows a material to its texture.</summary>
    /// <remarks>
    /// The chain is VMT, then a patch's included VMT if there is one, then the VTF. Any step
    /// failing yields null, because a half-resolved material has nothing to draw.
    /// </remarks>
    /// <summary>The six cube directions of an <c>$envmap</c> VTF, decoded.</summary>
    /// <param name="file">The VTF's bytes.</param>
    /// <param name="maximumTextureSize">Largest edge to decode; zero for full size.</param>
    /// <returns>Six faces in Valve's order.</returns>
    /// <remarks>
    /// **Six of the seven, in file order.** Valve's face names read RIGHT/LEFT/BACK/FRONT/UP/DOWN
    /// and are misleading; <c>LookDir_t</c> declared beside them gives the real order as
    /// <c>+X, -X, +Y, -Y, +Z, -Z</c>, which is D3D's <c>TextureCube</c> order exactly. The seventh
    /// is a fallback spheremap — a different projection, not a seventh direction — and is dropped.
    ///
    /// One routine rather than two, because a material's own cubemap and one of the map's
    /// placements are the same file format read for different reasons, and a face order that
    /// drifted between them would put a reflection on backwards for props alone.
    /// </remarks>
    private static List<MapTexture> CubeFaces(byte[] file, int maximumTextureSize)
    {
        List<MapTexture> faces = new(6);

        for (int face = 0; face < 6; face++)
        {
            VtfTexture decoded = VtfTexture.Read(file, maximumTextureSize, face);

            faces.Add(new MapTexture(
                decoded.Width, decoded.Height, decoded.Image, IsTransparent: false));
        }

        return faces;
    }

    /// <summary>Every cubemap the map baked, decoded and placed.</summary>
    /// <param name="assets">Where a refusal is reported; a parameter because this is static (D83).</param>
    /// <param name="map">The map's bytes.</param>
    /// <param name="pak">The map's own archive, where vbsp wrote the bakes.</param>
    /// <param name="maximumTextureSize">Largest edge to decode; zero for full size.</param>
    /// <returns>The placements that decoded, in lump order minus any that did not.</returns>
    /// <remarks>
    /// **This is the half of <c>$envmap</c> that a brush face never needs.** vbsp chose each brush
    /// face's cubemap at compile time and wrote the texture's name into a patched material, so the
    /// world path resolves a name and never asks where anything is. A model's material still says
    /// the literal <c>env_cubemap</c>, so the choice happens at draw time from where the model
    /// stands — which needs the positions, and therefore this.
    ///
    /// **Every placement is decoded, not only the ones something reflects.** Which cubemap a model
    /// takes depends on where it stands, and entities move; picking a subset at load would mean
    /// deciding in advance which parts of the map anything will ever walk into. Measured on
    /// cp_process_final: 43 placements at 32 pixels a face is about a megabyte, against a lightmap
    /// atlas of 2048×3485.
    /// </remarks>
    // The logger is a parameter because this is static (D83).
    private static List<MapPlacedCubemap> LoadPlacedCubemaps(
        ILogger assets,
        ReadOnlyMemory<byte> map,
        PakFile pak,
        int maximumTextureSize)
    {
        IReadOnlyList<BspCubemap> placements = BspCubemaps.Read(map);

        if (placements.Count == 0)
        {
            return [];
        }

        // **The map's name, taken from the archive rather than from a filename**, because that is
        // where the answer has to agree. vbsp wrote `maps/<name>/c<x>_<y>_<z>.vtf` into this pak
        // using the name it compiled under, and a caller's idea of the map's name — from a path, a
        // download, a rename — need not match it. Reading it back from a packed path cannot drift.
        if (MapNameIn(pak) is not { } mapName)
        {
            assets.LogWarning(
                "{Message}",
                $"the map bakes {placements.Count} cubemaps but packs no maps/<name>/ path to " +
                "name them from, so no model will reflect");

            return [];
        }

        List<MapPlacedCubemap> loaded = new(placements.Count);
        int absent = 0;
        int refused = 0;

        foreach (BspCubemap placement in placements)
        {
            string path = "materials/" + BspCubemaps.TextureName(mapName, placement) + ".vtf";

            if (pak.ReadFile(path) is not { } file)
            {
                // **Expected on some maps rather than a defect.** `Cubemap_AddUnreferencedCubemaps`
                // keeps an env_cubemap entity in the lump even when nothing reflects it, and a map
                // shipped without a cubemap build has the lump and none of the bakes.
                absent++;
                continue;
            }

            try
            {
                loaded.Add(new MapPlacedCubemap(placement, CubeFaces(file, maximumTextureSize)));
            }
            catch (Exception failure) when (failure is InvalidDataException or ArgumentOutOfRangeException)
            {
                // One placement that will not decode costs the models near it their reflection and
                // nothing else. Counted rather than logged per file: a map missing its cubemap
                // build would otherwise print hundreds of identical lines.
                refused++;
            }
        }

        assets.LogInformation(
            "{Message}",
            $"ASKED FOR {placements.Count} baked cubemaps of {mapName}; " +
            $"HAVE {loaded.Count} decoded; " +
            $"MISSING {absent} unpacked, {refused} that would not decode");

        return loaded;
    }

    /// <summary>The name vbsp compiled a map under, read back from what it packed.</summary>
    /// <remarks>
    /// Every path vbsp writes into the pakfile for a cubemap or a patched material begins
    /// <c>materials/maps/&lt;name&gt;/</c> (<c>vbsp/cubemap.cpp:511</c>), lowercased. Taking the
    /// name from there rather than from the file on disk means the two cannot disagree.
    /// </remarks>
    private static string? MapNameIn(PakFile pak)
    {
        const string prefix = "materials/maps/";

        foreach (string path in pak.Paths)
        {
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int end = path.IndexOf('/', prefix.Length);

            if (end > prefix.Length)
            {
                return path[prefix.Length..end];
            }
        }

        return null;
    }

    private static ResolvedMaterial Resolve(
        ILogger assets,
        string materialName,
        PakFile pak,
        GameArchives archives,
        int maximumTextureSize,
        bool report = true)
    {
        byte[]? Find(string path)
        {
            try
            {
                return pak.ReadFile(path) ?? archives.Read(path);
            }
            catch (Exception failure) when (failure is IOException or InvalidDataException)
            {
                // **Reported rather than swallowed.** An unreadable archive entry is a defect in
                // this reader until shown otherwise; the engine opens all of these.
                assets.LogWarning(failure, "reading {Path}", path);

                return null;
            }
        }

        // **A texdata name may already carry `.vmt`, and appending a second one asks for a path that
        // cannot exist.** `cp_fulgur` stores `water/water_well_beneath.vmt`, so the lookup below
        // would ask an archive for `materials/water/water_well_beneath.vmt.vmt` — no archive
        // contains that, whatever the engine does internally. The bug is in the path this builds and
        // needs no appeal to Valve to see.
        //
        // **The engine additionally NORMALISES such a name rather than erroring**, which is a
        // separate fact and is measured rather than read: `IMaterialSystem::FindMaterial`'s comment
        // says the name is *"a full path to the vmt file ... without a file extension"* and the
        // SDK's tools pass the texdata string straight through (`utilmatlib.cpp:75`), so the header
        // suggests the map is malformed. It is not — the owner, on this map: *"the real tf2 doesnt
        // show the purple and black texture anywhere on this map, its not a new map"*. The material
        // system is closed, so the running game is the only source that could settle that half.
        string name = materialName.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase)
            ? materialName[..^4]
            : materialName;

        if (Find("materials/" + name + ".vmt") is not { } vmt)
        {
            // **Silent only when the caller is guessing.** A model's material can be reached by
            // several candidate paths and all but one are expected to miss; reporting each would
            // bury the real failures, which the caller logs once it has run out of candidates.
            if (report)
            {
                assets.LogWarning("material materials/{Material}.vmt was not found", materialName);
            }

            return default;
        }

        VmtMaterial material;

        try
        {
            material = VmtMaterial.Parse(vmt);

            if (material.IsPatch && material.Include is { } include && Find(include) is { } based)
            {
                material = VmtMaterial.ApplyPatch(material, VmtMaterial.Parse(based));
            }
        }
        catch (InvalidDataException failure)
        {
            assets.LogWarning(failure, "parsing materials/{Material}.vmt", materialName);

            return default;
        }

        // **PrimaryTexture, not BaseTexture**, because a material need not have a base one. TF2's
        // eyes use EyeRefract, which names an iris, a cornea and an occlusion map and no
        // $basetexture at all - so asking for the base drew the missing-texture chequer on every
        // player's eyes while the material itself resolved perfectly (B62).
        MapTexture? first = Decode(
            material.PrimaryTexture, material.IsAlphaTested, material.IsAdditive);

        // **Two shaders reach this slot and they combine differently.** A WorldVertexTransition
        // names $basetexture2 and MIXES by vertex alpha; UnLitTwoTexture names $texture2 and
        // MULTIPLIES. Both are "the material's second texture", so they share the slot, and the
        // material carries which operation to use — a capture point's beam is stripes times a
        // colour, and mixed by alpha instead it is whichever the vertices happen to ask for.
        MapTexture? second = Decode(
            material.Value("$basetexture2") ?? material.SecondTexture,
            material.IsAlphaTested,
            material.IsAdditive);

        // **The parameters carried out alongside the textures**, so the caller can report what
        // the map asked for rather than only what failed. Gathered here because this is the one
        // place the parsed VMT exists; the census itself runs on the single-threaded side.
        return new ResolvedMaterial(
            first,
            second,
            ResolveDetail(),
            ResolveBump(),
            material.Proxies,
            material.Keys,
            material.Shader,
            ResolveCubemap(),
            ResolveLocalReflection(),
            ResolvePhong(),
            ResolveLightWarp(),
            ResolveSelfIllumMask());

        MapTexture? ResolveSelfIllumMask()
        {
            // **Only where the material actually lights itself**, which is the engine's own gate:
            //
            //   bool bHasSelfIllumMask = IS_FLAG_SET( MATERIAL_VAR_SELFILLUM ) &&
            //       (info.m_nSelfIllumMask != -1) && params[info.m_nSelfIllumMask]->IsDefined();
            //
            // `vertexlitgeneric_dx9_helper.cpp:289`. A mask on a material with no `$selfillum` is
            // inert in TF2 and is inert here, so resolving it would load a texture nothing samples.
            if (!material.IsSelfIlluminated || material.SelfIllumMask is not { } name)
            {
                return null;
            }

            if (Load(name) is not { } decoded)
            {
                assets.LogWarning(
                    "{Message}",
                    $"self-illumination mask {name}, named by materials/{materialName}.vmt, " +
                    "could not be read");

                return null;
            }

            return new MapTexture(
                decoded.Width, decoded.Height, decoded.Image, IsTransparent: false);
        }

        MapPhong? ResolvePhong()
        {
            // **A boolean, not a texture**, so there is nothing to fail to load: a material either
            // asks for a highlight or does not. Everything else has a declared default, and two of
            // those defaults matter — the exponent is 5 (broad) and the boost is 1.
            if (!material.HasPhong)
            {
                return null;
            }

            return new MapPhong(
                material.PhongExponent,
                material.PhongBoost,
                material.PhongFresnelRanges,
                material.PhongTint ?? (1f, 1f, 1f),
                material.UsesBaseMapAlphaAsPhongMask,

                // **Only reachable through phong**, which is the engine's dispatch: the rim lives in
                // the Skin shader and VertexLitGeneric routes there on $phong alone. A material with
                // $rimlight and no $phong gets neither, so it is resolved inside this branch.
                material.HasRimLight
                    ? new MapRimLight(material.RimLightExponent, material.RimLightBoost)
                    : null);
        }

        MapEnvmapShading? ResolveLocalReflection()
        {
            // **On a MODEL the literal `env_cubemap` is the request, not a compile leftover**, and
            // the two shaders are where that splits. LightmappedGeneric throws it away outright —
            // "env_cubemap used on world geometry without rebuilding map. . ignoring",
            // lightmappedgeneric_dx9_helper.cpp:83 — so a brush face reflects only what vbsp
            // patched in. VertexLitGeneric carries no such rejection and calls
            // LoadCubeMap( info.m_nEnvmap ) on whatever the material says, where `env_cubemap`
            // resolves to the cubemap the engine has bound as local (BindLocalCubemap,
            // imaterialsystem.h:1200).
            //
            // So this half is the model's, and it carries only the SHADING. Which cube depends on
            // where the model stands and is chosen per draw; see WorldRenderer.DrawModel.
            //
            // vbsp does patch a brush face's material rather than leaving the literal, so a
            // brushwork material reaching here with `env_cubemap` means an unrebuilt map — the
            // exact case the engine warns about. Shading it anyway is closer to the engine than
            // dropping it, because the engine only drops it for the LightmappedGeneric shader.
            return material.WantsMapCubemap ? Shading() : null;
        }

        MapEnvmapShading Shading() =>
            new(
                material.EnvMapTint,
                material.EnvMapContrast,
                material.EnvMapSaturation,
                material.UsesBaseAlphaAsEnvMapMask,
                Fresnel(),
                material.UsesNormalMapAlphaAsEnvMapMask);

        float Fresnel()
        {
            // **The shader decides, and the two disagree.** LightmappedGeneric computes Schlick and
            // remaps it by $fresnelreflection, which defaults to 1 — "1.0 == mirror, 0.0 == water" —
            // so the term is a constant unless the material asks otherwise. VertexLitGeneric's
            // envmap block has no Fresnel of any kind, so a model reflects at full strength whatever
            // its VMT says; forcing 1 here is what says so, rather than trusting a key nothing reads.
            //
            // Decided by the shader NAME rather than by whether the material came from the map or a
            // model, because that is what the engine dispatches on. A brush face painted with a
            // VertexLitGeneric material gets the model's rule, which is right.
            bool lightmapped =
                material.Shader.StartsWith("LightmappedGeneric", StringComparison.OrdinalIgnoreCase) ||
                material.Shader.StartsWith("WorldVertexTransition", StringComparison.OrdinalIgnoreCase);

            return lightmapped ? material.FresnelReflection : 1f;
        }

        MapCubemap? ResolveCubemap()
        {
            // **A compiled map names a concrete texture here, never `env_cubemap`.** vbsp rewrites
            // the key at compile time for every brush face it binds; a material still carrying the
            // literal was never patched — which on this map is every static prop's material,
            // because Cubemap_CreateTexInfo works on texinfo and a prop has none. Those go to
            // ResolveLocalReflection above and are bound per draw.
            if (material.EnvMap is not { } name || material.WantsMapCubemap)
            {
                return null;
            }

            string bare = name.EndsWith(".vtf", StringComparison.OrdinalIgnoreCase)
                ? name[..^4]
                : name;

            if (Find("materials/" + bare + ".vtf") is not { } file)
            {
                assets.LogWarning(
                    "{Message}",
                    $"cubemap materials/{bare}.vtf, named by materials/{materialName}.vmt, was not found");

                return null;
            }

            try
            {
                return new MapCubemap(CubeFaces(file, maximumTextureSize), Shading());
            }
            catch (InvalidDataException failure)
            {
                // **The surface survives this.** A cubemap that will not decode costs the material
                // its shine and nothing else; it must never take the base texture with it.
                assets.LogWarning(failure, "cubemap for materials/{Material}.vmt", materialName);

                return null;
            }
            catch (ArgumentOutOfRangeException failure)
            {
                // A VTF that declares the envmap flag and holds fewer than seven faces. Reported
                // rather than swallowed: it means either a malformed file or a wrong face count,
                // and both are worth seeing.
                assets.LogWarning(failure, "cubemap for materials/{Material}.vmt", materialName);

                return null;
            }
        }

        MapTexture? ResolveLightWarp()
        {
            // **A ramp, not a picture.** One row of texels indexed by the diffuse term, so it is
            // loaded like any other texture and sampled with a CLAMP sampler — wrapping it would
            // send a surface at the very edge of the ramp back to the other end of the curve.
            if (material.LightWarpTexture is not { } name)
            {
                return null;
            }

            if (Load(name) is not { } decoded)
            {
                assets.LogWarning(
                    "{Message}",
                    $"light warp {name}, named by materials/{materialName}.vmt, could not be read");

                return null;
            }

            return new MapTexture(
                decoded.Width, decoded.Height, decoded.Image, IsTransparent: false);
        }

        MapBump? ResolveBump()
        {
            if (material.BumpMap is not { } name)
            {
                return null;
            }

            if (Load(name) is not { } decoded)
            {
                assets.LogWarning(
                    "{Message}",
                    $"bump map {name}, named by materials/{materialName}.vmt, could not be read");

                return null;
            }

            // **The texture's own flag outranks the material's declaration**, the same way it does
            // for a detail texture's blend mode. On cp_process_final the two agree on all 13
            // materials that use one, but the flag is data and $ssbump is a statement about it.
            return new MapBump(
                new MapTexture(
                    decoded.Width, decoded.Height, decoded.Image, IsTransparent: false),
                decoded.IsSelfShadowBump || material.IsSelfShadowingBump);
        }

        VtfTexture? Load(string name)
        {
            string bare = name.EndsWith(".vtf", StringComparison.OrdinalIgnoreCase)
                ? name[..^4]
                : name;

            if (Find("materials/" + bare + ".vtf") is not { } file)
            {
                return null;
            }

            try
            {
                return VtfTexture.Read(file, maximumTextureSize);
            }
            catch (InvalidDataException failure)
            {
                // Reported, never silent: the engine reads every one of these, so anything that
                // will not decode is a defect here until shown otherwise.
                assets.LogWarning(failure, "decoding materials/{Texture}.vtf", bare);

                return null;
            }
        }

        MapDetail? ResolveDetail()
        {
            if (material.Detail is not { } name)
            {
                return null;
            }

            string bare = name.EndsWith(".vtf", StringComparison.OrdinalIgnoreCase)
                ? name[..^4]
                : name;

            if (Find("materials/" + bare + ".vtf") is not { } file)
            {
                assets.LogWarning(
                    "{Message}",
                    $"detail texture materials/{bare}.vtf, named by materials/{materialName}.vmt, was not found");

                return null;
            }

            try
            {
                VtfTexture decoded = VtfTexture.Read(file, maximumTextureSize);

                // **The texture's own flag outranks the material's mode.** Valve's helper forces
                // 10 or 11 when the detail is a self-shadowing bump map, whatever
                // $detailblendmode asked for. Mode 10 needs a bump map we do not have yet, so
                // ssbump detail resolves to 11, which is what the engine does without one.
                int mode = decoded.IsSelfShadowBump
                    ? DetailCombine.SelfShadowBumpNoBump
                    : material.DetailBlendMode;

                return new MapDetail(
                    new MapTexture(decoded.Width, decoded.Height, decoded.Image, IsTransparent: false),
                    material.DetailScale,
                    material.DetailBlendFactor,
                    mode,
                    material.DetailTint);
            }
            catch (InvalidDataException failure)
            {
                // **The base texture survives this.** A detail texture that will not decode, or a
                // $detailscale that is not a number, costs the surface its grain and nothing else
                // - it must never take the base texture with it and turn the surface purple.
                assets.LogWarning(failure, "detail for materials/{Material}.vmt", materialName);

                return null;
            }
        }

        MapTexture? Decode(string? name, bool transparent, bool additive)
        {
            if (name is null)
            {
                return null;
            }

            // **Some materials name the texture WITH its extension.** Valve's own script-generated
            // VMTs do it - the props_hydro pipes carry
            // `$baseTexture "models/props_hydro/2pipe.vtf"` - and appending .vtf to that asks for
            // 2pipe.vtf.vtf, which exists nowhere. The engine tolerates both spellings, so this
            // must too; 19 of cp_process_final's prop materials resolved to nothing over it.
            string bare = name.EndsWith(".vtf", StringComparison.OrdinalIgnoreCase)
                ? name[..^4]
                : name;

            if (Find("materials/" + bare + ".vtf") is not { } vtf)
            {
                assets.LogWarning("texture materials/{Texture}.vtf was not found", bare);

                return null;
            }

            try
            {
                VtfTexture decoded = VtfTexture.Read(vtf, maximumTextureSize);

                // **Names every material that will be BLENDED, and which key decided it.** A prop
                // drawn with alpha blending when it should be opaque looks like a lighting fault,
                // not like a classification fault — the pipes on cp_process read through to the
                // wall behind them and nothing in any log said the word "translucent". Whether a
                // surface blends is decided once, here, from three separate keys, so this is the
                // one place that can answer "why is that see-through" without a debugger.
                // **Alpha test is in here as well as blending, because it is the OTHER way a
                // material's alpha decides whether a surface appears.** A blended surface at the
                // wrong alpha looks faint; an alpha-tested one at the wrong alpha is `clip`ped away
                // pixel by pixel and is simply gone — the same defect, at two severities, and no
                // count anywhere distinguishes "invisible" from "never drawn".
                if (material.IsTranslucent || additive || material.IsModulate || material.IsAlphaTested)
                {
                    string why = string.Concat(
                        material.IsTranslucent ? " translucent" : string.Empty,
                        additive ? " additive" : string.Empty,
                        material.IsModulate ? " modulate" : string.Empty,
                        material.IsAlphaTested ? " alphatest" : string.Empty);

                    assets.LogInformation(
                        "{Message}",
                        $"blended '{bare}' shader '{material.Shader}':{why}" +
                        $" $translucent='{material.Value("$translucent") ?? "-"}'" +
                        $" $vertexalpha='{material.Value("$vertexalpha") ?? "-"}'" +
                        $" $alpha='{material.Value("$alpha") ?? "-"}'" +
                        $" $alphatest='{material.Value("$alphatest") ?? "-"}'" +
                        $" ref='{material.Value("$alphatestreference") ?? "-"}'" +
                        $" fmt={decoded.Format}");
                }

                return new MapTexture(
                    decoded.Width,
                    decoded.Height,
                    decoded.Image,
                    transparent,
                    additive,
                    material.IsTranslucent,
                    material.IsSelfIlluminated ? material.SelfIllumTint : null,
                    material.IsModulate,
                    material.IsModulateTwice,
                    material.IsNoCull,
                    material.IsTwoTexture,
                    material.IsHalfLambert,
                    material.AlphaTestReference,

                    // **Null rather than white when the material names neither**, so the renderer
                    // can tell "no modulation" from "modulation that happens to be the identity"
                    // and the census can report the parameter as consumed only where it is.
                    material.IsModulated ? material.Modulation : null,
                    material.IsDecal,

                    // **The thumbnail Valve's mat_showlowresimage draws.** Carried only on the base
                    // texture, because that is the one the debug view substitutes for — a bump map
                    // or a detail texture has a thumbnail too and nothing ever shows it.
                    decoded.LowResolutionPixels.Length > 0
                        ? new MapThumbnail(
                            decoded.LowResolutionWidth,
                            decoded.LowResolutionHeight,
                            TextureImage.Rgba(decoded.LowResolutionPixels))
                        : null,

                    // Where the tint lands, for a material that names one (B331).
                    material.TintsByBaseAlpha,
                    material.TintOverBase,

                    // The two colours the paint proxies work on, kept apart (B330).
                    material.ColourFactor,
                    material.TintBase);
            }
            catch (InvalidDataException failure)
            {
                // **Reported, never silent.** A texture that cannot be decoded is a defect in this
                // reader until shown otherwise - the engine reads every one of these - and a face
                // quietly falling back to a reflectivity colour is how that goes unnoticed.
                assets.LogWarning(failure, "decoding materials/{Texture}.vtf", bare);

                return null;
            }
        }
    }
}
