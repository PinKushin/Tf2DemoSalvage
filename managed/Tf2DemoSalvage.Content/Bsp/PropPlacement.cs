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

    /// <summary>No move, no turn, no scale.</summary>
    /// <remarks>
    /// **Not <c>default</c>, which is a very different thing.** A defaulted <c>PropTransform</c> has
    /// every field at zero including the scale and the rotation's diagonal, so it collapses a model
    /// to a point at the origin. This is the one a caller means when it says "leave it where it is",
    /// and it is what a model whose BONES already carry its placement needs (D88): the bones are in
    /// world space, so applying a second transform would move it twice.
    /// </remarks>
    public static PropTransform Identity { get; } = new(0f, 0f, 0f, 0f, 0f, 0f, 1f);

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

    /// <summary>Builds a transform from an origin and a rotation already in matrix form.</summary>
    /// <remarks>
    /// **Private, because a rotation given as nine loose floats is trivially corruptible** — a
    /// caller can hand in a matrix that is not a rotation at all and nothing here would notice. The
    /// only user is <see cref="Concat"/>, which produces one by multiplying two rotations and so
    /// cannot produce anything else.
    /// </remarks>
    private PropTransform(
        float x, float y, float z, float scale,
        float m00, float m01, float m02,
        float m10, float m11, float m12,
        float m20, float m21, float m22)
    {
        _originX = x;
        _originY = y;
        _originZ = z;
        _scale = scale;
        _m00 = m00;
        _m01 = m01;
        _m02 = m02;
        _m10 = m10;
        _m11 = m11;
        _m12 = m12;
        _m20 = m20;
        _m21 = m21;
        _m22 = m22;
    }

    /// <summary>Where this transform puts its own origin.</summary>
    public float OriginX => _originX;

    /// <summary>Where this transform puts its own origin.</summary>
    public float OriginY => _originY;

    /// <summary>Where this transform puts its own origin.</summary>
    public float OriginZ => _originZ;

    /// <summary>This transform composed with one expressed in its space.</summary>
    /// <param name="child">The child's transform, relative to this one.</param>
    /// <returns>The child's transform in the space this one is expressed in.</returns>
    /// <remarks>
    /// **Valve's <c>ConcatTransforms</c>** (<c>mathlib_base.cpp:658</c>), which for a 3×4 matrix is
    /// the rotations multiplied and the child's translation carried through the parent's rotation
    /// before the parent's own is added:
    ///
    /// <code>
    ///   out[i][j] = in1[i][0]*in2[0][j] + in1[i][1]*in2[1][j] + in1[i][2]*in2[2][j]
    ///   // then, "add in translation vector":
    ///   out[i][3] += in1[i][3]
    /// </code>
    ///
    /// The shipped implementation is SIMD and multiplies the full four-wide rows, so column three
    /// falls out of the same expression; the masked add at the end (<c>mathlib_base.cpp:706</c>) is
    /// what puts the parent's own translation back. This is that arithmetic written out.
    ///
    /// **Scale multiplies**, which Valve's <c>matrix3x4_t</c> has no field for — a static prop's
    /// uniform scale lives beside the matrix here. A parent at half size holding a child at double
    /// leaves the child at its authored size, which is the only composition that makes sense and
    /// the only one a nested placement can mean.
    /// </remarks>
    public PropTransform Concat(PropTransform child)
    {
        // Rotation: this ∘ child, row by row. Written out rather than looped because the fields are
        // separate floats — a record struct rather than an array, for the reason the type's own
        // remarks give.
        float m00 = (_m00 * child._m00) + (_m01 * child._m10) + (_m02 * child._m20);
        float m01 = (_m00 * child._m01) + (_m01 * child._m11) + (_m02 * child._m21);
        float m02 = (_m00 * child._m02) + (_m01 * child._m12) + (_m02 * child._m22);

        float m10 = (_m10 * child._m00) + (_m11 * child._m10) + (_m12 * child._m20);
        float m11 = (_m10 * child._m01) + (_m11 * child._m11) + (_m12 * child._m21);
        float m12 = (_m10 * child._m02) + (_m11 * child._m12) + (_m12 * child._m22);

        float m20 = (_m20 * child._m00) + (_m21 * child._m10) + (_m22 * child._m20);
        float m21 = (_m20 * child._m01) + (_m21 * child._m11) + (_m22 * child._m21);
        float m22 = (_m20 * child._m02) + (_m21 * child._m12) + (_m22 * child._m22);

        // **The child's origin is SCALED by this one before being rotated into place.** A parent
        // drawn at half size holds its children half as far away, which is what a nested placement
        // means; leaving the scale out moves a child correctly only while the parent is at 1.
        (float x, float y, float z) = Rotate(
            child._originX * _scale, child._originY * _scale, child._originZ * _scale);

        return new PropTransform(
            _originX + x,
            _originY + y,
            _originZ + z,
            _scale * child._scale,
            m00, m01, m02,
            m10, m11, m12,
            m20, m21, m22);
    }

    /// <summary>The angles this transform's rotation represents, in degrees.</summary>
    /// <returns>Pitch, yaw and roll.</returns>
    /// <remarks>
    /// **Valve's <c>MatrixAngles</c>** (<c>mathlib_base.cpp:208</c>), both branches:
    ///
    /// <code>
    ///   forward = ( m[0][0], m[1][0], m[2][0] );  left = ( m[0][1], m[1][1], m[2][1] );
    ///   xyDist  = sqrt( forward.x^2 + forward.y^2 );
    ///   if ( xyDist &gt; 0.001f ) {
    ///       yaw   = atan2(  forward.y, forward.x );
    ///       pitch = atan2( -forward.z, xyDist );
    ///       roll  = atan2(  left.z,    up.z );
    ///   } else {                       // forward is mostly Z, gimbal lock
    ///       yaw   = atan2( -left.x,    left.y );
    ///       pitch = atan2( -forward.z, xyDist );
    ///       roll  = 0;                 // "one degree of freedom has been lost"
    ///   }
    /// </code>
    ///
    /// **The gimbal-lock branch is not an edge case worth skipping**: pitch 90 is an overhead
    /// camera and a falling entity, and the branch DISCARDS roll rather than computing a degenerate
    /// one. An implementation using the general formula throughout returns whatever the degenerate
    /// <c>atan2</c> produced, which is not zero and not stable.
    /// </remarks>
    public (float Pitch, float Yaw, float Roll) Angles()
    {
        // Valve reads columns: forward is column 0, left is column 1, and only `up.z` is needed.
        float forwardX = _m00;
        float forwardY = _m10;
        float forwardZ = _m20;
        float leftX = _m01;
        float leftY = _m11;
        float leftZ = _m21;
        float upZ = _m22;

        float xyDist = MathF.Sqrt((forwardX * forwardX) + (forwardY * forwardY));

        float pitch = Degrees(MathF.Atan2(-forwardZ, xyDist));

        return xyDist > GimbalLimit
            ? (pitch, Degrees(MathF.Atan2(forwardY, forwardX)), Degrees(MathF.Atan2(leftZ, upZ)))
            : (pitch, Degrees(MathF.Atan2(-leftX, leftY)), 0f);
    }

    /// <summary>A parented entity's absolute angles, as <c>CalcAbsolutePosition</c> decides them.</summary>
    /// <param name="parentToWorld">The parent's transform, or its attachment's.</param>
    /// <param name="parentAngles">The parent's own absolute angles, for the copy shortcut.</param>
    /// <param name="localAngles">The child's own angles, in the parent's space.</param>
    /// <param name="parentAttachment">Which attachment it hangs from, or zero for none.</param>
    /// <returns>Pitch, yaw and roll in world space.</returns>
    /// <remarks>
    /// **The shortcut is Valve's and is the common case** (<c>c_baseentity.cpp:4406</c>):
    ///
    /// <code>
    ///   if ( m_angRotation == vec3_angle &amp;&amp; m_iParentAttachment == 0 )
    ///       VectorCopy( m_pMoveParent-&gt;GetAbsAngles(), m_angAbsRotation );
    ///   else
    ///       MatrixAngles( m_rgflCoordinateFrame, m_angAbsRotation );
    /// </code>
    ///
    /// A prop parented to a door usually carries no rotation of its own, so this branch is taken
    /// far more often than the other — and it COPIES rather than extracting. Extraction would be
    /// right to within a rounding error everywhere and wrong at gimbal lock, and "within a rounding
    /// error" is not what the engine does.
    ///
    /// **Both halves of the condition matter.** An entity hung on an attachment point takes that
    /// attachment's orientation, which is not the parent's own, so zero local angles are not
    /// sufficient on their own.
    /// </remarks>
    public static (float Pitch, float Yaw, float Roll) AbsoluteAngles(
        PropTransform parentToWorld,
        (float Pitch, float Yaw, float Roll) parentAngles,
        (float Pitch, float Yaw, float Roll) localAngles,
        int parentAttachment)
    {
        // **The parent's STORED angles, not its matrix's.** `VectorCopy( m_pMoveParent->
        // GetAbsAngles(), ... )` copies a value the parent already holds; deriving the same angles
        // back out of the parent's rotation is a round trip through two transcendental functions
        // and returns 19.999998 for 20. The conformance test asserts exact equality here precisely
        // to keep this honest, and it caught the first attempt doing the round trip.
        if (localAngles == default && parentAttachment == 0)
        {
            return parentAngles;
        }

        return parentToWorld
            .Concat(new PropTransform(
                0f, 0f, 0f, localAngles.Pitch, localAngles.Yaw, localAngles.Roll, 1f))
            .Angles();
    }

    /// <summary>Where <c>MatrixAngles</c> stops trusting the forward vector's XY length.</summary>
    /// <remarks><c>if ( xyDist &gt; 0.001f )</c>, <c>mathlib_base.cpp:233</c>.</remarks>
    private const float GimbalLimit = 0.001f;

    private static float Radians(float degrees) => degrees * (MathF.PI / 180f);

    private static float Degrees(float radians) => radians * (180f / MathF.PI);
}
