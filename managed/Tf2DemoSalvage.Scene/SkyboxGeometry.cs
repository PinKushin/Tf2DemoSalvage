using System;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// The six quads of the 2D skybox, in Valve's face order.
/// </summary>
/// <remarks>
/// **The 2D skybox is a box drawn around the eye**, one material per face, and it is what a TF2 map
/// has behind its horizon whether or not it also has a 3D skybox room. This viewer discarded every
/// <c>SURF_SKY</c> face at read time under a comment calling the skybox *"irrelevant to a map
/// overview"*, so the flat colour behind the world was the clear colour (B303).
///
/// **The face order is vbsp's, and there are two candidate arrays in the SDK.** Only one of them
/// means a direction: `skyboxswapper.cpp:60` lists <c>{ rt, bk, lf, ft, up, dn }</c> for precaching,
/// where order is irrelevant, while vbsp builds a CUBEMAP whose index IS the cube face
/// (<c>cubemap.cpp:195</c>):
///
/// <code>
///   const char *facingName[6] = { "rt", "lf", "bk", "ft", "up", "dn" };
/// </code>
///
/// Cube faces run +X, −X, +Y, −Y, +Z, −Z, and Source's axes are X forward, Y left, Z up — so
/// <c>rt</c> is the +X wall, <c>lf</c> −X, <c>bk</c> +Y, <c>ft</c> −Y, <c>up</c> +Z, <c>dn</c> −Z.
///
/// **The box is centred on the EYE and never on the world.** A sky that stayed put would show
/// parallax, which is the one thing a sky must not have: it is infinitely far away, so moving does
/// not change it and only turning does.
///
/// **Winding is inward.** Every face is seen from inside, so the corner order that would be
/// back-facing for a solid box is the front-facing one here.
/// </remarks>
public static class SkyboxGeometry
{
    /// <summary>How many faces a skybox has, and how many materials it needs.</summary>
    public const int Faces = 6;

    /// <summary>Corners per face.</summary>
    public const int CornersPerFace = 6;

    /// <summary>One corner of the box: a direction from the eye, and where it samples.</summary>
    /// <param name="X">Direction from the eye, in Source axes.</param>
    /// <param name="Y">Direction from the eye.</param>
    /// <param name="Z">Direction from the eye.</param>
    /// <param name="U">Texture coordinate.</param>
    /// <param name="V">Texture coordinate.</param>
    public readonly record struct Corner(float X, float Y, float Z, float U, float V);

    /// <summary>Builds one face's two triangles.</summary>
    /// <param name="face">Which face, in Valve's cube order.</param>
    /// <param name="reach">How far from the eye to put the box.</param>
    /// <returns>Six corners, two triangles, wound to be seen from inside.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="face"/> is not a face.</exception>
    /// <remarks>
    /// **Each side face is built so its texture stands upright with the world's up**, and the four
    /// of them run around the box in the order their names imply, so their edges meet. The top and
    /// bottom are laid so their edges meet the sides they touch.
    ///
    /// **`reach` is a distance, not a scale factor, and it has to sit inside the far plane.** The
    /// sky writes no depth, so its size changes nothing about what occludes what — but a box beyond
    /// the far plane is clipped away entirely, which looks exactly like a sky that failed to load.
    /// </remarks>
    public static Corner[] Face(int face, float reach)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(face);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(face, Faces);

        // Each entry names the face's own axes: the corner at (u=0, v=0), then the vectors that
        // walk to u=1 and to v=1. Written this way because a face is a plane plus a texture
        // orientation, and listing four corners hides which pair carries the orientation.
        (float X, float Y, float Z) origin;
        (float X, float Y, float Z) alongU;
        (float X, float Y, float Z) alongV;

        switch (face)
        {
            // +X, `rt`. Seen from inside: up is +Z, and u runs from +Y to −Y so the four side
            // faces circle the box the same way round.
            case 0:
                origin = (1f, 1f, 1f);
                alongU = (0f, -2f, 0f);
                alongV = (0f, 0f, -2f);
                break;

            // −X, `lf`. The opposite wall, so u runs the other way.
            case 1:
                origin = (-1f, -1f, 1f);
                alongU = (0f, 2f, 0f);
                alongV = (0f, 0f, -2f);
                break;

            // +Y, `bk`.
            case 2:
                origin = (-1f, 1f, 1f);
                alongU = (2f, 0f, 0f);
                alongV = (0f, 0f, -2f);
                break;

            // −Y, `ft`.
            case 3:
                origin = (1f, -1f, 1f);
                alongU = (-2f, 0f, 0f);
                alongV = (0f, 0f, -2f);
                break;

            // +Z, `up`. Its v runs along −X so its edge meets `ft`'s top edge.
            case 4:
                origin = (-1f, 1f, 1f);
                alongU = (0f, -2f, 0f);
                alongV = (2f, 0f, 0f);
                break;

            // −Z, `dn`. Mirrored against `up` because it is seen from the other side.
            default:
                origin = (1f, 1f, -1f);
                alongU = (0f, -2f, 0f);
                alongV = (-2f, 0f, 0f);
                break;
        }

        Corner At(float u, float v) =>
            new(
                (origin.X + (alongU.X * u) + (alongV.X * v)) * reach,
                (origin.Y + (alongU.Y * u) + (alongV.Y * v)) * reach,
                (origin.Z + (alongU.Z * u) + (alongV.Z * v)) * reach,
                u,
                v);

        // **Two triangles, wound so the face is front-facing from INSIDE the box.** Written the
        // other way round first, which is the winding a solid box wants — and the symptom of that
        // is a sky present, textured, and entirely invisible. `Face_EveryTriangle_FacesTheEyeAtThe
        // Centre` is the test that said so before anything was drawn.
        return
        [
            At(0f, 0f), At(1f, 1f), At(1f, 0f),
            At(0f, 0f), At(0f, 1f), At(1f, 1f),
        ];
    }
}
