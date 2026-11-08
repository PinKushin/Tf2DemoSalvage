using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
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

    /// <summary>
    /// <c>SHADOWS_SIMPLE</c>: the blob, projected by <c>BuildOrthoShadow</c> (<see cref="ShadowProjection.ForOrtho"/>), never
    /// in the atlas and never enumerated (`EnumShadow` takes only render-to-texture, `clientshadowmgr.cpp:1067-1068`).
    /// </summary>
    Simple,
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
/// <param name="FalloffBias">
/// Its shadow's falloff bias (<c>SetFalloffBias</c>, from <c>ComputeFxBlend</c>): added to every decal corner's fade, and 255
/// draws the shadow not at all.
/// </param>
/// <param name="Ragdoll">
/// Whether it is a simulated corpse, whose every physics update marks its texture dirty (<c>RagdollMoved</c>'s
/// <c>ANIMATION_CHANGED</c>, `c_baseanimating.cpp:4606-4617`).
/// </param>
/// <param name="PhysicsUpdates">A corpse's physics updates so far; a change is an update since the last frame.</param>
public readonly record struct ShadowEntity(
    int EntityIndex,
    int? ShadowParent,
    ShadowCast Cast,
    Vector3 RenderOrigin,
    (float Pitch, float Yaw, float Roll) RenderAngles,
    Vector3 Mins,
    Vector3 Maxs,
    string Model = "",
    (int Sequence, float Cycle) Animation = default,
    byte FalloffBias = 0,
    bool Ragdoll = false,
    int PhysicsUpdates = 0)
{
    /// <summary>Its own cast direction (<c>GetShadowCastDirection</c>), or null for the shadow manager's.</summary>
    public Vector3? CastDirection { get; init; }
}

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

/// <summary>The view's leaves, as <c>ComputeShadowTextures</c> enumerates shadows by them (`clientleafsystem.cpp:1078-1105`).</summary>
/// <param name="Tree">The map's tree, which each shadow's swept box is filed in (<c>BuildShadowLeafList</c>).</param>
/// <param name="Drawn">Whether the view's world list holds a leaf — <c>m_pWorldListInfo-&gt;m_pLeafList</c>.</param>
public readonly record struct ShadowLeafView(BspLeafTree Tree, Func<int, bool> Drawn);

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

    /// <summary>The leaves its projection was filed in (<c>BuildShadowLeafList</c>), or null when no tree was given.</summary>
    public IReadOnlyList<int>? Leaves { get; init; }

    /// <summary>
    /// Whether its cell is drawn this frame — new or dirty (`DrawRenderToTextureShadow`, `:3756-3798`); otherwise the atlas
    /// keeps what it last held there, and a blob or a shadow the view did not enumerate draws nothing into it.
    /// </summary>
    public bool Redraw { get; init; }

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
/// **Which shadows are considered**: those filed in a leaf the view draws (`EnumerateShadowsInLeaves`), given a
/// <see cref="ShadowLeafView"/>, then the frustum.
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

        /// <summary>Its projection as last built, null until the first (<c>m_WorldToShadow</c> and the shadow manager's copy).</summary>
        public ShadowProjection? Projection { get; set; }

        /// <summary>Where and how it stood when last projected — <c>m_LastOrigin</c>, <c>m_LastAngles</c>.</summary>
        public (Vector3 Origin, (float, float, float) Angles) ProjectedFrom { get; set; }

        /// <summary>The leaves that projection was filed in.</summary>
        public IReadOnlyList<int> Leaves { get; set; } = [];

        /// <summary>The tree <see cref="Leaves"/> was walked in, or null when none was given.</summary>
        public BspLeafTree? LeavesIn { get; set; }

        /// <summary><c>SHADOW_FLAGS_USING_LOD_SHADOW</c>: it is the blob, its planes cleared when it became one.</summary>
        public bool UsingLod { get; set; }

        /// <summary>
        /// <c>m_LastAngles = FLT_MAX</c> from a forced <c>AddToDirtyShadowList</c> (`:3004-3007`): re-projected at its next
        /// update whether it moved or not — kept through frames it is too transparent to update.
        /// </summary>
        public bool Forced { get; set; }

        /// <summary>The cell its texture coordinates last named (<c>SetRenderToTextureShadowTexCoords</c>), null before one.</summary>
        public ShadowCell? Cell { get; set; }

        /// <summary>Whether it is a <c>SHADOWS_SIMPLE</c> shadow, with no texture of its own.</summary>
        public bool Simple => Handle == NoTexture;
    }

    /// <summary>A simple shadow's handle: it has no texture.</summary>
    private const int NoTexture = -1;

    /// <summary>A shadow destroyed: its texture, if it has one, given back (`DeallocateTexture`).</summary>
    private void Free(Texture texture)
    {
        if (!texture.Simple)
        {
            _atlas.DeallocateTexture(texture.Handle);
        }
    }

    /// <summary>The direction and distance every shadow was last projected with; a change re-projects all (`UpdateAllShadows`).</summary>
    private (Vector3 Direction, float Distance)? _projectedWith;

    private readonly ShadowTextureAllocator _atlas = new();
    private readonly Dictionary<int, Texture> _textures = [];
    private readonly Dictionary<int, ShadowEntity> _byIndex = [];
    private readonly Dictionary<int, List<int>> _children = [];
    private readonly HashSet<int> _roots = [];
    private readonly List<int> _gone = [];
    private readonly List<(ShadowEntity Root, float Area)> _visible = [];

    /// <summary>Every shadow projected this frame — not too transparent to update — and those the view enumerated.</summary>
    private readonly List<ShadowEntity> _projected = [];
    private readonly HashSet<int> _enumerated = [];

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
    /// <param name="view">The view's leaves, which decide which shadows are considered; null to consider every one the
    /// frustum keeps.</param>
    /// <returns>Every shadow drawn this frame, largest on screen first.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entities"/> is null.</exception>
    /// <remarks>
    /// **The engine's schedule** (`clientshadowmgr.cpp:2868-2959`, `:3077-3167`): every shadow is re-projected in
    /// <c>PreRender</c> only when created, when its caster's render origin or angles changed since its last projection, or
    /// when the direction or distance changed (`UpdateAllShadows`) — bounds alone do not. The rest keep their projection,
    /// their leaves, and the extra clip planes a blob cleared.
    /// </remarks>
    public IReadOnlyList<CastShadow> Frame(
        IReadOnlyList<ShadowEntity> entities,
        Vector3 direction,
        float distance,
        ViewFrustum frustum,
        ScreenFadeView screen,
        int maxRendered = MaxRendered,
        ShadowLeafView? view = null)
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

            bool simple = entity.Cast == ShadowCast.Simple;

            // A different model in the same index is a new entity, so a new shadow sized for it; a shadow that turns from
            // simple to render-to-texture or back is destroyed and made again (`UpdateShadowLOD`'s `DestroyShadow`).
            if (_textures.TryGetValue(entity.EntityIndex, out Texture? held) && (held.Last.Model != entity.Model || held.Simple != simple))
            {
                Free(held);
                _textures.Remove(entity.EntityIndex);
                held = null;
            }

            if (held is null)
            {
                // `CreateShadow` (`:1827-1838`): a texture only for a render-to-texture shadow.
                _textures[entity.EntityIndex] = new Texture
                {
                    Handle = simple ? NoTexture : _atlas.AllocateTexture(TextureSize(entity)),
                    Dirty = true,
                    Last = entity,
                };

                continue;
            }

            // `ANGLES_CHANGED` and `ANIMATION_CHANGED` mark a render-to-texture shadow dirty
            // (`MarkRenderToTextureShadowDirty`, `baseentity_shared.cpp:1444-1475`); a move alone re-projects it only —
            // except a ragdoll's, which `RagdollMoved` marks `ANIMATION_CHANGED` on every physics update — every step any of
            // its bodies is on vphysics' active list (`physics.cpp:449-466`; `IvpRagdoll.AnyAwake`), moved or not.
            if (held.Last.RenderAngles != entity.RenderAngles || held.Last.Animation != entity.Animation ||
                (entity.Ragdoll && held.Last.PhysicsUpdates != entity.PhysicsUpdates))
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
            Free(_textures[caster]);
            _textures.Remove(caster);
        }

        _visible.Clear();
        _projected.Clear();

        // `UpdateAllShadows` from `SetShadowDirection` / `SetShadowDistance` (`:1629-1709`): every shadow forced.
        if (_projectedWith != (dir, distance))
        {
            foreach (Texture each in _textures.Values)
            {
                each.Forced = true;
            }
        }

        _projectedWith = (dir, distance);

        foreach (ShadowEntity entity in entities)
        {
            if (!_roots.Contains(entity.EntityIndex))
            {
                continue;
            }

            Texture texture = _textures[entity.EntityIndex];

            // "Don't bother with it if the shadow is totally transparent": not re-projected, kept dirty, disabled
            // (`UpdateShadow`, `:3101-3110`), and not enumerated (`EnumShadow`, `:1069-1072`).
            if (entity.FalloffBias == byte.MaxValue)
            {
                continue;
            }

            Project(texture, entity, dir, distance, view);
            _projected.Add(entity);

            // `EnumerateShadowsInLeaves`: only a shadow filed in a leaf the view draws (`clientleafsystem.cpp:1086-1103`), and
            // only a render-to-texture one (`EnumShadow`, `:1067-1068`).
            if (texture.Simple || (view is { } v && !AnyDrawn(texture.Leaves, v.Drawn)))
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

        _enumerated.Clear();

        foreach ((ShadowEntity root, float area) in _visible)
        {
            _enumerated.Add(root.EntityIndex);

            Texture texture = _textures[root.EntityIndex];
            IReadOnlyList<int>? leaves = view is null ? null : texture.Leaves;

            // `ComputeShadowTextures`' loop (`:4080-4089`): past the budget, the blob.
            if (rendered >= maxRendered)
            {
                shadows.Add(BlobOf(root, texture, leaves));
                continue;
            }

            // `DrawRenderToTextureShadow` clears the LOD flag before it knows whether there is a cell (`:3738-3743`).
            texture.UsingLod = false;

            bool dirty = texture.Dirty;
            bool newCell = _atlas.UseTexture(texture.Handle, dirty, area);

            // No cell (`DrawRenderToTextureShadow`, `:3750-3754`): the blob, and not counted.
            if (!_atlas.HasValidTexture(texture.Handle))
            {
                shadows.Add(BlobOf(root, texture, leaves));
                continue;
            }

            bool redraw = newCell || dirty;

            if (redraw)
            {
                rendered++;

                // "Only clear the dirty flag if the caster isn't animating" (`:3792-3796`).
                if (root.Cast != ShadowCast.Dynamic)
                {
                    texture.Dirty = false;
                }
            }

            (int x, int y, int width, int height) = _atlas.TextureRect(texture.Handle);
            ShadowCell cell = new(x, y, width, height);

            texture.Cell = cell;

            shadows.Add(new CastShadow(root.EntityIndex, Hierarchy(root), texture.Projection!, cell, root)
            {
                Leaves = leaves,
                Redraw = redraw,
            });
        }

        // **Every other shadow projected this frame keeps its decals** with the texture it last had: `RenderShadows` draws
        // each shadow linked to a surface the view draws, and only the atlas work is gated by the enumeration. Its cell is
        // not redrawn, so it shows what the atlas last held there. One never given a cell or a blob has nothing to show.
        foreach (ShadowEntity root in _projected)
        {
            if (_enumerated.Contains(root.EntityIndex))
            {
                continue;
            }

            Texture texture = _textures[root.EntityIndex];
            IReadOnlyList<int>? leaves = view is null ? null : texture.Leaves;

            // A simple shadow is `m_SimpleShadow` from its creation (`:1827-1838`). *Interpolated:* its texture coordinates,
            // which nothing sets, are the shadow manager's defaults, taken as the whole texture.
            if (texture.UsingLod || texture.Simple)
            {
                shadows.Add(new CastShadow(root.EntityIndex, [], texture.Projection!, default, root) { Blob = true, Leaves = leaves });
            }
            else if (texture.Cell is { } cell)
            {
                shadows.Add(new CastShadow(root.EntityIndex, [], texture.Projection!, cell, root) { Leaves = leaves });
            }
        }

        return shadows;
    }

    /// <summary>
    /// `UpdateShadow`'s gate (`:3132-3156`) and `BuildRenderToTextureShadow` (`:2464-2565`): a new projection, and its leaves,
    /// only when forced or when the caster moved or turned since the last.
    /// </summary>
    private static void Project(Texture texture, ShadowEntity entity, Vector3 shared, float distance, ShadowLeafView? view)
    {
        // `GetShadowDirection( pRenderable )` (`:1690-1699`): the caster may name its own.
        Vector3 dir = entity.CastDirection ?? shared;
        bool moved = texture.ProjectedFrom != (entity.RenderOrigin, entity.RenderAngles);

        if (texture.Projection is null || texture.Forced || moved)
        {
            // `UpdateStudioShadow` (`:2760-2807`): `BuildOrthoShadow` for a simple shadow, else `BuildRenderToTextureShadow`.
            texture.Projection = texture.Simple
                ? ShadowProjection.ForOrtho(entity.RenderOrigin, entity.RenderAngles, entity.Mins, entity.Maxs, dir, distance)
                : ShadowProjection.ForRenderToTexture(entity.RenderOrigin, entity.RenderAngles, entity.Mins, entity.Maxs, dir, distance);
            texture.ProjectedFrom = (entity.RenderOrigin, entity.RenderAngles);
            texture.Forced = false;
            texture.Leaves = view is { } v ? LeavesOf(texture.Projection, v.Tree) : [];
            texture.LeavesIn = view?.Tree;
        }
        else if (view is { } v && !ReferenceEquals(texture.LeavesIn, v.Tree))
        {
            texture.Leaves = LeavesOf(texture.Projection, v.Tree);
            texture.LeavesIn = v.Tree;
        }
    }

    /// <summary><c>BuildShadowLeafList</c> (`:2263-2278`): the leaves along the swept box, half-extent |size| / 2.</summary>
    private static List<int> LeavesOf(ShadowProjection p, BspLeafTree tree)
    {
        Vector3 delta = p.Direction * p.MaxHeight;
        float half = p.Size.Length() * 0.5f;
        List<int> leaves = [];

        tree.EnumerateLeavesAlongSweptBox((p.Origin.X, p.Origin.Y, p.Origin.Z), (delta.X, delta.Y, delta.Z), (half, half, half), leaves);
        return leaves;
    }

    private static bool AnyDrawn(IReadOnlyList<int> leaves, Func<int, bool> drawn)
    {
        foreach (int leaf in leaves)
        {
            if (drawn(leaf))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <c>DrawRenderToTextureShadowLOD</c> (`:3816-3826`): the shadow's own projection, its extra clip planes cleared on
    /// the frame it becomes the blob — kept cleared until it is next re-projected, which adds them back — and no cell.
    /// </summary>
    private static CastShadow BlobOf(ShadowEntity root, Texture texture, IReadOnlyList<int>? leaves)
    {
        if (!texture.UsingLod)
        {
            texture.Projection = texture.Projection! with { ClipPlanes = [] };
            texture.UsingLod = true;
        }

        return new(root.EntityIndex, [], texture.Projection!, default, root) { Blob = true, Leaves = leaves };
    }

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
