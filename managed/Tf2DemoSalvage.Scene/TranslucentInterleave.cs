using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene;

/// <summary>One step of the translucent pass: a leaf's world surfaces, or one translucent entity.</summary>
/// <param name="IsEntity">True for an entity, false for a leaf's translucent world surfaces.</param>
/// <param name="Index">The entity's place in the caller's list, or the leaf's place in the world list.</param>
public readonly record struct InterleaveStep(bool IsEntity, int Index);

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
/// </remarks>
public static class TranslucentInterleave
{
    /// <summary>Plans the translucent pass.</summary>
    /// <param name="leafCount">How many leaves the world list holds.</param>
    /// <param name="entityLeaves">Each entity's leaf place, in list order — ascending by leaf, as the engine collates.</param>
    /// <param name="into">Cleared, then filled with the steps in draw order.</param>
    /// <exception cref="ArgumentNullException">A list is null.</exception>
    public static void Plan(int leafCount, IReadOnlyList<int> entityLeaves, IList<InterleaveStep> into)
    {
        ArgumentNullException.ThrowIfNull(entityLeaves);
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();

        int previous = leafCount - 1;
        int entity = entityLeaves.Count - 1;

        while (entity >= 0)
        {
            int leaf = entityLeaves[entity];

            for (; previous >= leaf; previous--)
            {
                into.Add(new InterleaveStep(false, previous));
            }

            previous = leaf - 1;

            for (; entity >= 0 && entityLeaves[entity] == leaf; entity--)
            {
                into.Add(new InterleaveStep(true, entity));
            }
        }

        for (; previous >= 0; previous--)
        {
            into.Add(new InterleaveStep(false, previous));
        }
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
