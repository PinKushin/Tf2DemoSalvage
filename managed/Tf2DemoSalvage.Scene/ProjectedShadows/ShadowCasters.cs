using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.ProjectedShadows;

/// <summary>How an entity casts — the <c>ShadowType_t</c> values this port draws.</summary>
public enum ShadowCast
{
    /// <summary><c>SHADOWS_NONE</c>.</summary>
    None = 0,

    /// <summary><c>SHADOWS_RENDER_TO_TEXTURE</c>: its texture is redrawn when it is dirty or takes a new cell.</summary>
    RenderToTexture,

    /// <summary><c>SHADOWS_RENDER_TO_TEXTURE_DYNAMIC</c>: an animating source, redrawn every frame it is in view.</summary>
    Dynamic,
}

/// <summary>One drawn entity, as the client shadow manager reads it.</summary>
/// <param name="EntityIndex">The entity.</param>
/// <param name="ShadowParent">Its move parent (<c>GetShadowParent</c>), or null.</param>
/// <param name="Cast">Its <c>ShadowCastType</c>.</param>
/// <param name="RenderOrigin"><c>GetRenderOrigin</c>.</param>
/// <param name="RenderAngles"><c>GetRenderAngles</c>, pitch, yaw and roll in degrees.</param>
/// <param name="Mins"><c>GetShadowRenderBounds</c>' lower corner, in its own frame.</param>
/// <param name="Maxs">Its upper corner.</param>
/// <param name="Model">Its model, which tells a new entity in a reused index from the old one.</param>
/// <param name="Animation">Its sequence and cycle, whose change marks its texture dirty (<c>ANIMATION_CHANGED</c>).</param>
public readonly record struct ShadowEntity(
    int EntityIndex,
    int? ShadowParent,
    ShadowCast Cast,
    Vector3 RenderOrigin,
    (float Pitch, float Yaw, float Roll) RenderAngles,
    Vector3 Mins,
    Vector3 Maxs,
    string Model = "",
    (int Sequence, float Cycle) Animation = default);

/// <summary>A shadow's cell on the atlas page, in texels — <c>GetTextureRect</c>.</summary>
/// <param name="X">Its left edge.</param>
/// <param name="Y">Its top edge.</param>
/// <param name="Width">Its width.</param>
/// <param name="Height">Its height.</param>
public readonly record struct ShadowCell(int X, int Y, int Width, int Height)
{
    /// <summary>
    /// The texture coordinate of the cell's first texel centre — <c>SetRenderToTextureShadowTexCoords</c>
    /// (`clientshadowmgr.cpp:3570-3586`): "go in a half-pixel to avoid blending with neighboring textures".
    /// </summary>
    public Vector2 TexOrigin => new((X + 0.5f) / ShadowTextureAllocator.PageSize, (Y + 0.5f) / ShadowTextureAllocator.PageSize);

    /// <summary>The span from the first texel centre to the last, `(w − 1) / 1024`.</summary>
    public Vector2 TexSize => new((Width - 1f) / ShadowTextureAllocator.PageSize, (Height - 1f) / ShadowTextureAllocator.PageSize);
}

/// <summary>One render-to-texture shadow this frame: its caster, what is drawn into it, where, and how it projects.</summary>
/// <param name="EntityIndex">The caster.</param>
/// <param name="Hierarchy">Every entity <c>DrawShadowHierarchy</c> draws into its cell: the caster and its move children.</param>
/// <param name="Projection">Its geometry.</param>
/// <param name="Cell">Its cell.</param>
/// <param name="Source">The caster as `CullReceiver` reads it — render origin, angles and shadow render bounds; default for a
/// shadow built without one, which a receiver's separating-plane test then measures from the origin.</param>
public sealed record CastShadow(
    int EntityIndex, IReadOnlyList<int> Hierarchy, ShadowProjection Projection, ShadowCell Cell, ShadowEntity Source = default)
{
    /// <summary>`Ortho( 0, 0, 1, 1, -9999, 0 )`'s far depth (`clientshadowmgr.cpp:4036`).</summary>
    private const float OrthoDepth = 9999f;

    /// <summary>
    /// Whether this is the blob — <c>DrawRenderToTextureShadowLOD</c> (`clientshadowmgr.cpp:3816-3826`), past the budget or
    /// without a cell: `decals/simpleshadow` for both materials, the texture coordinates (0, 0, 1, 1), no extra clip planes,
    /// nothing drawn into the atlas.
    /// </summary>
    public bool Blob { get; init; }

    /// <summary>Where (u, v) = (0, 0) samples: the cell's first texel centre, or the blob texture's corner.</summary>
    public Vector2 TexOrigin => Blob ? Vector2.Zero : Cell.TexOrigin;

    /// <summary>How far (u, v) = (1, 1) reaches from <see cref="TexOrigin"/>: the cell's span, or the whole blob texture.</summary>
    public Vector2 TexSize => Blob ? Vector2.One : Cell.TexSize;

    /// <summary>
    /// The caster's view-projection into its cell, row-major for a row vector — <c>m_WorldToShadow</c> as the view and
    /// `Scale( 1, -1, 1 ) · Ortho( 0, 0, 1, 1, -9999, 0 )` as the projection (`clientshadowmgr.cpp:4032-4036`, `:3771-3772`).
    /// </summary>
    /// <returns>Sixteen floats: x = 2u − 1, y = 1 − 2v, z = 1 − depth / 9999.</returns>
    /// <remarks>
    /// **The view is the world-to-TEXTURE matrix** (finding 107): u and v run 0 to 1 across the shadow, so the ortho's unit
    /// square is the whole cell. The depth is D3DX's right-handed off-centre ortho with near −9999 and far 0, which keeps
    /// 0 to 9999 along the shadow direction and clips what lies behind the shadow's origin plane.
    /// </remarks>
    public float[] CasterViewProjection()
    {
        ShadowProjection p = Projection;
        float sx = 2f / p.Size.X;
        float sy = 2f / p.Size.Y;

        return
        [
            p.XAxis.X * sx, -p.YAxis.X * sy, -p.Direction.X / OrthoDepth, 0f,
            p.XAxis.Y * sx, -p.YAxis.Y * sy, -p.Direction.Y / OrthoDepth, 0f,
            p.XAxis.Z * sx, -p.YAxis.Z * sy, -p.Direction.Z / OrthoDepth, 0f,
            -Vector3.Dot(p.XAxis, p.Origin) * sx,
            Vector3.Dot(p.YAxis, p.Origin) * sy,
            1f + (Vector3.Dot(p.Direction, p.Origin) / OrthoDepth),
            1f,
        ];
    }
}

/// <summary>
/// Which entities get a render-to-texture shadow this frame and which atlas cell each draws into —
/// <c>CClientShadowMgr::ComputeShadowTextures</c> (`clientshadowmgr.cpp:4001-4109`, finding 107).
/// </summary>
/// <remarks>
/// **The engine's order**: a shadow per drawn caster that does not render into its parent's (<c>ShouldUseParentShadow</c>,
/// `:2845-2865`); the visible ones, each frustum-culled by its swept box (<c>EnumShadow</c>, `:1059-1109`) and sorted by
/// screen area, largest first (<c>PrioritySort</c>, `:1115-1143`); then at most <c>r_shadowmaxrendered</c> of them REDRAWN
/// this frame (<c>DrawRenderToTextureShadow</c>, `:3733-3810`) — a shadow whose cell was kept and is not dirty draws from
/// its old texture and does not count. Past the budget, and when the atlas has no cell, the shadow is the blob
/// (<see cref="CastShadow.Blob"/>).
///
/// **What this does not do**: enumerate shadows by the view's leaves (`EnumerateShadowsInLeaves`) — the frustum alone
/// decides, a superset. Filed in finding 107.
/// </remarks>
public sealed class ShadowCasters
{
    /// <summary><c>r_shadowmaxrendered</c>'s default (`clientshadowmgr.cpp:685`).</summary>
    public const int MaxRendered = 32;

    /// <summary>The deepest move-parent chain followed, so a cycle in demo data cannot hang the frame.</summary>
    private const int MaxDepth = 16;

    private sealed class Texture
    {
        public required int Handle { get; init; }

        public bool Dirty { get; set; }

        /// <summary>The caster as last seen, to tell what changed.</summary>
        public required ShadowEntity Last { get; set; }
    }

    private readonly ShadowTextureAllocator _atlas = new();
    private readonly Dictionary<int, Texture> _textures = [];
    private readonly Dictionary<int, ShadowEntity> _byIndex = [];
    private readonly Dictionary<int, List<int>> _children = [];
    private readonly HashSet<int> _roots = [];
    private readonly List<int> _gone = [];
    private readonly List<(ShadowEntity Root, float Area)> _visible = [];

    /// <summary>A new level — <c>LevelInitPreEntity</c>'s <c>m_ShadowAllocator.Reset()</c> — or a jump that dropped every shadow.</summary>
    public void Reset()
    {
        _atlas.Reset();
        _textures.Clear();
    }

    /// <summary>This frame's shadows.</summary>
    /// <param name="entities">The drawn entities.</param>
    /// <param name="direction">The shadow direction, any length.</param>
    /// <param name="distance">The cast distance.</param>
    /// <param name="frustum">The view, for the swept-box cull.</param>
    /// <param name="screen">The view, for the screen area each shadow's cell size comes from.</param>
    /// <param name="maxRendered"><c>r_shadowmaxrendered</c>.</param>
    /// <returns>Every shadow drawn this frame, largest on screen first.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entities"/> is null.</exception>
    public IReadOnlyList<CastShadow> Frame(
        IReadOnlyList<ShadowEntity> entities,
        Vector3 direction,
        float distance,
        ViewFrustum frustum,
        ScreenFadeView screen,
        int maxRendered = MaxRendered)
    {
        ArgumentNullException.ThrowIfNull(entities);

        // `CViewRender::RenderView` advances the allocator once a frame, before any view (`AdvanceFrame`, `:3832`).
        _atlas.AdvanceFrame();

        Index(entities);
        Vector3 dir = Vector3.Normalize(direction);

        // A shadow exists while its caster is drawn (`RemoveFromLeafSystem` destroys it) and is sized once, when created
        // (`SetupRenderToTextureShadow`, `:1584-1615`).
        _roots.Clear();

        foreach (ShadowEntity entity in entities)
        {
            if (entity.Cast == ShadowCast.None || UsesParentShadow(entity, 0))
            {
                continue;
            }

            _roots.Add(entity.EntityIndex);

            // A different model in the same index is a new entity, so a new shadow sized for it.
            if (_textures.TryGetValue(entity.EntityIndex, out Texture? held) && held.Last.Model != entity.Model)
            {
                _atlas.DeallocateTexture(held.Handle);
                _textures.Remove(entity.EntityIndex);
                held = null;
            }

            if (held is null)
            {
                _textures[entity.EntityIndex] = new Texture
                {
                    Handle = _atlas.AllocateTexture(TextureSize(entity)),
                    Dirty = true,
                    Last = entity,
                };

                continue;
            }

            // `ANGLES_CHANGED` and `ANIMATION_CHANGED` mark a render-to-texture shadow dirty
            // (`MarkRenderToTextureShadowDirty`, `baseentity_shared.cpp:1444-1475`); a move alone re-projects it only.
            if (held.Last.RenderAngles != entity.RenderAngles || held.Last.Animation != entity.Animation)
            {
                held.Dirty = true;
            }

            held.Last = entity;
        }

        _gone.Clear();

        foreach (int caster in _textures.Keys)
        {
            if (!_roots.Contains(caster))
            {
                _gone.Add(caster);
            }
        }

        foreach (int caster in _gone)
        {
            _atlas.DeallocateTexture(_textures[caster].Handle);
            _textures.Remove(caster);
        }

        _visible.Clear();

        foreach (ShadowEntity entity in entities)
        {
            if (!_roots.Contains(entity.EntityIndex))
            {
                continue;
            }

            (Vector3 centre, float radius) = ShadowReceiverCull.BoundingSphere(entity);
            (Vector3 min, Vector3 max) = SweptBox(centre, radius, dir, distance);

            if (frustum.Cull(min.X, min.Y, min.Z, max.X, max.Y, max.Z))
            {
                continue;
            }

            float diameter = screen.PixelDiameterOfSphere((centre.X, centre.Y, centre.Z), radius);
            _visible.Add((entity, diameter * diameter));
        }

        PrioritySort(_visible);

        List<CastShadow> shadows = [];
        int rendered = 0;

        foreach ((ShadowEntity root, float area) in _visible)
        {
            ShadowProjection projection = ShadowProjection.ForRenderToTexture(
                root.RenderOrigin, root.RenderAngles, root.Mins, root.Maxs, dir, distance);

            // `ComputeShadowTextures`' loop (`:4080-4089`): past the budget, the blob.
            if (rendered >= maxRendered)
            {
                shadows.Add(BlobOf(root, projection));
                continue;
            }

            Texture texture = _textures[root.EntityIndex];
            bool dirty = texture.Dirty;
            bool newCell = _atlas.UseTexture(texture.Handle, dirty, area);

            // No cell (`DrawRenderToTextureShadow`, `:3750-3754`): the blob, and not counted.
            if (!_atlas.HasValidTexture(texture.Handle))
            {
                shadows.Add(BlobOf(root, projection));
                continue;
            }

            if (newCell || dirty)
            {
                rendered++;

                // "Only clear the dirty flag if the caster isn't animating" (`:3792-3796`).
                if (root.Cast != ShadowCast.Dynamic)
                {
                    texture.Dirty = false;
                }
            }

            (int x, int y, int width, int height) = _atlas.TextureRect(texture.Handle);

            shadows.Add(new CastShadow(root.EntityIndex, Hierarchy(root), projection, new ShadowCell(x, y, width, height), root));
        }

        return shadows;
    }

    /// <summary><c>DrawRenderToTextureShadowLOD</c>: the same projection with its extra clip planes cleared, and no cell.</summary>
    private static CastShadow BlobOf(ShadowEntity root, ShadowProjection projection) =>
        new(root.EntityIndex, [], projection with { ClipPlanes = [] }, default, root) { Blob = true };

    /// <summary>The first power of two at or above two texels per unit of the largest side (`:1596-1614`).</summary>
    private static int TextureSize(ShadowEntity entity)
    {
        Vector3 size = entity.Maxs - entity.Mins;
        float largest = MathF.Max(MathF.Max(size.X, size.Y), size.Z);
        int texels = (int)(ShadowProjection.TexelsPerCasterUnit * largest);
        int side = 1;

        while (side < texels)
        {
            side <<= 1;
        }

        return side;
    }

    /// <summary><c>ComputeShadowBBox</c> (`:3254-3276`): the sphere's box extruded along the direction by the cast distance.</summary>
    private static (Vector3 Min, Vector3 Max) SweptBox(Vector3 centre, float radius, Vector3 dir, float distance)
    {
        Vector3 min = centre - new Vector3(radius);
        Vector3 max = centre + new Vector3(radius);
        Vector3 reach = dir * distance;

        return (
            new Vector3(dir.X < 0f ? min.X + reach.X : min.X, dir.Y < 0f ? min.Y + reach.Y : min.Y, dir.Z < 0f ? min.Z + reach.Z : min.Z),
            new Vector3(dir.X < 0f ? max.X : max.X + reach.X, dir.Y < 0f ? max.Y : max.Y + reach.Y, dir.Z < 0f ? max.Z : max.Z + reach.Z));
    }

    /// <summary><c>CVisibleShadowList::PrioritySort</c>: a selection sort by area, largest first, swapping as the engine does.</summary>
    private static void PrioritySort(List<(ShadowEntity Root, float Area)> visible)
    {
        for (int i = 0; i < visible.Count - 1; i++)
        {
            int largest = i;
            float area = visible[i].Area;

            for (int j = i + 1; j < visible.Count; j++)
            {
                if (area < visible[j].Area)
                {
                    largest = j;
                    area = visible[j].Area;
                }
            }

            (visible[i], visible[largest]) = (visible[largest], visible[i]);
        }
    }

    private void Index(IReadOnlyList<ShadowEntity> entities)
    {
        _byIndex.Clear();

        foreach (List<int> list in _children.Values)
        {
            list.Clear();
        }

        foreach (ShadowEntity entity in entities)
        {
            _byIndex[entity.EntityIndex] = entity;
        }

        foreach (ShadowEntity entity in entities)
        {
            if (entity.ShadowParent is { } parent && _byIndex.ContainsKey(parent))
            {
                if (!_children.TryGetValue(parent, out List<int>? list))
                {
                    list = [];
                    _children[parent] = list;
                }

                list.Add(entity.EntityIndex);
            }
        }
    }

    /// <summary><c>ShouldUseParentShadow</c> (`:2845-2865`): a parent that casts takes the child; one that does not is passed through.</summary>
    private bool UsesParentShadow(ShadowEntity entity, int depth)
    {
        if (depth >= MaxDepth || entity.ShadowParent is not { } parent || !_byIndex.TryGetValue(parent, out ShadowEntity above))
        {
            return false;
        }

        return above.Cast != ShadowCast.None || UsesParentShadow(above, depth + 1);
    }

    /// <summary><c>DrawShadowHierarchy</c> (`:3650-3704`): the caster, then each move child that casts, through any that do not.</summary>
    private List<int> Hierarchy(ShadowEntity root)
    {
        List<int> drawn = [];
        Visit(root.EntityIndex, drawn, 0);
        return drawn;
    }

    private void Visit(int entity, List<int> drawn, int depth)
    {
        if (_byIndex[entity].Cast != ShadowCast.None)
        {
            drawn.Add(entity);
        }

        if (depth >= MaxDepth || !_children.TryGetValue(entity, out List<int>? children))
        {
            return;
        }

        foreach (int child in children)
        {
            Visit(child, drawn, depth + 1);
        }
    }
}
