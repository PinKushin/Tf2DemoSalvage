namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>The fields of a core IVP's time-of-impact searches read (B369).</summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *`FUN_1800a1b50` field by field* and *The other three times of
/// impact*). A search reaches a core through its side's real object's <c>+0xe8</c>, or through its motion cache's
/// <c>+0x8</c>, which <c>FUN_1800a0800</c> fills from the same object.
/// </remarks>
/// <param name="Radius">The core's <c>+0x4</c>, set once from the surface's radius.</param>
/// <param name="InverseDiameter">The core's <c>+0x54</c>, <c>0.5f / +0x4</c>.</param>
/// <param name="AngularSpeedBound">The core's <c>+0x80</c>, written each step by <see cref="IvpCoreSpeedBound"/>.</param>
/// <param name="LinearSpeed">The core's <c>+0x1dc</c>, the integrator's <c>|linear velocity|</c>.</param>
/// <param name="SurfaceSpeedBound">The core's <c>+0x254</c>, <c>+0x80 × +0x8</c>.</param>
public readonly record struct IvpCoreBounds(
    float Radius,
    float InverseDiameter,
    float AngularSpeedBound,
    float LinearSpeed,
    float SurfaceSpeedBound);
