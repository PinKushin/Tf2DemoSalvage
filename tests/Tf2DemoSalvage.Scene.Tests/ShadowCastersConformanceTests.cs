using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.ProjectedShadows;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>CClientShadowMgr::ComputeShadowTextures</c> and what it calls (`clientshadowmgr.cpp:1059-1162`, `:2845-2865`,
/// `:3225-3276`, `:3650-3810`, `:4001-4109`): which drawn entities get a render-to-texture shadow, what is drawn into
/// it, the redraw budget and the cell each takes (finding 107).
/// </summary>
/// <remarks>
/// **The fixture view** looks down +X from the origin with +Z up, a projection <c>[1][1]</c> of 1 and a 1000-pixel
/// viewport, so a sphere of radius r at distance d along +X has a "diameter" (half its NDC span in pixels,
/// <c>ComputePixelDiameterOfSphere</c>) of 1000 r / d and an area of its square.
/// </remarks>
public sealed class ShadowCastersConformanceTests
{
    private static readonly Vector3 Down = new(0f, 0f, -1f);

    private static readonly ScreenFadeView Screen = new((0f, 0f, 0f), (1f, 0f, 0f), (0f, 0f, 1f), 1f, 1000f);

    private static ShadowEntity Caster(int index, float x, ShadowCast cast = ShadowCast.RenderToTexture, int? parent = null, float half = 4f) =>
        new(index, parent, cast, new Vector3(x, 0f, 0f), (0f, 0f, 0f), new Vector3(-half), new Vector3(half));

    private static IReadOnlyList<CastShadow> Frame(ShadowCasters casters, IReadOnlyList<ShadowEntity> entities, int maxRendered = ShadowCasters.MaxRendered) =>
        casters.Frame(entities, Down, 50f, default, Screen, maxRendered);

    /// <remarks>A hat on a player renders into the player's texture and casts none of its own (`ShouldUseParentShadow`).</remarks>
    [Test]
    public void Frame_AHatOnAPlayer_IsDrawnIntoThePlayersShadow()
    {
        IReadOnlyList<CastShadow> shadows = Frame(
            new ShadowCasters(), [Caster(1, 100f, ShadowCast.Dynamic), Caster(40, 100f, parent: 1)]);

        shadows.Count.ShouldBe(1);
        shadows[0].EntityIndex.ShouldBe(1);
        shadows[0].Hierarchy.ShouldBe([1, 40]);
    }

    /// <remarks>
    /// A parent that casts nothing is passed through: its child renders into the grandparent's texture, and the walk
    /// down `DrawShadowHierarchy` reaches it through the parent without drawing the parent.
    /// </remarks>
    [Test]
    public void Frame_AChildOfANonCastingParent_RendersIntoTheGrandparent()
    {
        IReadOnlyList<CastShadow> shadows = Frame(
            new ShadowCasters(),
            [Caster(1, 100f), Caster(2, 100f, ShadowCast.None, parent: 1), Caster(3, 100f, parent: 2)]);

        shadows.Single().Hierarchy.ShouldBe([1, 3]);
    }

    /// <remarks>With no casting ancestor a child casts its own; a parent that is not drawn is no parent at all.</remarks>
    [Test]
    public void Frame_AChildWithNoCastingAncestor_CastsItsOwn()
    {
        IReadOnlyList<CastShadow> shadows = Frame(
            new ShadowCasters(), [Caster(2, 100f, ShadowCast.None), Caster(3, 100f, parent: 2), Caster(5, 200f, parent: 99)]);

        shadows.Select(shadow => shadow.EntityIndex).ShouldBe([3, 5]);
    }

    /// <remarks>
    /// Three new shadows want drawing and the budget is two: the two largest on screen, the nearest, are drawn, and the
    /// third falls to the blob (<c>DrawRenderToTextureShadowLOD</c>, `:4080-4089`).
    /// </remarks>
    [Test]
    public void Frame_PastTheBudget_DrawsTheLargestOnScreenFirst()
    {
        IReadOnlyList<CastShadow> shadows = Frame(
            new ShadowCasters(), [Caster(3, 300f), Caster(1, 100f), Caster(2, 200f)], maxRendered: 2);

        shadows.Select(shadow => (shadow.EntityIndex, shadow.Blob)).ShouldBe([(1, false), (2, false), (3, true)]);
    }

    /// <remarks>
    /// **The blob** (`DrawRenderToTextureShadowLOD`, `clientshadowmgr.cpp:3816-3826`): `decals/simpleshadow` for both
    /// materials, the texture coordinates (0, 0, 1, 1) — the whole texture, not a cell — and the extra clip planes cleared.
    /// The projection is otherwise the render-to-texture one, since `UpdateStudioShadow` chooses by the cast type, not by
    /// the LOD flag (`:2760-2807`). Nothing is drawn into the atlas for it.
    /// </remarks>
    [Test]
    public void Frame_PastTheBudget_TheBlobSamplesTheWholeTextureWithNoClipPlanesOnTheSameProjection()
    {
        ShadowEntity turned = Caster(1, 100f) with { RenderAngles = (0f, 30f, 0f) };

        CastShadow drawn = Frame(new ShadowCasters(), [turned]).Single();
        CastShadow blob = Frame(new ShadowCasters(), [turned], maxRendered: 0).Single();

        drawn.Projection.ClipPlanes.ShouldNotBeEmpty("the control: a render-to-texture shadow has its extra clip planes");
        drawn.Blob.ShouldBeFalse();
        blob.Blob.ShouldBeTrue();
        (blob.TexOrigin, blob.TexSize).ShouldBe((Vector2.Zero, Vector2.One));
        (drawn.TexOrigin, drawn.TexSize).ShouldBe((drawn.Cell.TexOrigin, drawn.Cell.TexSize));
        blob.Projection.ClipPlanes.ShouldBeEmpty();
        blob.Projection.ShouldBe(drawn.Projection with { ClipPlanes = blob.Projection.ClipPlanes });
        blob.Hierarchy.ShouldBeEmpty("nothing is drawn into the atlas for a blob");
    }

    /// <remarks>
    /// **No cell is a blob too** (`DrawRenderToTextureShadow`, `:3750-3754`): 260 small casters, each a 16 texture, want
    /// the 16-pixel cells, and block 0 holds 256 (`CTextureAllocator::Reset`, `:271-316`); with no smaller size to drop to
    /// the four smallest on screen get none and fall to the blob, with the budget never reached.
    /// </remarks>
    [Test]
    public void Frame_MoreSmallCastersThanCells_TheSmallestOnScreenFallToTheBlob()
    {
        ShadowEntity[] entities = [.. Enumerable.Range(1, 260).Select(index => Caster(index, 1000f + index))];

        IReadOnlyList<CastShadow> shadows = Frame(new ShadowCasters(), entities, maxRendered: 1000);

        shadows.Count.ShouldBe(260);
        shadows.Where(shadow => shadow.Blob).Select(shadow => shadow.EntityIndex).ShouldBe([257, 258, 259, 260]);
    }

    /// <remarks>
    /// A static caster whose cell was kept and is no longer dirty draws from its old texture and does not count against
    /// the budget, so with a budget of one both stay on the second frame. An animating caster is dirty every frame, so
    /// the same budget keeps only the larger.
    /// </remarks>
    [TestCase(ShadowCast.RenderToTexture, 2)]
    [TestCase(ShadowCast.Dynamic, 1)]
    public void Frame_ASecondFrameOnABudgetOfOne_KeepsOnlyWhatNeedsNoRedraw(ShadowCast cast, int expected)
    {
        ShadowCasters casters = new();
        ShadowEntity[] entities = [Caster(1, 100f, cast), Caster(2, 200f, cast)];

        Frame(casters, entities).Count(shadow => !shadow.Blob).ShouldBe(2);

        IReadOnlyList<CastShadow> second = Frame(casters, entities, maxRendered: 1);

        second.Count.ShouldBe(2, "a shadow past the budget is a blob, not gone");
        second.Count(shadow => !shadow.Blob).ShouldBe(expected);
    }

    /// <remarks>
    /// A static caster that TURNS is dirty again (`ANGLES_CHANGED` → `MarkRenderToTextureShadowDirty`,
    /// `baseentity_shared.cpp:1444-1460`), so on the second frame it is redrawn and uses the budget of one, and the clean
    /// caster after it falls to the blob. A new cycle does the same (`ANIMATION_CHANGED`, `:1471-1475`).
    /// </remarks>
    [TestCase(true)]
    [TestCase(false)]
    public void Frame_AStaticCasterThatTurnsOrAnimates_IsRedrawnAndUsesTheBudget(bool turns)
    {
        ShadowCasters casters = new();
        ShadowEntity near = Caster(1, 100f);

        Frame(casters, [near, Caster(2, 200f)]).Count.ShouldBe(2);

        ShadowEntity changed = turns ? near with { RenderAngles = (0f, 30f, 0f) } : near with { Animation = (0, 0.5f) };

        Frame(casters, [changed, Caster(2, 200f)], maxRendered: 1).Select(shadow => (shadow.EntityIndex, shadow.Blob))
            .ShouldBe([(1, false), (2, true)]);
    }

    /// <remarks>
    /// A different model in the same index is a new entity with its own texture, sized for it: an eight-unit prop's 16 cell
    /// gives way to a 256 one when the index next holds a 120-unit model up close.
    /// </remarks>
    [Test]
    public void Frame_ANewModelInAReusedIndex_GetsATextureSizedForIt()
    {
        ShadowCasters casters = new();

        Frame(casters, [Caster(1, 100f) with { Model = "small.mdl" }]).Single().Cell.Width.ShouldBe(16);

        ShadowEntity big = new(1, null, ShadowCast.RenderToTexture, new Vector3(100f, 0f, 0f), (0f, 0f, 0f),
            new Vector3(-60f, -60f, 0f), new Vector3(60f, 60f, 82f), "big.mdl");

        Frame(casters, [big]).Single().Cell.Width.ShouldBe(256);
    }

    /// <remarks>A caster whose swept box is wholly behind the camera is not in view and casts nothing this frame.</remarks>
    [Test]
    public void Frame_ACasterBehindTheView_IsCulled()
    {
        ViewFrustum frustum = ViewFrustum.PerspectiveFromAspect(
            (0f, 0f, 0f), (1f, 0f, 0f), (0f, -1f, 0f), (0f, 0f, 1f), 7f, 28000f, 90f, 1f);

        IReadOnlyList<CastShadow> shadows = new ShadowCasters().Frame(
            [Caster(1, 100f), Caster(2, -100f)], Down, 50f, frustum, Screen);

        shadows.Select(shadow => shadow.EntityIndex).ShouldBe([1]);
    }

    /// <remarks>
    /// The cell size is the power of two the screen area asks for, capped by the texture's own: a player's fattened
    /// bounds are 120 wide, 240 texels, a 256 texture; a sphere of radius √(120² + 120² + 82²) / 2 ≈ 94.2 at 100 units
    /// is 942 "pixels" across, so it takes a whole 256 cell. At 3000 units it is 31.4 across, an area of 987: the first
    /// width whose square covers it is 32.
    /// </remarks>
    [TestCase(100f, 256)]
    [TestCase(3000f, 32)]
    public void Frame_APlayer_TakesTheCellItsScreenAreaAsks(float distance, int side)
    {
        ShadowEntity player = new(1, null, ShadowCast.Dynamic, new Vector3(distance, 0f, 0f), (0f, 0f, 0f), new Vector3(-60f, -60f, 0f), new Vector3(60f, 60f, 82f));

        ShadowCell cell = Frame(new ShadowCasters(), [player]).Single().Cell;

        (cell.Width, cell.Height).ShouldBe((side, side));
    }

    /// <summary>One node on the plane x = 0: leaf 0 in front (x &gt; 0), leaf 1 behind.</summary>
    private static BspLeafTree Wall()
    {
        byte[] plane = new byte[20];
        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(0), 1f);

        byte[] node = new byte[32];
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(4), -0 - 1);
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(8), -1 - 1);

        return BspLeafTree.FromLumps(node, plane, new byte[64]);
    }

    /// <remarks>
    /// **The view's leaves decide** (`EnumerateShadowsInLeaves`, `clientleafsystem.cpp:1078-1105`; `ComputeShadowTextures`
    /// hands it the world list's leaves, `viewrender.cpp:5494`): a shadow is considered only when a leaf its projection
    /// was filed in (`BuildShadowLeafList`, `clientshadowmgr.cpp:2263-2278`) is one the view draws, then frustum-culled.
    /// Both casters stand in front of the wall, in leaf 0; with leaf 1 the only one drawn neither is considered — not even
    /// as a blob — though both are in the frustum.
    /// </remarks>
    [Test]
    public void Frame_ACasterWhoseLeavesTheViewDoesNotDraw_IsNotConsidered()
    {
        ShadowEntity[] entities = [Caster(1, 100f), Caster(2, 200f)];

        new ShadowCasters().Frame(entities, Down, 50f, default, Screen, view: new ShadowLeafView(Wall(), leaf => leaf == 0))
            .Select(shadow => shadow.EntityIndex).ShouldBe([1, 2], "the control: drawn leaves keep both");
        new ShadowCasters().Frame(entities, Down, 50f, default, Screen, view: new ShadowLeafView(Wall(), leaf => leaf == 1))
            .ShouldBeEmpty();
    }

    /// <remarks>
    /// **A shadow is re-projected only when its caster moved or turned** (`UpdateShadow`, `clientshadowmgr.cpp:3132-3156`:
    /// "Figure out if the shadow moved"), or when forced: created (`CreateShadow`, `:1880-1898`), or every shadow when the
    /// direction or distance changes (`UpdateAllShadows` from `SetShadowDirection` / `SetShadowDistance`, `:1629-1709`).
    /// So a caster whose bounds grow in place keeps the projection it had; moving it, or a new direction, rebuilds it.
    /// </remarks>
    [Test]
    public void Frame_ACasterThatOnlyGrows_KeepsItsProjectionUntilItMovesOrTheDirectionChanges()
    {
        ShadowCasters casters = new();
        ShadowEntity small = Caster(1, 100f);
        ShadowEntity grown = small with { Maxs = new Vector3(4f, 4f, 40f) };

        ShadowProjection first = Frame(casters, [small]).Single().Projection;

        ShadowProjection.ForRenderToTexture(small.RenderOrigin, small.RenderAngles, grown.Mins, grown.Maxs, Down, 50f).Origin
            .ShouldNotBe(first.Origin, "the control: the grown bounds project from elsewhere");
        Frame(casters, [grown]).Single().Projection.ShouldBe(first, "bounds alone do not re-project");
        casters.Frame([grown], new Vector3(0.1f, 0f, -1f), 50f, default, Screen).Single().Projection.ShouldNotBe(first);

        ShadowCasters moved = new();

        Frame(moved, [small]);

        ShadowProjection rebuilt = Frame(moved, [grown with { RenderOrigin = new Vector3(101f, 0f, 0f) }]).Single().Projection;
        ShadowProjection expected = ShadowProjection.ForRenderToTexture(
            new Vector3(101f, 0f, 0f), (0f, 0f, 0f), grown.Mins, grown.Maxs, Down, 50f);

        (rebuilt.Origin, rebuilt.Size, rebuilt.MaxHeight).ShouldBe((expected.Origin, expected.Size, expected.MaxHeight));
    }

    /// <remarks>
    /// **The blob's cleared planes stay cleared** (`DrawRenderToTextureShadowLOD`, `:3816-3826`): only
    /// `BuildRenderToTextureShadow` adds them back (`:2560`), and it runs on a re-projection. So a caster that falls to the
    /// blob and gets its cell back without moving casts without planes; once it turns, they return.
    /// </remarks>
    [Test]
    public void Frame_ABlobThatGetsItsCellBackUnmoved_KeepsNoClipPlanesUntilItIsReprojected()
    {
        ShadowCasters casters = new();
        ShadowEntity turned = Caster(1, 100f) with { RenderAngles = (0f, 30f, 0f) };

        Frame(casters, [turned]).Single().Projection.ClipPlanes.ShouldNotBeEmpty("the control: a cell shadow has planes");
        Frame(casters, [turned], maxRendered: 0).Single().Blob.ShouldBeTrue();

        CastShadow back = Frame(casters, [turned]).Single();

        back.Blob.ShouldBeFalse();
        back.Projection.ClipPlanes.ShouldBeEmpty();
        Frame(casters, [turned with { RenderAngles = (0f, 40f, 0f) }]).Single().Projection.ClipPlanes.ShouldNotBeEmpty();
    }

    /// <remarks>
    /// **The planes are cleared on the frame a shadow BECOMES a blob, not every frame it is one**
    /// (`DrawRenderToTextureShadowLOD` tests <c>SHADOW_FLAGS_USING_LOD_SHADOW</c> first, `:3819`), and a re-projection adds
    /// them back with no LOD test (`ComputeExtraClipPlanes`, `:2560`). So a caster held past the budget that turns keeps
    /// its planes as a blob from then on. Within the budget with no cell, the flag was cleared at the top of
    /// <c>DrawRenderToTextureShadow</c> (`:3738-3743`), so there the planes are cleared every frame.
    /// </remarks>
    [Test]
    public void Frame_ABlobPastTheBudgetThatTurns_GetsItsPlanesBackAndKeepsThem()
    {
        ShadowCasters casters = new();
        ShadowEntity turned = Caster(1, 100f) with { RenderAngles = (0f, 30f, 0f) };

        Frame(casters, [turned], maxRendered: 0).Single().Projection.ClipPlanes.ShouldBeEmpty("the transition clears them");

        CastShadow again = Frame(casters, [turned with { RenderAngles = (0f, 40f, 0f) }], maxRendered: 0).Single();

        again.Blob.ShouldBeTrue();
        again.Projection.ClipPlanes.ShouldNotBeEmpty("a re-projection adds them back, and a held blob does not clear them");
    }

    /// <remarks>An eight-unit prop asks for 16 texels, so however near it is it never takes more than a 16 cell.</remarks>
    [Test]
    public void Frame_ASmallPropUpClose_IsCappedAtItsOwnTextureSize() =>
        Frame(new ShadowCasters(), [Caster(1, 10f)]).Single().Cell.Width.ShouldBe(16);

    /// <remarks>A caster no longer drawn gives its cell back, so the next caster takes the same one.</remarks>
    [Test]
    public void Frame_ACasterThatLeaves_FreesItsCell()
    {
        ShadowCasters casters = new();
        ShadowCell first = Frame(casters, [Caster(1, 100f)]).Single().Cell;

        Frame(casters, [Caster(2, 100f)]).Single().Cell.ShouldBe(first);
    }

    /// <remarks>
    /// `SetRenderToTextureShadowTexCoords`: half a texel in, and the span to the last texel centre — a 64 cell at
    /// (128, 256) is u = 128.5 / 1024, du = 63 / 1024.
    /// </remarks>
    [Test]
    public void TexCoords_ACell_StartHalfATexelInAndSpanToTheLastTexelCentre()
    {
        ShadowCell cell = new(128, 256, 64, 64);

        cell.TexOrigin.ShouldBe(new Vector2(128.5f / 1024f, 256.5f / 1024f));
        cell.TexSize.ShouldBe(new Vector2(63f / 1024f, 63f / 1024f));
    }

    /// <remarks>
    /// The caster's projection maps shadow texture space to the cell: the shadow origin (u = v = 0.5, depth 0) to the
    /// centre at depth 1; half the size along the x axis to the right edge; along the y axis to the TOP edge being v = 0
    /// (`Scale( 1, -1, 1 )`); and 9999 down the direction to depth 0.
    /// </remarks>
    [Test]
    public void CasterViewProjection_ShadowTextureSpace_MapsOntoTheCell()
    {
        CastShadow shadow = Frame(new ShadowCasters(), [Caster(1, 100f)]).Single();
        ShadowProjection p = shadow.Projection;
        float[] m = shadow.CasterViewProjection();

        Near(Clip(m, p.Origin), new Vector3(0f, 0f, 1f));
        Near(Clip(m, p.Origin + (p.XAxis * (p.Size.X * 0.5f))), new Vector3(1f, 0f, 1f));
        Near(Clip(m, p.Origin - (p.YAxis * (p.Size.Y * 0.5f))), new Vector3(0f, 1f, 1f));
        Clip(m, p.Origin + (p.Direction * 9999f)).Z.ShouldBe(0f, 1e-4f);
    }

    private static void Near(Vector3 actual, Vector3 expected) =>
        Vector3.Distance(actual, expected).ShouldBeLessThan(1e-5f, $"{actual} is not {expected}");

    private static Vector3 Clip(float[] m, Vector3 w) => new(
        (w.X * m[0]) + (w.Y * m[4]) + (w.Z * m[8]) + m[12],
        (w.X * m[1]) + (w.Y * m[5]) + (w.Z * m[9]) + m[13],
        (w.X * m[2]) + (w.Y * m[6]) + (w.Z * m[10]) + m[14]);
}
