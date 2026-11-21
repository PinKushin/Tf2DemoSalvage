using System;
using System.Collections.Generic;

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
/// </remarks>
internal sealed class PanelMeshFlex
{
    private float[] _weights = [];

    private IReadOnlyList<int>? _order;

    private (float[]? Face, FlexSlots? Range) _face;

    /// <summary>The face for these weights.</summary>
    /// <param name="cache">The cache that packed the model.</param>
    /// <param name="path">The model's path.</param>
    /// <param name="frames">The model.</param>
    /// <param name="weights">Its descriptor weights this draw, or null where none are computed; read, never kept.</param>
    /// <returns>The face and its range, or nulls for a model drawn unflexed.</returns>
    public (float[]? Face, FlexSlots? Range) For(IMdlCache cache, string path, PropModels.ModelFrames frames, float[]? weights)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(frames);

        if (weights is null || frames.Flex.Descriptors.Count == 0 || cache.PackedOrder(path) is not { } order)
        {
            return (null, null);
        }

        // The weights array may be the caller's own, rewritten each paint (TfPlayerModelPanel's), so a copy is compared.
        if (!ReferenceEquals(order, _order) || !_weights.AsSpan().SequenceEqual(weights))
        {
            _order = order;
            _weights = (float[])weights.Clone();
            _face = FaceFlex.MeshFace(frames, weights, order);
        }

        return _face;
    }
}
