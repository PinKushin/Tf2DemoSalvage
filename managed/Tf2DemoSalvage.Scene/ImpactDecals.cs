using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>Which decal a bullet leaves on the world, from the surface it struck (B415).</summary>
/// <remarks>
/// The client's chain, from `FireBullet`'s `UTIL_ImpactTrace( &amp;trace, nDamageType )`:
///
/// <code>
/// UTIL_ImpactTrace:   no entity, or surface.flags &amp; SURF_SKY, or fraction == 1, or SURF_NODRAW  → nothing
/// ImpactTrace:        DispatchEffect( "Impact" ) with m_nSurfaceProp = trace.surface.surfaceProps
/// ParseImpactData:    iMaterial = physprops-&gt;GetSurfaceData( m_nSurfaceProp )-&gt;game.material
/// Impact:             GetImpactDecal → DamageDecal ("Impact.Concrete" for the world) → TranslateDecalForGameMaterial
///                     → GetDecalIndexForName (a weighted pick) → AddDecal → AddBrushModelDecal → DecalShoot at endpos
/// </code>
///
/// **A brush surface's `surfaceProps` is its texdata's**: `CMod_LoadTextures` (`engine.dll` `0x18016eb00`) finds each
/// texdata's world material and stores `GetSurfaceIndex` of its `$surfaceprop`, which is −1 for a material without one
/// and reads as surface zero.
/// </remarks>
public sealed class ImpactDecals
{
    private readonly DecalEmitters _emitters;
    private readonly DecalMaterials _materials;
    private readonly IReadOnlyList<BspTexinfo> _texinfo;
    private readonly char[] _gameMaterials;
    private readonly Func<int, char> _surfacePropMaterial;

    /// <summary>`DMG_SLASH`: `DamageDecal` answers `"ManhackCut"` for exactly this damage type.</summary>
    private const int SlashDamage = 1 << 2;

    /// <summary>A resolver over one map.</summary>
    /// <param name="emitters">The game's decal groups.</param>
    /// <param name="materials">Decal names to materials.</param>
    /// <param name="texinfo">The map's texinfos.</param>
    /// <param name="gameMaterials">Each texdata's `game.material`.</param>
    /// <param name="surfacePropMaterial">A surfaceprop index's `game.material` — `GetSurfaceData( i )->game.material`.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public ImpactDecals(
        DecalEmitters emitters,
        DecalMaterials materials,
        IReadOnlyList<BspTexinfo> texinfo,
        char[] gameMaterials,
        Func<int, char>? surfacePropMaterial = null)
    {
        ArgumentNullException.ThrowIfNull(emitters);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(texinfo);
        ArgumentNullException.ThrowIfNull(gameMaterials);

        _emitters = emitters;
        _materials = materials;
        _texinfo = texinfo;
        _gameMaterials = gameMaterials;
        _surfacePropMaterial = surfacePropMaterial ?? (static _ => '\0');
    }

    /// <summary>Decal names to materials — the <c>decalprecache</c> table's names resolve here too.</summary>
    public DecalMaterials Materials => _materials;

    /// <summary>Reads everything the chain needs out of a map and the game.</summary>
    /// <param name="map">The map's bytes.</param>
    /// <param name="archives">The game's content.</param>
    /// <param name="surfaces">The game's surface properties.</param>
    /// <returns>The resolver; one that answers nothing when the game ships no decal list.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static ImpactDecals Load(ReadOnlyMemory<byte> map, GameArchives archives, VphysicsSurfaceProps surfaces)
    {
        ArgumentNullException.ThrowIfNull(archives);
        ArgumentNullException.ThrowIfNull(surfaces);

        PakFile pak = PakFile.ReadFrom(map);
        IReadOnlyList<BspMaterial> texdata = BspMaterials.Read(map);
        char[] gameMaterials = new char[texdata.Count];
        int[] surfaceProps = new int[texdata.Count];

        for (int index = 0; index < gameMaterials.Length; index++)
        {
            int surface = MapAssets.ReadVmt(texdata[index].Name, pak, archives)?.Value("$surfaceprop") is { } name
                ? surfaces.GetSurfaceIndex(name)
                : -1;

            surfaceProps[index] = surface;
            gameMaterials[index] = (char)(surfaces.GetSurfaceData(surface)?.GameMaterial ?? 0);
        }

        byte[]? script = archives.Read(DecalEmitters.ScriptPath);

        return new ImpactDecals(
            DecalEmitters.Parse(script ?? []),
            DecalMaterials.Over(path => pak.ReadFile(path) ?? archives.Read(path)),
            BspMaterials.ReadTexinfo(map),
            gameMaterials,
            surfaceProp => (char)(surfaces.GetSurfaceData(surfaceProp)?.GameMaterial ?? 0))
        {
            _surfaceProps = surfaceProps,
        };
    }

    /// <summary>Each texdata's surfaceprop index — `CMod_LoadTextures`' `GetSurfaceIndex`, −1 for none.</summary>
    private int[] _surfaceProps = [];

    /// <summary>The surfaceprop a bullet struck — `trace.surface.surfaceProps`, or the server's own for its impacts.</summary>
    /// <param name="impact">The bullet.</param>
    /// <returns>The surface index, −1 when the texture declares none (which reads as surface zero).</returns>
    public int SurfacePropOf(ShotImpact impact)
    {
        return impact.FromServer ? impact.SurfaceProp : SurfacePropOfTexinfo(impact.Texinfo);
    }

    /// <summary>`trace.surface.surfaceProps` for a world trace that hit a texinfo.</summary>
    /// <param name="texinfo">The texinfo the trace reported.</param>
    /// <returns>The surface index, −1 when the texture declares none or the texinfo is out of range.</returns>
    public int SurfacePropOfTexinfo(int texinfo)
    {
        if (texinfo < 0 || texinfo >= _texinfo.Count)
        {
            return -1;
        }

        int texdata = _texinfo[texinfo].Texdata;

        return texdata >= 0 && texdata < _surfaceProps.Length ? _surfaceProps[texdata] : -1;
    }

    /// <summary>The decal a bullet leaves where the world stopped it, or null for none.</summary>
    /// <param name="impact">The bullet.</param>
    /// <param name="random">`random->RandomFloat( min, max )` for the weighted pick.</param>
    /// <returns>The material, or null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="random"/> is null.</exception>
    /// <remarks>*Not built:* terrain (texinfo −1), whose decals take the displacement path.</remarks>
    public DecalMaterial? For(ShotImpact impact, Func<float, float, float> random)
    {
        ArgumentNullException.ThrowIfNull(random);

        if (Surface(impact) is not { } surface ||
            (surface.Flags & (SurfaceProperties.Sky | SurfaceProperties.NoDraw)) != 0)
        {
            return null;
        }

        // `CBaseEntity::DamageDecal` for the world: "ManhackCut" for exactly `DMG_SLASH`, else "Impact.Concrete".
        string decal = impact.DamageType == SlashDamage ? "ManhackCut" : DecalEmitters.ImpactConcrete;
        string group = _emitters.Translate(decal, surface.GameMaterial);

        return group.Length > 0 && _emitters.Pick(group, random) is { } file ? _materials.Resolve(file) : null;
    }

    /// <summary><see cref="For(ShotImpact, Func{float, float, float})"/> with a draw seeded by the bullet — <see cref="SeededDraw"/>.</summary>
    /// <param name="impact">The bullet.</param>
    /// <returns>The material, or null.</returns>
    public DecalMaterial? For(ShotImpact impact) => For(impact, SeededDraw.For(SeededDraw.Of(impact.Shot, impact.Bullet)));

    /// <summary>The decal a bullet leaves on an entity, from the surfaceprop the server named, or null for none.</summary>
    /// <param name="surfaceProp">The dispatch's `m_nSurfaceProp`.</param>
    /// <param name="damageType">The dispatch's `m_nDamageType`.</param>
    /// <param name="renderMode">The struck entity's `m_nRenderMode`.</param>
    /// <param name="random">`random->RandomFloat( min, max )` for the weighted pick.</param>
    /// <returns>The material, or null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="random"/> is null.</exception>
    /// <remarks>
    /// `CBaseEntity::DamageDecal` (`baseentity_shared.cpp:708`): nothing for `kRenderTransAlpha`, `"BulletProof"` for any
    /// other non-normal render mode on glass, `"ManhackCut"` for exactly `DMG_SLASH`, else `"Impact.Concrete"` — then
    /// `TranslateDecalForGameMaterial` by the surfaceprop's game material, as for the world.
    /// </remarks>
    public DecalMaterial? ForEntity(int surfaceProp, int damageType, int renderMode, Func<float, float, float> random)
    {
        ArgumentNullException.ThrowIfNull(random);

        const int TransAlpha = 4;
        const int Normal = 0;

        char material = _surfacePropMaterial(surfaceProp);
        string decal;

        if (renderMode == TransAlpha)
        {
            return null;
        }

        if (renderMode != Normal && material == 'G')
        {
            decal = "BulletProof";
        }
        else
        {
            decal = damageType == SlashDamage ? "ManhackCut" : DecalEmitters.ImpactConcrete;
        }

        string group = _emitters.Translate(decal, material);

        return group.Length > 0 && _emitters.Pick(group, random) is { } file ? _materials.Resolve(file) : null;
    }

    /// <summary>What `PerformCustomEffects` reads of the struck surface: its game material and its flags.</summary>
    /// <param name="impact">The bullet.</param>
    /// <returns>The surface, or null for terrain.</returns>
    /// <remarks>
    /// **The material is the dispatch's own for a server impact** — `ParseImpactData` reads it from `m_nSurfaceProp` —
    /// and the struck texinfo's for a client bullet, as its trace's `surface.surfaceProps` gives it. The flags are always
    /// the trace's.
    /// </remarks>
    public (char GameMaterial, SurfaceProperties Flags)? Surface(ShotImpact impact)
    {
        if (impact.Texinfo < 0 || impact.Texinfo >= _texinfo.Count)
        {
            return null;
        }

        BspTexinfo surface = _texinfo[impact.Texinfo];
        char gameMaterial;

        if (impact.FromServer)
        {
            gameMaterial = _surfacePropMaterial(impact.SurfaceProp);
        }
        else
        {
            gameMaterial = surface.Texdata >= 0 && surface.Texdata < _gameMaterials.Length
                ? _gameMaterials[surface.Texdata]
                : '\0';
        }

        return (gameMaterial, surface.Flags);
    }

    /// <summary>Every material an impact could draw with, for loading with the map.</summary>
    /// <returns>The drawn materials' names: a Subrect's atlas, not the Subrect.</returns>
    public IReadOnlyCollection<string> Drawn()
    {
        HashSet<string> drawn = new(StringComparer.OrdinalIgnoreCase);

        foreach (string file in _emitters.Files())
        {
            // A decal's `$modelmaterial` too, which draws it on a model (B415).
            if (_materials.Resolve(file)?.ModelMaterial is { } model)
            {
                drawn.Add(model);
            }

            if (_materials.Resolve(file)?.Draws is { } name)
            {
                drawn.Add(name);
            }
        }

        return drawn;
    }
}
