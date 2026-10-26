using System;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>The model matrices of one instance list, written in place each time the list is rebuilt (B585).</summary>
/// <remarks>
/// **The engine's shape:** <c>C_BaseAnimating::InternalDrawModel</c> builds the model-to-world matrix on every draw
/// into its stack <c>ClientModelRenderInfo_t</c> (<c>c_baseanimating.cpp:3464</c>, <c>AngleMatrix</c> at
/// <c>:3491</c>) — no heap and no compare against last frame. So here: one array per slot, overwritten by
/// <see cref="Next"/>, grown by doubling and never shrunk. The failed copy-on-change cache this replaces cost a
/// lookup, a transform compare and a branch per prop and measured slower (RISKS B585).
///
/// **Owned per instance LIST, not per pass name or frame**, so a matrix is rewritten exactly when the list
/// holding it is cleared — which happens after <c>FinishDrawBeforePose</c>, the same wait that makes reusing
/// the lists themselves safe under the queued draw.
/// </remarks>
internal sealed class InstanceMatrices
{
    private float[][] _slots = [];
    private int _used;

    /// <summary>Starts the list's rebuild: every slot is free to be written again.</summary>
    public void Reset() => _used = 0;

    /// <summary>The next slot, holding <paramref name="transform"/>'s <see cref="PropTransform.ToMatrix"/>.</summary>
    /// <param name="transform">The placement.</param>
    /// <returns>An array owned by this pool, valid until the next <see cref="Reset"/>.</returns>
    public float[] Next(in PropTransform transform)
    {
        if (_used == _slots.Length)
        {
            Grow();
        }

        float[] matrix = _slots[_used++];
        transform.WriteMatrix(matrix);

        return matrix;
    }

    private void Grow()
    {
        int old = _slots.Length;
        Array.Resize(ref _slots, Math.Max(16, old * 2));

        for (int i = old; i < _slots.Length; i++)
        {
            _slots[i] = new float[16];
        }
    }
}
