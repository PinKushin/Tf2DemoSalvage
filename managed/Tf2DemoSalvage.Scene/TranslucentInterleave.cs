using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene;

/// <summary>What one step of the translucent pass draws.</summary>
public enum InterleaveKind
{
    /// <summary>A leaf's translucent world surfaces; the index is the leaf's place.</summary>
    World,

    /// <summary>One translucent entity; the index is its place in the caller's list.</summary>
    Entity,

    /// <summary>A leaf's detail sprites not yet drawn; the index is the leaf's place (B434).</summary>
    Detail,

    /// <summary>
    /// The detail sprites of an entity's leaf farther than that entity; the index is the entity's place (B434).
    /// </summary>
    DetailBeyond,
}

/// <summary>One step of the translucent pass.</summary>
/// <param name="Kind">What it draws.</param>
/// <param name="Index">A leaf's place, or an entity's place in the caller's list — see <see cref="InterleaveKind"/>.</param>
public readonly record struct InterleaveStep(InterleaveKind Kind, int Index)
{
    /// <summary>Whether this step draws a translucent entity.</summary>
    public bool IsEntity => Kind == InterleaveKind.Entity;
}

/// <summary>
/// The order <c>CRendering3dView::DrawTranslucentRenderables</c> draws the translucent world and the translucent
/// entities in (<c>viewrender.cpp:4465</c>), as a list of steps (B426).
/// </summary>
/// <remarks>
/// The world list's leaves are front to back, and each translucent entity carries the place of its leaf in that list
/// (<c>m_iWorldListInfoLeaf</c>, the nearest leaf it touches — <c>ComputeTranslucentRenderLeaf</c>,
/// <c>clientleafsystem.cpp:1400</c>). Starting at the last leaf (<c>:4554</c>), for each entity walked backwards the
/// leaves from the last one drawn down to and including the entity's are drawn (<c>:4583</c>, the <c>&gt;=</c> loop in
/// <c>DrawTranslucentWorldAndDetailPropsInLeaves</c> at <c>:4302</c>), then every entity of that leaf (<c>:4601</c>,
/// <c>:4647</c>); the leaves left are drawn after the loop (<c>:4695</c>).
///
/// **The detail sprites go in the same walk** (B434). <c>DrawTranslucentWorldAndDetailPropsInLeaves</c> queues a
/// leaf's sprites after its surfaces (<c>:4316-4320</c>) and flushes the queue before the next leaf's surfaces
/// (<c>:4308-4310</c>), so a leaf's sprites draw after its surfaces and before anything nearer. In an entity's leaf
/// the other queued leaves go first (<c>:4594-4598</c>), then before each entity the leaf's sprites farther than it
/// (<c>:4605-4607</c>), then the rest (<c>:4639</c>); an entity leaf with no sprites is not split (<c>:4643</c>).
/// Flushing the queue as late as the engine does draws the same sprites in the same order, because nothing else
/// draws between — except that <c>RenderTranslucentDetailObjects</c> draws the queued leaves' "fast" sprites before
/// their ordinary ones (<c>detailobjectsystem.cpp:2404</c>), a split this port does not make (filed with B434).
/// </remarks>
public static class TranslucentInterleave
{
    /// <summary>Plans the translucent pass with no detail sprites.</summary>
    /// <param name="leafCount">How many leaves the world list holds.</param>
    /// <param name="entityLeaves">Each entity's leaf place, in list order — ascending by leaf, as the engine collates.</param>
    /// <param name="into">Cleared, then filled with the steps in draw order.</param>
    /// <exception cref="ArgumentNullException">A list is null.</exception>
    public static void Plan(int leafCount, IReadOnlyList<int> entityLeaves, IList<InterleaveStep> into) =>
        Plan(leafCount, entityLeaves, static _ => false, into);

    /// <summary>Plans the translucent pass.</summary>
    /// <param name="leafCount">How many leaves the world list holds.</param>
    /// <param name="entityLeaves">Each entity's leaf place, in list order — ascending by leaf, as the engine collates.</param>
    /// <param name="hasDetail">
    /// Whether a leaf place has detail sprites to draw — <c>ShouldDrawDetailObjectsInLeaf</c> (<c>clientleafsystem.cpp:1380</c>).
    /// </param>
    /// <param name="into">Cleared, then filled with the steps in draw order.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static void Plan(
        int leafCount, IReadOnlyList<int> entityLeaves, Func<int, bool> hasDetail, IList<InterleaveStep> into)
    {
        ArgumentNullException.ThrowIfNull(entityLeaves);
        ArgumentNullException.ThrowIfNull(hasDetail);
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();

        int previous = leafCount - 1;
        int entity = entityLeaves.Count - 1;

        while (entity >= 0)
        {
            int leaf = entityLeaves[entity];

            for (; previous > leaf; previous--)
            {
                Leaf(previous, hasDetail, into);
            }

            if (previous == leaf)
            {
                into.Add(new InterleaveStep(InterleaveKind.World, leaf));
            }

            previous = leaf - 1;

            bool detail = leaf >= 0 && leaf < leafCount && hasDetail(leaf);

            for (; entity >= 0 && entityLeaves[entity] == leaf; entity--)
            {
                if (detail)
                {
                    into.Add(new InterleaveStep(InterleaveKind.DetailBeyond, entity));
                }

                into.Add(new InterleaveStep(InterleaveKind.Entity, entity));
            }

            if (detail)
            {
                into.Add(new InterleaveStep(InterleaveKind.Detail, leaf));
            }
        }

        for (; previous >= 0; previous--)
        {
            Leaf(previous, hasDetail, into);
        }
    }

    /// <summary>A leaf with no entity: its surfaces, then its sprites.</summary>
    private static void Leaf(int place, Func<int, bool> hasDetail, IList<InterleaveStep> into)
    {
        into.Add(new InterleaveStep(InterleaveKind.World, place));

        if (hasDetail(place))
        {
            into.Add(new InterleaveStep(InterleaveKind.Detail, place));
        }
    }
}

/// <summary>
/// One view's detail sprite quads grouped by BSP leaf, each leaf's farthest first, with the engine's per-leaf draw
/// cursor (B434).
/// </summary>
/// <remarks>
/// <c>SortSpritesBackToFront</c> sorts one leaf's sprites by squared distance, farthest first
/// (<c>detailobjectsystem.cpp:2042</c>, <c>SortLessFunc</c> at <c>:2035</c>), and
/// <c>RenderTranslucentDetailObjectsInLeaf</c> draws from <c>m_nFirstSprite</c> while
/// <c>m_flDistance &gt;= flMinDistance</c>, advancing it (<c>:2708</c>); <c>BeginTranslucentDetailRendering</c>
/// resets it each view (<c>:1562</c>).
/// </remarks>
public sealed class DetailSpriteLeaves
{
    private readonly List<float> _squared = [];
    private readonly Dictionary<int, (int First, int Count)> _leaves = [];
    private readonly Dictionary<int, int> _drawn = [];
    private int _lastLeaf = int.MinValue;

    /// <summary>Forgets every quad.</summary>
    public void Clear()
    {
        _squared.Clear();
        _leaves.Clear();
        _drawn.Clear();
        _lastLeaf = int.MinValue;
    }

    /// <summary>Records the next quad — leaves contiguous, each leaf's quads farthest first.</summary>
    /// <param name="leaf">The BSP leaf the sprite sits in.</param>
    /// <param name="squared">Its squared distance from the eye.</param>
    public void Add(int leaf, float squared)
    {
        if (leaf != _lastLeaf)
        {
            _leaves[leaf] = (_squared.Count, 0);
            _lastLeaf = leaf;
        }

        (int first, int count) = _leaves[leaf];
        _leaves[leaf] = (first, count + 1);
        _squared.Add(squared);
    }

    /// <summary>Whether a BSP leaf has any sprite this view.</summary>
    /// <param name="leaf">The BSP leaf.</param>
    /// <returns>True when it has at least one.</returns>
    public bool Has(int leaf) => _leaves.ContainsKey(leaf);

    /// <summary>Rewinds every leaf's cursor — <c>BeginTranslucentDetailRendering</c>.</summary>
    public void Begin() => _drawn.Clear();

    /// <summary>Takes a leaf's next undrawn quads, advancing its cursor.</summary>
    /// <param name="leaf">The BSP leaf.</param>
    /// <param name="nearest">
    /// The squared distance of the entity about to draw: only quads at least this far are taken. Null takes the rest.
    /// </param>
    /// <returns>The first quad and how many, in the order <see cref="Add"/> was called.</returns>
    public (int First, int Count) Take(int leaf, float? nearest)
    {
        if (!_leaves.TryGetValue(leaf, out (int First, int Count) range))
        {
            return (0, 0);
        }

        int start = range.First + _drawn.GetValueOrDefault(leaf);
        int end = range.First + range.Count;
        int at = start;

        while (at < end && (nearest is not { } limit || _squared[at] >= limit))
        {
            at++;
        }

        _drawn[leaf] = at - range.First;

        return (start, at - start);
    }
}

/// <summary>The world's translucent runs for one view, grouped by the place of their leaf in the world list.</summary>
/// <remarks>
/// What <c>render-&gt;DrawTranslucentSurfaces( list, leaf, … )</c> draws for one leaf (<c>viewrender.cpp:4313</c>),
/// for every leaf at once. Leaf place <c>p</c> holds <c>Runs[Starts[p] .. Starts[p + 1])</c>, in draw order.
/// </remarks>
public sealed class TranslucentLeafRuns
{
    private readonly IReadOnlyList<int> _starts;

    /// <summary>Groups runs by leaf place.</summary>
    /// <param name="runs">Every run, leaf by leaf.</param>
    /// <param name="starts">Where each leaf's runs begin, plus one entry past the last.</param>
    /// <exception cref="ArgumentNullException">A list is null.</exception>
    public TranslucentLeafRuns(IReadOnlyList<WorldBatch> runs, IReadOnlyList<int> starts)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(starts);

        Runs = runs;
        _starts = starts;
    }

    /// <summary>Every run, leaf by leaf.</summary>
    public IReadOnlyList<WorldBatch> Runs { get; }

    /// <summary>How many leaf places there are.</summary>
    public int LeafCount => Math.Max(0, _starts.Count - 1);

    /// <summary>Where one leaf place's runs are in <see cref="Runs"/>.</summary>
    /// <param name="position">The leaf's place in the world list.</param>
    /// <returns>The first run and how many; none for a place outside the list.</returns>
    public (int First, int Count) Leaf(int position) =>
        position >= 0 && position < LeafCount
            ? (_starts[position], _starts[position + 1] - _starts[position])
            : (0, 0);
}
