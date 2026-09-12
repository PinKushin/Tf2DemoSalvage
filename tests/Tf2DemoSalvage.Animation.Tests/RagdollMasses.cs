using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// Mass properties for synthetic solids whose tests are not about mass (B403).
/// </summary>
/// <remarks>
/// **Every solid the engine makes an object from has them**, because they are fields of the compact surface it
/// was built from, and `RagdollBody` refuses a solid without them. So a fixture about joints, pushes or poses
/// still has to supply a pair.
///
/// **This pair changes nothing those fixtures predict.** A mass center at the bone leaves the core there, and a
/// hull inertia of one per kilogram on every axis makes each core's inertia `mass × scale` about all three —
/// the single number this project gave every body before B403 — with the floor, a tenth of `√3 × mass`, never
/// reached.
/// </remarks>
internal static class RagdollMasses
{
    private const float SquareInchesPerSquareMetre = 39.37f * 39.37f;

    /// <summary>A mass center at the bone and one square inch per kilogram about each axis, in IVP metres.</summary>
    public static PhysicsMassProperties Unit { get; } =
        new(Vector3.Zero, Vector3.One / SquareInchesPerSquareMetre);

    /// <summary><see cref="Unit"/> for each of <paramref name="count"/> solids.</summary>
    /// <param name="count">How many solids the model declares.</param>
    /// <returns>One pair per solid.</returns>
    public static PhysicsMassProperties?[] Uniform(int count) =>
        Enumerable.Repeat<PhysicsMassProperties?>(Unit, count).ToArray();
}
