using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// One view's legacy impact particles as the engine files them (B734): each emitter its own translucent renderable, the
/// simple singleton's particles one renderable drawn at the start of the translucent pass, and the client-side quads after
/// the view.
/// </summary>
/// <remarks>
/// - **An emitter is a `CParticleEffectBinding`**, a leaf-system renderable whose <c>IsTransparent</c> is always true
///   (`particlemgr.cpp:220`), collated by its box and sorted in its leaf by the box's centre (`clientleafsystem.cpp:1758-1783`);
///   its <c>DrawModel</c> draws its particles material by material (`particlemgr.cpp:296-485`).
/// - **`AddSimpleParticle` feeds the simple singleton**, which <c>DrawParticleSingletons</c> draws whole before the leaf
///   walk of every view's translucent pass, uncollated (`viewrender.cpp:4570`, `particles_ez.cpp:138-159`). The 3D sky draws
///   the skybox singleton, which nothing here feeds.
/// - **A `CFXQuad` is a `CClientSideEffect`**, drawn by <c>DrawEffects</c> after the view and unsorted
///   (`viewrender.cpp:1462-1463`).
/// </remarks>
public sealed class ImpactRenderables
{
    /// <summary>
    /// The first renderable number an emitter takes: below every entity's (<see cref="PlacedBatches.Of"/>) and every particle
    /// effect's ordinal, so no neighbour shares it.
    /// </summary>
    private const int FirstRenderable = int.MinValue / 2;

    private readonly List<(TranslucentPlacement? Placement, string Material, List<DetailSpriteVertex> Corners)> _entries = [];
    private readonly Dictionary<string, List<DetailSpriteVertex>> _singleton = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<DetailSpriteVertex>> _quads = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ParticleBatch> _batches = [];
    private readonly Func<string, List<DetailSpriteVertex>> _cornersFor;
    private readonly Func<string, List<DetailSpriteVertex>> _singletonFor;
    private int _used;
    private int _start;
    private int _renderable;
    private TranslucentPlacement? _placement;

    /// <summary>A view's renderables, built each frame.</summary>
    public ImpactRenderables()
    {
        _cornersFor = CornersFor;
        _singletonFor = material => Pooled(_singleton, material);
    }

    /// <summary>How many emitters the view did not collate.</summary>
    public int Culled { get; private set; }

    /// <summary>Starts a view's frame, keeping the lists.</summary>
    public void Begin()
    {
        _used = 0;
        _renderable = FirstRenderable;
        Culled = 0;

        foreach (List<DetailSpriteVertex> corners in _singleton.Values)
        {
            corners.Clear();
        }

        foreach (List<DetailSpriteVertex> corners in _quads.Values)
        {
            corners.Clear();
        }
    }

    /// <summary>One effect's emitters, singleton particles and quads, from the view's camera.</summary>
    /// <param name="effect">The effect.</param>
    /// <param name="eye">The view's origin.</param>
    /// <param name="forward">Its forward.</param>
    /// <param name="right">Its right.</param>
    /// <param name="up">Its up.</param>
    /// <param name="materialAlpha">A material's own <c>$alpha</c>.</param>
    /// <param name="sees">Whether the view collates a box; null collates every emitter (no view walk ran).</param>
    /// <param name="placeOf">The view's place for a collated box; null places none.</param>
    /// <param name="clientEffects">
    /// Whether the view runs <c>DrawEffects</c> — the main view and a monitor's, through <c>ViewDrawScene</c>
    /// (`viewrender.cpp:1462-1463`, `:3224`); a water view and the 3D sky do not.
    /// </param>
    /// <param name="singletons">Whether the view draws the simple singleton: every view but the 3D sky (`viewrender.cpp:4570`).</param>
    public void Add(
        ImpactEffect effect,
        Vector3 eye,
        Vector3 forward,
        Vector3 right,
        Vector3 up,
        Func<string, float> materialAlpha,
        Func<(Vector3 Min, Vector3 Max), bool>? sees,
        Func<(Vector3 Min, Vector3 Max), int>? placeOf,
        bool clientEffects,
        bool singletons)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(materialAlpha);

        for (int e = 0; e < effect.Emitters.Count; e++)
        {
            ImpactEmitter emitter = effect.Emitters[e];

            if (emitter.Particles.Count == 0)
            {
                continue;
            }

            if (emitter.Singleton)
            {
                if (singletons)
                {
                    ImpactDraw.BuildEmitter(emitter, eye, forward, right, up, materialAlpha, _singletonFor);
                }

                continue;
            }

            (Vector3 Min, Vector3 Max) box = emitter.RenderBox();

            if (sees is not null && !sees(box))
            {
                Culled++;
                continue;
            }

            Vector3 centre = (box.Min + box.Max) * 0.5f;

            _placement = placeOf is null
                ? null
                : new TranslucentPlacement(
                    placeOf(box), (centre.X, centre.Y, centre.Z), _renderable++, (emitter.SortOrigin.X, emitter.SortOrigin.Y, emitter.SortOrigin.Z));
            _start = _used;

            ImpactDraw.BuildEmitter(emitter, eye, forward, right, up, materialAlpha, _cornersFor);
        }

        if (clientEffects)
        {
            ImpactDraw.Quads(effect, materialAlpha, _quads);
        }
    }

    /// <summary>A client-side effect's corners — a tracer line's — drawn after the view with the quads.</summary>
    /// <param name="material">The material.</param>
    /// <param name="corners">The corners, copied.</param>
    /// <exception cref="ArgumentNullException"><paramref name="corners"/> is null.</exception>
    public void AddClientEffect(string material, IReadOnlyList<DetailSpriteVertex> corners)
    {
        ArgumentNullException.ThrowIfNull(corners);

        List<DetailSpriteVertex> into = Pooled(_quads, material);

        for (int at = 0; at < corners.Count; at++)
        {
            into.Add(corners[at]);
        }
    }

    /// <summary>Whether this frame built anything for the view.</summary>
    public bool Any
    {
        get
        {
            for (int at = 0; at < _used; at++)
            {
                if (_entries[at].Corners.Count > 0)
                {
                    return true;
                }
            }

            foreach (List<DetailSpriteVertex> corners in _singleton.Values)
            {
                if (corners.Count > 0)
                {
                    return true;
                }
            }

            foreach (List<DetailSpriteVertex> corners in _quads.Values)
            {
                if (corners.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>The view's batches: the singleton's, each emitter's, then the quads'.</summary>
    /// <param name="materials">Every particle material, by name.</param>
    /// <returns>The batches, valid until the next <see cref="Begin"/>.</returns>
    public IReadOnlyList<ParticleBatch> Batches(IReadOnlyDictionary<string, ParticleMaterial> materials)
    {
        ArgumentNullException.ThrowIfNull(materials);

        _batches.Clear();

        foreach ((string name, List<DetailSpriteVertex> corners) in _singleton)
        {
            if (corners.Count > 0 && materials.TryGetValue(name, out ParticleMaterial material))
            {
                _batches.Add(new ParticleBatch(corners, material, Pass: ParticlePass.Singletons));
            }
        }

        for (int at = 0; at < _used; at++)
        {
            (TranslucentPlacement? placement, string name, List<DetailSpriteVertex> corners) = _entries[at];

            if (corners.Count > 0 && materials.TryGetValue(name, out ParticleMaterial material))
            {
                _batches.Add(new ParticleBatch(corners, material, placement));
            }
        }

        foreach ((string name, List<DetailSpriteVertex> corners) in _quads)
        {
            if (corners.Count > 0 && materials.TryGetValue(name, out ParticleMaterial material))
            {
                _batches.Add(new ParticleBatch(corners, material));
            }
        }

        return _batches;
    }

    /// <summary>The current emitter's corners for a material: its own entry, started on the material's first particle.</summary>
    private List<DetailSpriteVertex> CornersFor(string material)
    {
        for (int at = _start; at < _used; at++)
        {
            if (string.Equals(_entries[at].Material, material, StringComparison.OrdinalIgnoreCase))
            {
                return _entries[at].Corners;
            }
        }

        if (_entries.Count == _used)
        {
            _entries.Add((null, material, []));
        }

        List<DetailSpriteVertex> corners = _entries[_used].Corners;

        corners.Clear();
        _entries[_used] = (_placement, material, corners);
        _used++;

        return corners;
    }

    private static List<DetailSpriteVertex> Pooled(Dictionary<string, List<DetailSpriteVertex>> into, string material)
    {
        if (!into.TryGetValue(material, out List<DetailSpriteVertex>? corners))
        {
            into[material] = corners = [];
        }

        return corners;
    }
}
