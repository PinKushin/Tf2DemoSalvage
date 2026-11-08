using System;
using System.Numerics;

namespace Tf2DemoSalvage.Scene.ProjectedShadows;

/// <summary>What a world surface says about taking a render-to-texture shadow — the engine's surface bits that matter.</summary>
[Flags]
public enum SurfaceShadowBits
{
    /// <summary>Nothing set; the face disabled shadows, so it takes none.</summary>
    None = 0,

    /// <summary>The face's dynamic-shadows bit, `!(m_NumPrims >> 15)` (`Mod_LoadFaces`, bit 0 of `+0x1a`).</summary>
    DynamicShadows = 1,

    /// <summary>`SURFDRAW_NOCULL` (`0x200`): takes a shadow from either side.</summary>
    NoCull = 2,

    /// <summary>`SURFDRAW_TRANS` (`0x20`).</summary>
    Translucent = 4,

    /// <summary>`SURFDRAW_ALPHATEST` (`0x1000`).</summary>
    AlphaTest = 8,

    /// <summary>`SURFDRAW_NOSHADOWS` (`0x2000`), from texinfo `SURF_NOSHADOWS`.</summary>
    NoShadows = 16,
}

/// <summary>
/// Whether a world surface takes a render-to-texture shadow — `engine.dll` `FUN_1801233f0`, the per-surface test of
/// `CShadowMgr::ProjectShadow`'s leaf walk (finding 107).
/// </summary>
/// <remarks>
/// **The engine's test, in order**: the face's dynamic-shadows bit; facing — dot(normal, direction) ≤ −0.01, or for a
/// no-cull surface |dot| ≥ 0.01 either way; the shadow's origin in front of (or on) the plane when the surface faces the
/// shadow, behind (or on) it when a no-cull surface faces away; the bounding sphere crossing the plane,
/// |normal·centre − dist| &lt; radius; and none of translucent, alpha-tested or no-shadows (`flags &amp; 0x3020`). A
/// flashlight bypasses the last; shadows never do.
/// </remarks>
public static class ShadowReceiver
{
    private const float FacingEpsilon = 0.01f;

    private const SurfaceShadowBits Excluding =
        SurfaceShadowBits.Translucent | SurfaceShadowBits.AlphaTest | SurfaceShadowBits.NoShadows;

    /// <summary>Whether a surface takes the shadow.</summary>
    /// <param name="shadow">The shadow.</param>
    /// <param name="normal">The surface's PLANE normal (not flipped for its side).</param>
    /// <param name="distance">The plane's distance.</param>
    /// <param name="flags">The surface's bits.</param>
    /// <returns>True when the engine would add the shadow to it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="shadow"/> is null.</exception>
    public static bool Takes(ShadowProjection shadow, Vector3 normal, float distance, SurfaceShadowBits flags)
    {
        ArgumentNullException.ThrowIfNull(shadow);

        if ((flags & SurfaceShadowBits.DynamicShadows) == 0)
        {
            return false;
        }

        float facing = Vector3.Dot(normal, shadow.Direction);
        float origin = Vector3.Dot(normal, shadow.Origin);

        bool placed;

        if ((flags & SurfaceShadowBits.NoCull) == 0)
        {
            placed = facing <= -FacingEpsilon && distance <= origin;
        }
        else if (MathF.Abs(facing) < FacingEpsilon)
        {
            placed = false;
        }
        else
        {
            placed = facing < 0f ? distance <= origin : origin <= distance;
        }

        return placed &&
            MathF.Abs(Vector3.Dot(normal, shadow.SphereCentre) - distance) < shadow.SphereRadius &&
            (flags & Excluding) == 0;
    }

    /// <summary>
    /// Whether a brush model's surface takes the shadow — `AddShadowToBrushModel` (`engine.dll` `FUN_180121f70`): facing,
    /// <c>dot(direction, normal) ≤ 0</c> in the model's own frame, or no-cull; and none of translucent, alpha-tested or
    /// no-shadows.
    /// </summary>
    /// <param name="direction">The shadow direction rotated into the model's frame by its angles (`VectorIRotate`).</param>
    /// <param name="normal">The surface's plane normal, in the model's frame.</param>
    /// <param name="flags">The surface's bits.</param>
    /// <returns>True when the engine would link the shadow to it.</returns>
    /// <remarks>
    /// **Not the world's test**: no dynamic-shadows bit, no origin side, no sphere, and a threshold of 0 rather than
    /// −0.01. **Its first test, of bit `0x10` (`SURFDRAW_NODRAW`), always passes in this build**: no instruction in
    /// `engine.dll` sets that bit on a surface (a disassembled search of every `or … 0x10`, 2026-11-08), and vbsp emits no
    /// `SURF_NODRAW` face but a displacement's (`writebsp.cpp:435-443`), so it is not tested here (finding 107).
    /// </remarks>
    public static bool TakesOnBrushModel(Vector3 direction, Vector3 normal, SurfaceShadowBits flags) =>
        ((flags & SurfaceShadowBits.NoCull) != 0 || Vector3.Dot(direction, normal) <= 0f) &&
        (flags & Excluding) == 0;
}
