using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Batches that are each one translucent renderable's own, placed in the view's walk (B734): a sprite's or a trail's, which
/// the engine files <c>RENDER_GROUP_TRANSLUCENT_ENTITY</c> and draws whole in its leaf's sorted run, rather than merged with
/// every other one on its material. The corner lists are reused frame to frame.
/// </summary>
internal sealed class PlacedBatches
{
    private readonly List<List<DetailSpriteVertex>> _corners = [];
    private readonly List<ParticleBatch> _batches = [];

    /// <summary>
    /// An entity's placement: its place, the centre of its world render box — <c>SortEntities</c>' key, from
    /// <c>GetRenderBounds</c> about <c>GetRenderOrigin</c> (`clientleafsystem.cpp:1758-1783`) — and its render origin.
    /// </summary>
    /// <param name="place">The view's place for it.</param>
    /// <param name="bounds">Its world render box.</param>
    /// <param name="entity">Its entity index, which names the renderable.</param>
    /// <param name="origin">Its render origin.</param>
    /// <returns>The placement.</returns>
    /// <remarks>
    /// The renderable is the entity's, made negative so it can never equal a particle effect's ordinal beside it, which
    /// <c>Device3D</c> would read as one renderable (<c>FillPlaced</c>).
    /// </remarks>
    public static TranslucentPlacement Of(int place, (Vector3 Min, Vector3 Max) bounds, int entity, Vector3 origin)
    {
        Vector3 centre = (bounds.Min + bounds.Max) * 0.5f;

        return new TranslucentPlacement(place, (centre.X, centre.Y, centre.Z), -1 - entity, (origin.X, origin.Y, origin.Z));
    }

    /// <summary>Forgets the last build's batches, keeping the lists.</summary>
    public void Begin() => _batches.Clear();

    /// <summary>A new, empty corner list for one draw of a placed renderable, its batch already recorded.</summary>
    /// <param name="placement">The renderable's placement.</param>
    /// <param name="material">The draw's material and pass.</param>
    /// <returns>The list to fill.</returns>
    public List<DetailSpriteVertex> Next(TranslucentPlacement placement, ParticleMaterial material)
    {
        if (_corners.Count == _batches.Count)
        {
            _corners.Add([]);
        }

        List<DetailSpriteVertex> corners = _corners[_batches.Count];

        corners.Clear();
        _batches.Add(new ParticleBatch(corners, material, placement));

        return corners;
    }

    /// <summary>Appends each placed batch that has corners, in the order made.</summary>
    /// <param name="into">The list to append to.</param>
    public void AppendTo(List<ParticleBatch> into)
    {
        for (int at = 0; at < _batches.Count; at++)
        {
            if (_batches[at].Corners.Count > 0)
            {
                into.Add(_batches[at]);
            }
        }
    }
}
