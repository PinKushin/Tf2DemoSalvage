namespace Tf2DemoSalvage.Core;

/// <summary>C's float constants as the engine's code uses them, in one place every project reaches.</summary>
public static class ValveFloat
{
    /// <summary>C's <c>FLT_EPSILON</c>, 2⁻²³, the gap above one.</summary>
    /// <remarks>
    /// **Not .NET's <c>float.Epsilon</c>**, which is the smallest subnormal, 2⁻¹⁴⁹: <c>1f + float.Epsilon</c> is one, and
    /// a port that wrote it for <c>FLT_EPSILON</c> twice compared against nothing (B623).
    /// </remarks>
    public const float FltEpsilon = 1.1920929e-7f;
}
