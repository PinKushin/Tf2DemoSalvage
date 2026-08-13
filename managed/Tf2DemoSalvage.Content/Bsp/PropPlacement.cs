using System;

namespace Tf2DemoSalvage.Content.Bsp;

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
        : this(prop.X, prop.Y, prop.Z, prop.Pitch, prop.Yaw, prop.Roll, prop.Scale)
    {
    }

    /// <summary>Builds the transform for anything placed anywhere.</summary>
    /// <param name="x">World position.</param>
    /// <param name="y">World position.</param>
    /// <param name="z">World position.</param>
    /// <param name="pitch">Rotation about the side axis, in degrees.</param>
    /// <param name="yaw">Rotation about the vertical axis, in degrees.</param>
    /// <param name="roll">Rotation about the forward axis, in degrees.</param>
    /// <param name="scale">Size relative to the model as authored.</param>
    /// <remarks>
    /// **The same transform serves a static prop and a networked entity**, because in the engine
    /// it is the same transform: a placement and an entity both reduce to an origin, a QAngle and
    /// a scale, and <c>AngleMatrix</c> turns those into a matrix without caring which produced
    /// them. The map-file constructor delegates here so Valve's rotation exists once — a second
    /// copy for entities would be a second chance to get the axis order wrong.
    /// </remarks>
    public PropTransform(
        float x, float y, float z, float pitch, float yaw, float roll, float scale)
    {
        _originX = x;
        _originY = y;
        _originZ = z;
        _scale = scale;

        (float sinYaw, float cosYaw) = MathF.SinCos(Radians(yaw));
        (float sinPitch, float cosPitch) = MathF.SinCos(Radians(pitch));
        (float sinRoll, float cosRoll) = MathF.SinCos(Radians(roll));

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

    /// <summary>The same placement as a matrix, for geometry the GPU transforms.</summary>
    /// <returns>Sixteen floats, row major, for <c>mul(float4(position, 1), matrix)</c>.</returns>
    /// <remarks>
    /// **The same arithmetic as <see cref="Apply"/>, restated rather than reimplemented**, which
    /// is the whole point: a vertex placed on the processor and one placed by this matrix must
    /// land in the same spot, and a test asserts they do. The failure otherwise is a model that
    /// sits slightly wrong — the kind of thing that reads as a bad animation rather than as a
    /// wrong formula.
    ///
    /// This is what lets a model's vertices stay in a static buffer in model space and be posed by
    /// the GPU, which is the engine's arrangement: <c>LoadBoneMatrix</c> hands the transform to the
    /// shader and the vertices never move on the processor at all.
    ///
    /// Row major with the translation in the last row, matching the camera matrix and the
    /// <c>row_major</c> declaration in the shader.
    /// </remarks>
    public float[] ToMatrix() =>
    [
        _scale * _m00, _scale * _m10, _scale * _m20, 0f,
        _scale * _m01, _scale * _m11, _scale * _m21, 0f,
        _scale * _m02, _scale * _m12, _scale * _m22, 0f,
        _originX, _originY, _originZ, 1f,
    ];

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
