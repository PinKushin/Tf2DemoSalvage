using System;
using System.Collections.Generic;
using System.Collections.Immutable;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// One panel model's flexed mesh (B779): the face its draw hands the studio render for the descriptor weights it computed,
/// rebuilt only when those weights or the model's packing change — a panel paints every frame, and its weights rarely move.
/// </summary>
/// <remarks>
/// **Every draw of a model with flex descriptors flexes its mesh.** <c>CMDL::Draw</c> (TF2 <c>client.dll</c>
/// <c>0x180936f70</c>) tests <c>numflexdesc</c> (<c>0x180937138</c>), locks the studio render's weights
/// (<c>0x180937173</c>), runs the rules into them (<c>0x18093718b</c>) and draws with them (<c>0x1809371f2</c>); a
/// <c>CModelPanelModel</c> is drawn by <c>C_BaseAnimating::DrawModel</c> with the weights its <c>SetupWeights</c> left, as a
/// world model is (<see cref="EntityModelSet"/>'s resting face).
///
/// **A CMDL's delayed weights are taken as its weights — interpolated.** <c>CMDL::Draw</c> asks <c>LockFlexWeights</c> for
/// no delayed array (<c>r9</c> zeroed at <c>0x180937167</c>) and hands <c>DrawModel</c> a null delayed pointer
/// (<c>0x1809371e8</c>); that null is read, studiorender's handling of it is not. A C_BaseFlex hands its own
/// (<c>RunFlexDelay</c>, <c>c_baseflex.cpp:1308-1311</c>), which a caller passes as <c>delayed</c>.
///
/// **A rebuild allocates nothing once the model is known** (round 1 review item 1): the world's route
/// (<c>EntityModelSet.FlexFor</c>, findings 80 and 92) — the moved slots computed once per model and packing, the deltas
/// accumulated into kept scratch cleared only where the slot write reads, and written into the kept stream. The stream is
/// rewritten in place: the HUD paints, then the frame draws what it painted, before the next paint (as the panel's
/// <see cref="StudioFace"/> is).
/// </remarks>
internal sealed class PanelMeshFlex
{
    private float[] _weights = [];

    private float[] _delayed = [];

    private IReadOnlyList<int>? _order;

    private PropModels.ModelFrames? _frames;

    private FlexedCorners? _corners;

    private FlexSlots? _range;

    private float[] _positions = [];

    private float[] _normals = [];

    private float[] _stream = [];

    private float[]? _face;

    /// <summary>How many times the face was accumulated, counted where it is (B243): what a cache hit saves.</summary>
    internal int Rebuilds { get; private set; }

    /// <summary>Whether a model's frames are kept, with the face built from them.</summary>
    internal bool HoldsFrames => _frames is not null;

    /// <summary>The face for these weights.</summary>
    /// <param name="cache">The cache that packed the model.</param>
    /// <param name="path">The model's path.</param>
    /// <param name="frames">The model.</param>
    /// <param name="weights">Its descriptor weights this draw, or null where none are computed; read, never kept.</param>
    /// <param name="delayed">Its delayed weights, or null for a draw that hands none (then the weights); read, never kept.</param>
    /// <returns>The face and its range, or nulls for a model drawn unflexed.</returns>
    public (float[]? Face, FlexSlots? Range) For(
        IMdlCache cache, string path, PropModels.ModelFrames frames, float[]? weights, float[]? delayed = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(frames);

        if (weights is null || !frames.Flex.HasVertexAnimation || frames.CornerVertex.Count == 0
            || cache.PackedOrder(path) is not { } order)
        {
            return (null, null);
        }

        delayed ??= weights;

        bool sameModel = ReferenceEquals(order, _order) && ReferenceEquals(frames, _frames);

        // The arrays may be the caller's own, rewritten each paint (TfPlayerModelPanel's), so copies are compared.
        if (sameModel && _weights.AsSpan().SequenceEqual(weights) && _delayed.AsSpan().SequenceEqual(delayed))
        {
            return (_face, _face is null ? null : _range);
        }

        if (!sameModel)
        {
            _order = order;
            _frames = frames;
            _corners = FaceFlex.FlexedCorners(frames.Flex, frames.CornerVertex, order);
            ImmutableArray<int> at = _corners.At;
            _range = at.Length > 0 ? new FlexSlots(at[0], at[^1] - at[0] + 1) : null;
            _positions = new float[frames.VertexCount * 3];
            _normals = new float[frames.VertexCount * 3];
            _stream = new float[order.Count * 6];
        }

        Kept(ref _weights, weights);
        Kept(ref _delayed, delayed);
        Rebuilds++;

        FaceFlex.ClearMoved(_corners!, _positions, _normals);
        int applied = StudioFlexRules.Accumulate(frames.Flex, weights, delayed, _positions, _normals);
        _face = applied > 0 ? FaceFlex.IntoFlexedSlots((_positions, _normals), _corners!, _stream) : null;

        return (_face, _face is null ? null : _range);
    }

    /// <summary>Drops the kept frames, packing and face, so a level that has ended is not held alive by them.</summary>
    public void Release()
    {
        _weights = [];
        _delayed = [];
        _order = null;
        _frames = null;
        _corners = null;
        _range = null;
        _positions = [];
        _normals = [];
        _stream = [];
        _face = null;
    }

    /// <summary>Copies <paramref name="from"/> into <paramref name="into"/>, resized only when the length changes.</summary>
    private static void Kept(ref float[] into, float[] from)
    {
        if (into.Length != from.Length)
        {
            into = new float[from.Length];
        }

        from.CopyTo(into, 0);
    }
}
