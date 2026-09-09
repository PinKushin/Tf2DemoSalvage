using System.Numerics;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// Where a particle system is attached, and which way it faces (B373).
/// </summary>
/// <param name="At">The point itself, in world space.</param>
/// <param name="Forward">Its local +X, from the entity's angles.</param>
/// <param name="Right">Its local +Y.</param>
/// <param name="Up">Its local +Z.</param>
/// <remarks>
/// **A control point has an ORIENTATION and not just a position**, which is the whole reason this
/// type exists rather than a bare <see cref="Vector3"/>. A rocket's trail is created with
/// <c>PATTACH_POINT_FOLLOW</c> (`c_tf_projectile_rocket.cpp:67`), so the point carries the
/// projectile's own frame — and `rockettrail` asks for
/// <c>speed_in_local_coordinate_system_min/max = (0 0 -10)</c>, which is ten units per second down
/// the point's local Z. Read in world space instead, every trail in the game would blow the same
/// direction regardless of where its rocket was pointing.
///
/// **The basis is passed in rather than derived from angles here**, because the angle convention
/// belongs to the layer that already owns it: `AngleVectors` is Source's own decomposition and this
/// assembly has no business holding a second copy of it
/// (`docs/memory/two-matrix-conventions-on-purpose.md`).
/// </remarks>
public readonly record struct ParticleControlPoint(
    Vector3 At, Vector3 Forward, Vector3 Right, Vector3 Up)
{
    /// <summary>A point with no orientation, for a caller that has none to give.</summary>
    /// <param name="at">Where it is.</param>
    /// <returns>The point, with the world axes as its own.</returns>
    /// <remarks>
    /// **The identity basis is a STATED fallback, not a neutral one.** A system whose initializers
    /// use the local frame will spray along world axes through this, which is wrong — but it is
    /// wrong in a way that draws, and the alternative is refusing to draw a trail because a caller
    /// did not have angles. Callers that have them should pass them.
    /// </remarks>
    public static ParticleControlPoint Unoriented(Vector3 at) =>
        new(at, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
}
