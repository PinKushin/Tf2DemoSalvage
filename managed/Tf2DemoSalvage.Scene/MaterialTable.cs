using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// The map's material table and everything indexed by it, grown as one.
/// </summary>
/// <remarks>
/// **This exists because the lists kept getting out of step, three times, in the same way.** A
/// material's texture, second texture, detail, bump map, cubemap and proxies are separate lists
/// indexed by one number, and a prop's materials continue the same table — so anything appending to
/// the table has to append to all of them.
///
/// Nothing enforced that. <c>PropModels.Register</c> appended to three lists and the rest were
/// padded afterwards with nulls, so **every model material silently lost its detail texture, its
/// bump map, its cubemap and its proxies**. That is not a bug that shows up as an error; it shows up
/// as a prop that is slightly flat, which is indistinguishable from art direction.
///
/// The history is written into the code it broke. <c>Register</c> carries a comment beginning
/// "**Three lists, not two, and for the same reason the comment above gives**", added when the
/// second texture went missing the same way and a capture point beam kept its stripes only for BLU.
/// The fix each time was to add one more <c>Add</c> call beside the others, which works until the
/// next list appears.
///
/// So the lists are private and <see cref="Add"/> is the only way in. Growing one without the
/// others is no longer something a caller can do by forgetting — the parallel-list invariant is
/// held by the type rather than by whoever edits next.
/// </remarks>
public sealed class MaterialTable
{
    private readonly List<BspMaterial> _materials = [];
    private readonly List<string> _shaders = [];
    private readonly List<MapTexture?> _textures = [];
    private readonly List<MapTexture?> _blendTextures = [];
    private readonly List<MapDetail?> _details = [];
    private readonly List<MapBump?> _bumps = [];
    private readonly List<MapCubemap?> _cubemaps = [];
    private readonly List<MapEnvmapShading?> _localReflections = [];
    private readonly List<MapPhong?> _phong = [];
    private readonly List<MapTexture?> _lightWarps = [];

    private readonly List<MapTexture?> _selfIllumMasks = [];
    private readonly List<MapTexture?> _phongExponentMaps = [];
    private readonly List<IReadOnlyList<MaterialProxy>> _proxies = [];

    /// <summary>How many materials the table holds.</summary>
    public int Count => _materials.Count;

    /// <summary>The map's texture table, for reflectivity where a texture is missing.</summary>
    public IReadOnlyList<BspMaterial> Materials => _materials;

    /// <summary>One decoded texture per material, null where none was found.</summary>
    /// <summary>Each material's VMT shader name, e.g. <c>LightmappedGeneric</c> or <c>Water</c>.</summary>
    /// <remarks>
    /// **Kept so a null base texture can be read correctly** (B62). Most shaders without one are a
    /// failure; <c>Water</c> is not, and the difference is only visible from here.
    /// </remarks>
    public IReadOnlyList<string> Shaders => _shaders;

    public IReadOnlyList<MapTexture?> Textures => _textures;

    /// <summary>The second layer of a blend material, null for the rest.</summary>
    public IReadOnlyList<MapTexture?> BlendTextures => _blendTextures;

    /// <summary>The detail texture for each material, null for those without one.</summary>
    public IReadOnlyList<MapDetail?> Details => _details;

    /// <summary>The bump map for each material, null for those without one.</summary>
    public IReadOnlyList<MapBump?> Bumps => _bumps;

    /// <summary>The baked reflection for each material, null for those without one.</summary>
    public IReadOnlyList<MapCubemap?> Cubemaps => _cubemaps;

    /// <summary>
    /// How each material shades the map's own cubemap, null for those that do not ask for it.
    /// </summary>
    /// <remarks>
    /// **Separate from <see cref="Cubemaps"/> because the two are chosen at different times.** A
    /// material here asked for the literal <c>env_cubemap</c>, so it has the shading but no cube —
    /// which placement it reflects depends on where the model stands and is decided per draw.
    /// </remarks>
    public IReadOnlyList<MapEnvmapShading?> LocalReflections => _localReflections;

    /// <summary>The specular highlight for each material, null for those without one.</summary>
    public IReadOnlyList<MapPhong?> Phong => _phong;

    /// <summary>The authored lighting ramp for each material, null for a linear falloff.</summary>
    public IReadOnlyList<MapTexture?> LightWarps => _lightWarps;

    /// <summary>
    /// The self-illumination mask for each material, null where the base map's alpha decides it.
    /// </summary>
    public IReadOnlyList<MapTexture?> SelfIllumMasks => _selfIllumMasks;

    /// <summary>
    /// The phong exponent map for each material, null where the engine binds white instead.
    /// </summary>
    /// <remarks>
    /// Three channels, three jobs — red the exponent, green the albedo tint, alpha the rim mask
    /// (B334). Null and white are NOT the same to a reader here: white is what the engine
    /// substitutes, and reproducing that substitution is the renderer's business rather than this
    /// table's.
    /// </remarks>
    public IReadOnlyList<MapTexture?> PhongExponentMaps => _phongExponentMaps;

    /// <summary>The proxies each material runs, empty for the great majority.</summary>
    public IReadOnlyList<IReadOnlyList<MaterialProxy>> Proxies => _proxies;

    /// <summary>Appends one material and everything indexed alongside it.</summary>
    /// <param name="material">The table entry: name, reflectivity and size.</param>
    /// <param name="resolved">What that material resolved to.</param>
    /// <returns>The index the material was given.</returns>
    /// <remarks>
    /// **One call, eight lists.** The whole point of the type: a caller cannot append a texture and
    /// forget the proxies, because there is no way to append a texture on its own.
    /// </remarks>
    public int Add(BspMaterial material, ResolvedMaterial resolved)
    {
        int index = _materials.Count;

        _materials.Add(material);
        _textures.Add(resolved.Texture);
        _blendTextures.Add(resolved.Blend);
        _details.Add(resolved.Detail);
        _bumps.Add(resolved.Bump);
        _cubemaps.Add(resolved.Cubemap);
        _localReflections.Add(resolved.LocalReflection);
        _phong.Add(resolved.Phong);
        _lightWarps.Add(resolved.LightWarp);
        _selfIllumMasks.Add(resolved.SelfIllumMask);
        _phongExponentMaps.Add(resolved.PhongExponentMap);
        _proxies.Add(resolved.Proxies ?? []);

        // **The shader name, kept because "no base texture" is only a fault for SOME shaders**
        // (B62). `Water` declares none by design — it refracts against a render target and takes its
        // surface from a normal map — so the renderer needs to know which shader asked before
        // deciding that a null texture means a missing material.
        // **`?? string.Empty`, because `default(ResolvedMaterial)` has a NULL Shader.** The record
        // declares `string Shader = ""`, and a primary-constructor default does not run for
        // `default(T)` — `Resolve` returns `default` on a miss, so every material that failed to
        // resolve arrives here with null. Without this the water check below threw a
        // NullReferenceException on four rendering tests, and only on maps with a missing material.
        //
        // Second time this exact trap has bitten in one day: `ChaseSettings` needed the same guard,
        // for the same reason. See `docs/memory/a-nullable-pattern-on-a-struct-is-dead-code.md`'s
        // neighbourhood — a record struct's defaults are constructor behaviour, not field state.
        _shaders.Add(resolved.Shader ?? string.Empty);

        return index;
    }
}
