using System;

namespace Tf2DemoSalvage.Scene;

/// <summary>One side of the view volume, with its normal pointing INWARD.</summary>
/// <param name="NormalX">The plane normal, pointing into the volume.</param>
/// <param name="NormalY">The plane normal, pointing into the volume.</param>
/// <param name="NormalZ">The plane normal, pointing into the volume.</param>
/// <param name="Distance">How far along the normal the plane sits from the origin.</param>
/// <remarks>
/// **Inward, and that is what makes `== 2` mean "cull".** `R_CullBox` drops a box when
/// `BoxOnPlaneSide` answers 2 — wholly on the BACK side — so the back of a frustum plane is outside
/// the view. Getting the sign backwards culls exactly what should be drawn and draws what should
/// not, which on a first look reads as the camera facing the wrong way.
///
/// **`cplane_t` carries `type` and `signbits` beside these four floats and neither is stored here.**
/// `signbits` is a cache of which normal components are negative, used to index an unrolled switch;
/// this project selects the corner directly from the sign, which is the same choice made the same
/// way. `type` names an axis-aligned plane and enables a fast path that a view frustum never takes:
/// `GeneratePerspectiveFrustum` passes `PLANE_ANYZ` (5) for all six planes, and the fast path is
/// gated on `type &lt; 3`. It exists for BSP planes, which are often axial, not for these.
///
/// That last point also disposes of a disagreement inside Valve's own function. The axial path
/// answers 2 for a box whose maximum lies exactly ON the plane, while the general path answers 3;
/// they differ only there, and only the general path can run here.
/// </remarks>
public readonly record struct CullPlane(
    float NormalX, float NormalY, float NormalZ, float Distance)
{
    /// <summary>Which side of this plane a box falls on: 1 in front, 2 behind, 3 straddling.</summary>
    /// <param name="minX">The box's lower corner.</param>
    /// <param name="minY">The box's lower corner.</param>
    /// <param name="minZ">The box's lower corner.</param>
    /// <param name="maxX">The box's upper corner.</param>
    /// <param name="maxY">The box's upper corner.</param>
    /// <param name="maxZ">The box's upper corner.</param>
    /// <returns>Valve's <c>BoxOnPlaneSide</c> answer — never zero.</returns>
    /// <remarks>
    /// **`BoxOnPlaneSide`, general case** (`mathlib_base.cpp:829`). Two corners are projected onto
    /// the normal: <c>dist1</c> the corner furthest ALONG it, <c>dist2</c> the corner furthest
    /// against. Then the two bits:
    ///
    /// <code>
    /// sides = 0;
    /// if (dist1 >= p->dist) sides = 1;
    /// if (dist2 &lt; p->dist) sides |= 2;
    /// </code>
    ///
    /// **The comparisons are deliberately asymmetric** — `&gt;=` for the front bit and a bare
    /// `&lt;` for the back — so a box lying exactly on the plane answers 1 rather than 3. Making
    /// both inclusive would have a coplanar box straddle; making both strict would let it answer 0,
    /// which Valve asserts cannot happen.
    ///
    /// **The corner choice is Valve's eight-case `switch (p->signbits)` written as a select.** Each
    /// case differs only in which of `emins`/`emaxs` supplies each component, chosen by the sign of
    /// that component of the normal, and the three products are summed in the same order — so this
    /// is the same arithmetic on the same operands, not an approximation of it. The switch is an
    /// unroll around a precomputed `signbits`; nothing about the result depends on it.
    /// </remarks>
    public int SideOf(
        float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
    {
        float far =
            (NormalX * (NormalX < 0f ? minX : maxX)) +
            (NormalY * (NormalY < 0f ? minY : maxY)) +
            (NormalZ * (NormalZ < 0f ? minZ : maxZ));

        float near =
            (NormalX * (NormalX < 0f ? maxX : minX)) +
            (NormalY * (NormalY < 0f ? maxY : minY)) +
            (NormalZ * (NormalZ < 0f ? maxZ : minZ));

        int sides = 0;

        if (far >= Distance)
        {
            sides = 1;
        }

        if (near < Distance)
        {
            sides |= 2;
        }

        return sides;
    }
}

/// <summary>
/// The six planes bounding what the camera can see, and the test that drops a box outside them.
/// </summary>
/// <remarks>
/// **`GeneratePerspectiveFrustum` and `R_CullBox`, together** — `mathlib_base.cpp:3923` and
/// `:3973`. The engine culls every renderable against this before it does anything else with it:
/// `CClientLeafSystem::CollateRenderablesInLeaf` computes the world-space AABB and drops it with
/// `engine->CullBox( absMins, absMaxs )` (`clientleafsystem.cpp:1647`), and only what survives is
/// bucketed by size and drawn.
///
/// **A cull is conservative in one direction only, and that is the property that makes it safe.**
/// A box straddling a plane survives, so this can never remove something that should have been
/// drawn; it can only fail to remove something that need not have been. Every visibility structure
/// is built on that asymmetry, and a "tighter" test that drops straddling boxes is not an
/// optimisation but a rendering bug.
///
/// **Where this lives, and why it is not in the renderer.** The camera basis and its angles are
/// here in Scene, and the engine culls in the client leaf system rather than in the material
/// system — visibility is a question about the world, not about a graphics API. Keeping it here
/// also leaves room for the engine's own next step, which is to cull BEFORE posing rather than
/// after: a model outside the view need not have its bones solved at all.
/// </remarks>
public readonly record struct ViewFrustum
{
    /// <summary>How many planes bound the volume.</summary>
    /// <remarks>
    /// `FRUSTUM_NUMPLANES`. Valve's header warns *"there is code that depends on these values"* —
    /// the indices are right, left, top, bottom, near, far, and `R_CullBoxSkipNear` is written as
    /// an explicit list of five rather than a loop that skips one.
    /// </remarks>
    public const int PlaneCount = 6;

    private readonly CullPlane[]? _planes;

    private ViewFrustum(CullPlane[] planes) => _planes = planes;

    /// <summary>Whether this frustum was built, as opposed to being a default value.</summary>
    /// <remarks>
    /// **A default `ViewFrustum` culls nothing rather than everything**, which is the safe direction
    /// for a value type that can be created without a constructor. The opposite default would make
    /// a missing camera look like an empty map.
    /// </remarks>
    public bool IsBuilt => _planes is not null;

    /// <summary>One of the six planes, in Valve's order.</summary>
    /// <param name="index">Right 0, left 1, top 2, bottom 3, near 4, far 5.</param>
    /// <returns>The plane.</returns>
    /// <exception cref="InvalidOperationException">This frustum was never built.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The index is not one of the six.</exception>
    public CullPlane Plane(int index)
    {
        if (_planes is null)
        {
            throw new InvalidOperationException("this frustum was never built");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, PlaneCount);

        return _planes[index];
    }

    /// <summary>Builds the view volume from a camera's position, basis and lens.</summary>
    /// <param name="origin">Where the eye is.</param>
    /// <param name="forward">Unit vector down the view direction.</param>
    /// <param name="right">Unit vector to the camera's right.</param>
    /// <param name="up">Unit vector up from the camera.</param>
    /// <param name="nearZ">The near plane distance.</param>
    /// <param name="farZ">The far plane distance.</param>
    /// <param name="fovX">The FULL horizontal field of view, in degrees.</param>
    /// <param name="fovY">The FULL vertical field of view, in degrees.</param>
    /// <returns>The six planes, normals inward.</returns>
    /// <remarks>
    /// **Both angles are the FULL view angle and are halved here**, exactly as Valve's own comment
    /// insists: *"NOTE: FOV is specified in degrees, as the *full* view angle (not half-angle)"*.
    /// Passing a half-angle produces a frustum half as wide as the picture, which culls things that
    /// are plainly on screen — and does it symmetrically, so it looks like a draw-distance problem
    /// rather than a lens one.
    ///
    /// **`right` and not `left`.** This project's camera derives a left vector for its view matrix
    /// because `VMatrix::GetBasisVectors` hands out forward/left/up; `GeneratePerspectiveFrustum`
    /// wants right. Handing it left swaps which plane is called LEFT and which RIGHT — harmless for
    /// a symmetric frustum and wrong the moment anything asks for a named plane.
    ///
    /// **The planes are normalised even though culling does not need it**, which is Valve's choice
    /// and their comment says so: *"OPTIMIZE: Normalizing these planes is not necessary for
    /// culling"*. Kept, because a normalised plane's distance is a real distance and anything later
    /// that measures rather than compares would silently get a scaled answer otherwise.
    /// </remarks>
    public static ViewFrustum Perspective(
        (float X, float Y, float Z) origin,
        (float X, float Y, float Z) forward,
        (float X, float Y, float Z) right,
        (float X, float Y, float Z) up,
        float nearZ,
        float farZ,
        float fovX,
        float fovY)
    {
        // **A degenerate basis is refused rather than absorbed, and this guard was earned.** A test
        // helper built `right` as `(towards.Y, -towards.X, 0)`, which is the zero vector for a
        // camera looking straight down. Nothing threw: `SidePlane` leaves a zero normal alone rather
        // than dividing by nothing, so two of the six planes silently became something else and the
        // frustum reported an empty world. The hunt went looking for a fault in the BSP walk.
        //
        // Cheap, because this runs on a view change rather than per frame — and the alternative is
        // a frustum that means something other than what its caller asked for.
        if (Dot(forward, forward) < 0.5f || Dot(right, right) < 0.5f || Dot(up, up) < 0.5f)
        {
            throw new ArgumentException(
                "A view frustum needs three unit basis vectors; one of these is degenerate.",
                nameof(forward));
        }

        // The eye's own projection along the view direction, which both depth planes are offset by.
        float intercept = Dot(origin, forward);

        CullPlane[] planes = new CullPlane[PlaneCount];

        // Far is negated in both normal and distance, so "in front of it" means "nearer than far".
        planes[Far] = new CullPlane(-forward.X, -forward.Y, -forward.Z, -farZ - intercept);
        planes[Near] = new CullPlane(forward.X, forward.Y, forward.Z, nearZ + intercept);

        float halfX = fovX * 0.5f;
        float halfY = fovY * 0.5f;

        float tanX = MathF.Tan(halfX * (MathF.PI / 180f));
        float tanY = MathF.Tan(halfY * (MathF.PI / 180f));

        // Each pair is one vector and its reflection through minus twice the basis axis --
        // `VectorMA( right, flTanX, forward, normalPos ); VectorMA( normalPos, -2, right, normalNeg )`.
        (float X, float Y, float Z) positive = Add(right, forward, tanX);
        (float X, float Y, float Z) negative = Add(positive, right, -2f);

        planes[Left] = SidePlane(positive, origin);
        planes[Right] = SidePlane(negative, origin);

        positive = Add(up, forward, tanY);
        negative = Add(positive, up, -2f);

        planes[Bottom] = SidePlane(positive, origin);
        planes[Top] = SidePlane(negative, origin);

        return new ViewFrustum(planes);
    }

    /// <summary>The vertical field of view an aspect ratio implies from the horizontal one.</summary>
    /// <param name="fovX">The full horizontal field of view, in degrees.</param>
    /// <param name="aspect">Viewport width over height.</param>
    /// <returns>The full vertical field of view, in degrees.</returns>
    /// <remarks>
    /// **`CalcFovY`** (`mathlib_base.cpp:3893`), including its guard: an angle outside 1..179
    /// degrees is *replaced by 90* rather than clamped to the boundary or allowed through. Valve's
    /// own comment on the line is `// error, set to 90`, and the values it rejects are the ones
    /// where `tan` runs away — at 180 the tangent is infinite and every side plane becomes
    /// degenerate.
    ///
    /// **Kept as a substitution rather than a clamp, which looks like a bug and is not.** Clamping
    /// 200 degrees to 179 would give a nearly flat lens; Valve gives an ordinary one. A viewer that
    /// clamped would disagree with the engine exactly where a broken config disagrees with a sane
    /// one, and the two would then be hard to tell apart.
    /// </remarks>
    public static float VerticalFieldOfView(float fovX, float aspect)
    {
        if (fovX < 1f || fovX > 179f)
        {
            fovX = 90f;
        }

        return MathF.Atan(MathF.Tan(fovX * (MathF.PI / 180f) * 0.5f) / aspect)
            * (180f / MathF.PI) * 2f;
    }

    /// <summary>Builds the view volume from a horizontal field of view and an aspect ratio.</summary>
    /// <param name="origin">Where the eye is.</param>
    /// <param name="forward">Unit vector down the view direction.</param>
    /// <param name="right">Unit vector to the camera's right.</param>
    /// <param name="up">Unit vector up from the camera.</param>
    /// <param name="nearZ">The near plane distance.</param>
    /// <param name="farZ">The far plane distance.</param>
    /// <param name="fovX">The FULL horizontal field of view, in degrees.</param>
    /// <param name="aspect">Viewport width over height.</param>
    /// <returns>The six planes, normals inward.</returns>
    /// <remarks>
    /// **Valve's second overload**, which differs from the first only by deriving the vertical
    /// angle: `float flFovY = CalcFovY( flFovX, flAspectRatio );` and then the same call. Kept as
    /// its own entry point rather than left to callers, because the derivation is the part a
    /// caller is most likely to get wrong — and this project's camera stores exactly these
    /// arguments.
    ///
    /// **Named rather than overloaded, because the two would be indistinguishable.** Valve
    /// separates them on `QAngle` against three vectors; here both take the same eight arguments
    /// and differ only in what the last float MEANS, which is the one thing a compiler cannot see
    /// and a reader cannot either. A degrees-versus-ratio mix-up would give a frustum with a
    /// plausible shape and the wrong height.
    /// </remarks>
    public static ViewFrustum PerspectiveFromAspect(
        (float X, float Y, float Z) origin,
        (float X, float Y, float Z) forward,
        (float X, float Y, float Z) right,
        (float X, float Y, float Z) up,
        float nearZ,
        float farZ,
        float fovX,
        float aspect) =>
        Perspective(
            origin, forward, right, up, nearZ, farZ, fovX, VerticalFieldOfView(fovX, aspect));

    /// <summary>Whether a world-space box lies entirely outside the view.</summary>
    /// <param name="minX">The box's lower corner, in world space.</param>
    /// <param name="minY">The box's lower corner, in world space.</param>
    /// <param name="minZ">The box's lower corner, in world space.</param>
    /// <param name="maxX">The box's upper corner, in world space.</param>
    /// <param name="maxY">The box's upper corner, in world space.</param>
    /// <param name="maxZ">The box's upper corner, in world space.</param>
    /// <returns>True when nothing inside the box can be seen, so it need not be drawn.</returns>
    /// <remarks>
    /// **`R_CullBox`** — wholly behind any ONE of the six planes is enough. A box outside two
    /// planes is outside; a box straddling all six is inside; and a box outside none is inside.
    ///
    /// **An unbuilt frustum culls nothing**, so a caller that never set a camera draws everything
    /// rather than nothing. The failure of drawing too much is visible as a frame rate; the failure
    /// of drawing nothing is a black screen that looks like a much deeper fault.
    /// </remarks>
    public bool Cull(
        float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
    {
        if (_planes is not { } planes)
        {
            return false;
        }

        for (int at = 0; at < planes.Length; at++)
        {
            if (planes[at].SideOf(minX, minY, minZ, maxX, maxY, maxZ) == 2)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Valve's index for the right plane.</summary>
    public const int Right = 0;

    /// <summary>Valve's index for the left plane.</summary>
    public const int Left = 1;

    /// <summary>Valve's index for the top plane.</summary>
    public const int Top = 2;

    /// <summary>Valve's index for the bottom plane.</summary>
    public const int Bottom = 3;

    /// <summary>Valve's index for the near plane.</summary>
    public const int Near = 4;

    /// <summary>Valve's index for the far plane.</summary>
    public const int Far = 5;

    /// <summary>A normalised side plane through the eye.</summary>
    private static CullPlane SidePlane(
        (float X, float Y, float Z) normal, (float X, float Y, float Z) origin)
    {
        float length = MathF.Sqrt(Dot(normal, normal));

        // VectorNormalize returns the length and leaves a zero vector alone rather than dividing by
        // nothing. A zero normal here would mean a degenerate basis, which the camera cannot
        // produce, but the guard costs one compare and turns a NaN plane into an inert one.
        if (length > 0f)
        {
            normal = (normal.X / length, normal.Y / length, normal.Z / length);
        }

        return new CullPlane(normal.X, normal.Y, normal.Z, Dot(normal, origin));
    }

    /// <summary>`VectorMA` — the first vector plus the second scaled.</summary>
    private static (float X, float Y, float Z) Add(
        (float X, float Y, float Z) start, (float X, float Y, float Z) direction, float scale) =>
        (start.X + (direction.X * scale),
         start.Y + (direction.Y * scale),
         start.Z + (direction.Z * scale));

    private static float Dot((float X, float Y, float Z) left, (float X, float Y, float Z) right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
}
