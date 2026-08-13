using System;

namespace Tf2DemoSalvage.Core.Bsp;

/// <summary>
/// Where a model's vertices land once the map has placed it.
/// </summary>
/// <remarks>
/// **The rotation is Valve's own <c>AngleMatrix</c>**, transcribed from
/// <c>src/mathlib/mathlib_base.cpp</c> in <c>source-sdk-2013</c> rather than derived. Euler angles
/// have a convention per engine — which axis each component turns about, in which order, and which
/// way is positive — and a plausible-looking guess produces props that stand in the right places
/// facing the wrong way. That is a picture nobody can check without knowing the map.
///
/// A <c>QAngle</c> is <c>(pitch, yaw, roll)</c>: pitch about the side axis, yaw about the vertical,
/// roll about the forward. Almost every prop in a map uses yaw alone.
///
/// The three columns are the model's own axes expressed in world space, so a vertex is
/// <c>origin + scale * (x*forward + y*left + z*up)</c>. Written out rather than kept as a matrix
/// type because this is the only place in the project that needs one.
/// </remarks>
public readonly record struct PropTransform
{
    private readonly float _originX;
    private readonly float _originY;
    private readonly float _originZ;
    private readonly float _scale;

    private readonly float _m00;
    private readonly float _m01;
    private readonly float _m02;
    private readonly float _m10;
    private readonly float _m11;
    private readonly float _m12;
    private readonly float _m20;
    private readonly float _m21;
    private readonly float _m22;

    /// <summary>Builds the transform a placement describes.</summary>
    /// <param name="prop">The placement, as the map recorded it.</param>
    /// <remarks>
    /// A record struct only so equality comes for free; nothing here compares transforms, and the
    /// analyzers are right that a value type without it is a trap waiting for someone who does.
    /// </remarks>
    public PropTransform(BspStaticProp prop)
    {
        _originX = prop.X;
        _originY = prop.Y;
        _originZ = prop.Z;
        _scale = prop.Scale;

        (float sinYaw, float cosYaw) = MathF.SinCos(Radians(prop.Yaw));
        (float sinPitch, float cosPitch) = MathF.SinCos(Radians(prop.Pitch));
        (float sinRoll, float cosRoll) = MathF.SinCos(Radians(prop.Roll));

        _m00 = cosPitch * cosYaw;
        _m10 = cosPitch * sinYaw;
        _m20 = -sinPitch;

        float cosRollCosYaw = cosRoll * cosYaw;
        float cosRollSinYaw = cosRoll * sinYaw;
        float sinRollCosYaw = sinRoll * cosYaw;
        float sinRollSinYaw = sinRoll * sinYaw;

        _m01 = (sinPitch * sinRollCosYaw) - cosRollSinYaw;
        _m11 = (sinPitch * sinRollSinYaw) + cosRollCosYaw;
        _m21 = sinRoll * cosPitch;

        _m02 = (sinPitch * cosRollCosYaw) + sinRollSinYaw;
        _m12 = (sinPitch * cosRollSinYaw) - sinRollCosYaw;
        _m22 = cosRoll * cosPitch;
    }

    /// <summary>Places one of the model's vertices in the world.</summary>
    /// <param name="x">The vertex, in the model's own space.</param>
    /// <param name="y">The vertex, in the model's own space.</param>
    /// <param name="z">The vertex, in the model's own space.</param>
    /// <returns>Where it stands in the map.</returns>
    public (float X, float Y, float Z) Apply(float x, float y, float z) => (
        _originX + (_scale * ((_m00 * x) + (_m01 * y) + (_m02 * z))),
        _originY + (_scale * ((_m10 * x) + (_m11 * y) + (_m12 * z))),
        _originZ + (_scale * ((_m20 * x) + (_m21 * y) + (_m22 * z))));

    /// <summary>Turns a normal, which takes the rotation but neither the origin nor the scale.</summary>
    /// <param name="x">The normal, in the model's own space.</param>
    /// <param name="y">The normal, in the model's own space.</param>
    /// <param name="z">The normal, in the model's own space.</param>
    /// <returns>The normal in world space.</returns>
    /// <remarks>
    /// **Uniform scale only, which is all a static prop has.** A normal under non-uniform scale
    /// needs the inverse transpose; under a single factor the factor cancels when the normal is
    /// used for lighting, so applying the rotation alone is exact here rather than an
    /// approximation.
    /// </remarks>
    public (float X, float Y, float Z) Rotate(float x, float y, float z) => (
        (_m00 * x) + (_m01 * y) + (_m02 * z),
        (_m10 * x) + (_m11 * y) + (_m12 * z),
        (_m20 * x) + (_m21 * y) + (_m22 * z));

    private static float Radians(float degrees) => degrees * (MathF.PI / 180f);
}
