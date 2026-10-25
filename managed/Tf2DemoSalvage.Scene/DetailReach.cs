using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>The map's detail props grouped by leaf, so a view reaches only the leaves near its eye (B580).</summary>
/// <remarks>
/// **The engine never walks every detail prop.** `BuildDetailObjectRenderLists` asks the BSP for the leaves in a
/// sphere of `cl_detaildist` about the eye (`EnumerateLeavesInSphere`, `detailobjectsystem.cpp:2839-2841`), and
/// `EnumerateLeaf` visits only the props of each such leaf (`:2742-2781`). This is that reach, built once per map.
///
/// **A leaf here is a RUN of the lump** — consecutive props with one <see cref="BspDetailProp.Leaf"/> — boxed by its
/// props' own origins and fast-sprite anchors rather than by the BSP leaf's bounds. Every prop sits inside its leaf, so
/// the box is the leaf's tighter subset; and it is a run rather than a leaf because a run boundary is where
/// <see cref="DetailSprites.Build"/> restarts the fast lane's group of four, which keeps the built quads identical to
/// the whole-list walk. The radius is the engine's bare <c>cl_detaildist</c> (<see cref="DetailFade.Radius"/>), which
/// a zoomed FOV factor does not scale, so a zoomed view's larger fade maximum is cut at the sphere as the engine's is.
///
/// **Interpolated, not exact, when zoomed:** the engine reaches whole BSP LEAVES whose region touches the sphere, and
/// a run box is its leaf's tighter subset, so a prop past the sphere in a leaf that touches it is drawn by the engine
/// and not here. At factor 1 the fade maximum equals the sphere and the two agree exactly. Porting
/// <c>EnumerateLeavesInSphere</c> beside <see cref="BspLeafTree.EnumerateLeavesInBox"/> closes it (B580 residual).
/// </remarks>
public sealed class DetailReach
{
    private readonly int[] _start;
    private readonly int[] _end;
    private readonly float[] _box; // min x y z, max x y z per run

    /// <summary>Groups <paramref name="objects"/> into runs and boxes each.</summary>
    /// <param name="objects">The detail props, in lump order.</param>
    /// <param name="sprites">The sprite dictionary, for the fast lane's anchors.</param>
    public DetailReach(IReadOnlyList<BspDetailProp> objects, IReadOnlyList<BspDetailSprite> sprites)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(sprites);

        List<int> starts = [];
        List<float> boxes = [];

        for (int index = 0; index < objects.Count; index++)
        {
            BspDetailProp prop = objects[index];

            if (index == 0 || prop.Leaf != objects[index - 1].Leaf)
            {
                starts.Add(index);
                boxes.AddRange([float.MaxValue, float.MaxValue, float.MaxValue, float.MinValue, float.MinValue, float.MinValue]);
            }

            int at = boxes.Count - 6;
            Grow(boxes, at, prop.Origin);

            if (prop.Type == DetailPropType.Sprite && prop.DetailModel >= 0 && prop.DetailModel < sprites.Count)
            {
                Grow(boxes, at, DetailSprites.Anchor(prop, sprites[prop.DetailModel]));
            }
        }

        _start = [.. starts];
        _end = new int[_start.Length];

        for (int run = 0; run < _start.Length; run++)
        {
            _end[run] = run + 1 < _start.Length ? _start[run + 1] : objects.Count;
        }

        _box = [.. boxes];
    }

    /// <summary>How many runs the lump holds.</summary>
    public int Runs => _start.Length;

    /// <summary>The props of one run, as a half-open index range.</summary>
    public (int Start, int End) Range(int run) => (_start[run], _end[run]);

    /// <summary>Whether any prop of a run can be within <paramref name="radius"/> of the eye.</summary>
    /// <param name="run">The run.</param>
    /// <param name="eye">The view origin.</param>
    /// <param name="radius"><see cref="DetailFade.Radius"/>, the engine's bare <c>cl_detaildist</c>.</param>
    /// <remarks>
    /// Conservative by a unit and a part in 10^5, so float rounding of the per-prop distance can never put a drawn prop
    /// outside a skipped run; a run reached in error builds nothing, as it did before.
    /// </remarks>
    public bool Near(int run, (float X, float Y, float Z) eye, float radius)
    {
        int at = run * 6;
        float dx = Gap(eye.X, _box[at], _box[at + 3]);
        float dy = Gap(eye.Y, _box[at + 1], _box[at + 4]);
        float dz = Gap(eye.Z, _box[at + 2], _box[at + 5]);
        float reach = (MathF.Max(radius, 0f) * 1.00001f) + 1f;

        return (dx * dx) + (dy * dy) + (dz * dz) <= reach * reach;
    }

    private static float Gap(float value, float min, float max) => MathF.Max(MathF.Max(min - value, value - max), 0f);

    private static void Grow(List<float> boxes, int at, (float X, float Y, float Z) point)
    {
        boxes[at] = MathF.Min(boxes[at], point.X);
        boxes[at + 1] = MathF.Min(boxes[at + 1], point.Y);
        boxes[at + 2] = MathF.Min(boxes[at + 2], point.Z);
        boxes[at + 3] = MathF.Max(boxes[at + 3], point.X);
        boxes[at + 4] = MathF.Max(boxes[at + 4], point.Y);
        boxes[at + 5] = MathF.Max(boxes[at + 5], point.Z);
    }
}
