using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Turns the map's detail props into the quads the engine draws for them (B360).
/// </summary>
/// <remarks>
/// **`CDetailModel::DrawTypeSprite`, `detailobjectsystem.cpp:1007`**, transcribed rather than
/// approximated. A detail sprite is one quad standing at the prop's origin, sized by a rectangle
/// from the sprite dictionary and oriented by the prop's own angles:
///
/// <code>
///   AngleVectors( m_Angles, NULL, &amp;dx, &amp;dy );
///   Vector2DMultiply( dict.m_UL, scale, ul );
///   Vector2DMultiply( dict.m_LR, scale, lr );
///   VectorMA( m_Origin, ul.x, dx, vecOrigin );
///   VectorMA( vecOrigin, ul.y, dy, vecOrigin );
///   dx *= (lr.x - ul.x);
///   dy *= (lr.y - ul.y);
/// </code>
///
/// then four vertices at <c>vecOrigin</c>, <c>+dy</c>, <c>+dy+dx</c>, <c>+dx</c>.
///
/// **`AngleVectors( angles, NULL, &amp;dx, &amp;dy )` asks for RIGHT and UP**, not forward — the
/// three-output form is (forward, right, up) and the first is discarded. Reading it as forward
/// builds every quad in the wrong plane, which looks like grass lying flat on the ground.
///
/// **The texture coordinates are swapped when the sprite is NOT flipped**, which reads backwards
/// and is Valve's:
///
/// <code>
///   if ( !m_bFlipped )
///   {
///       texul.x = dict.m_TexLR.x;
///       texlr.x = dict.m_TexUL.x;
///   }
/// </code>
///
/// `m_bFlipped` is a bit set from a parameter to `InitSprite` and is not in the lump, so every
/// sprite read from a file takes the unflipped branch — the swap always applies here.
///
/// **Only <c>DETAIL_PROP_ORIENT_NORMAL</c> is built.** The two screen-aligned orientations have
/// their angles recomputed every frame from the view position
/// (<c>CDetailModel::ComputeAngles</c>, <c>detailobjectsystem.cpp:950</c>), so they cannot be baked
/// into static geometry the way these are. On `koth_harvest_final` that is 20,117 of 28,699; the
/// remaining 8,582 are not drawn at all rather than drawn facing the wrong way, and B361 carries
/// what they need.
///
/// **Neither shape type is built, and that is not a gap.** `USE_DETAIL_SHAPES` is defined only for
/// Day of Defeat and Counter-Strike (<c>detailobjectsystem.cpp:30</c>), so `SHAPE_CROSS`,
/// `SHAPE_TRI` and the sway mechanic do not exist in TF2. Every detail prop measured on
/// `koth_harvest_final` is a plain `SPRITE`, which agrees.
/// </remarks>
public static class DetailSprites
{
    /// <summary>The one material every detail sprite is drawn with.</summary>
    /// <remarks>
    /// <c>#define DETAIL_SPRITE_MATERIAL "detail/detailsprites"</c>
    /// (<c>detailobjectsystem.cpp:44</c>), and Valve's own note on the dictionary says why there is
    /// only one: *"All detail prop sprites must lie in the material detail/detailsprites"*. The
    /// dictionary's entries are sub-rectangles of that single sheet.
    /// </remarks>
    public const string Material = "detail/detailsprites";

    /// <summary>Builds the quads for every fixed-orientation sprite the map places.</summary>
    /// <param name="objects">The detail props, from <see cref="BspDetailProps"/>.</param>
    /// <param name="sprites">The sprite dictionary they index.</param>
    /// <param name="materialIndex">Where <see cref="Material"/> sits in the shared table.</param>
    /// <param name="into">The world vertex list to append to.</param>
    /// <returns>How many sprites were built, and how many were skipped as screen-aligned.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Two triangles per quad, wound to match the world's own geometry** rather than the engine's
    /// quad primitive: Valve emits four vertices and lets the mesh builder make a quad, and this
    /// path takes triangles, so the four corners are issued as 0-1-2 and 0-2-3.
    /// </remarks>
    public static (int Built, int ScreenAligned) Build(
        IReadOnlyList<BspDetailProp> objects,
        IReadOnlyList<BspDetailSprite> sprites,
        int materialIndex,
        IList<PropVertex> into)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(sprites);
        ArgumentNullException.ThrowIfNull(into);

        int built = 0;
        int screenAligned = 0;

        foreach (BspDetailProp prop in objects)
        {
            if (prop.Type != DetailPropType.Sprite ||
                prop.DetailModel < 0 || prop.DetailModel >= sprites.Count)
            {
                continue;
            }

            // See the remarks: an orientation above zero is turned toward the camera every frame.
            if (prop.Orientation != 0)
            {
                screenAligned++;
                continue;
            }

            Quad(prop, sprites[prop.DetailModel], materialIndex, into);

            built++;
        }

        return (built, screenAligned);
    }

    /// <summary>One sprite's four corners, as two triangles.</summary>
    private static void Quad(
        BspDetailProp prop, BspDetailSprite sprite, int materialIndex, IList<PropVertex> into)
    {
        (float pitch, float yaw, float roll) = prop.Angles;

        (float X, float Y, float Z) right = AngleVectors.Right(pitch, yaw, roll);
        (float X, float Y, float Z) up = AngleVectors.Up(pitch, yaw, roll);

        float scale = prop.Scale;

        (float X, float Y) upperLeft = (sprite.UpperLeft.X * scale, sprite.UpperLeft.Y * scale);
        (float X, float Y) lowerRight = (sprite.LowerRight.X * scale, sprite.LowerRight.Y * scale);

        // VectorMA twice: the quad's first corner is the origin displaced by the rectangle's
        // upper-left, measured along the sprite's own right and up axes.
        (float X, float Y, float Z) corner = (
            prop.Origin.X + (upperLeft.X * right.X) + (upperLeft.Y * up.X),
            prop.Origin.Y + (upperLeft.X * right.Y) + (upperLeft.Y * up.Y),
            prop.Origin.Z + (upperLeft.X * right.Z) + (upperLeft.Y * up.Z));

        float width = lowerRight.X - upperLeft.X;
        float height = lowerRight.Y - upperLeft.Y;

        (float X, float Y, float Z) alongX = (right.X * width, right.Y * width, right.Z * width);
        (float X, float Y, float Z) alongY = (up.X * height, up.Y * height, up.Z * height);

        // The swap, which applies to every sprite read from a file — see the remarks on m_bFlipped.
        float texLeft = sprite.TextureLowerRight.X;
        float texRight = sprite.TextureUpperLeft.X;
        float texTop = sprite.TextureUpperLeft.Y;
        float texBottom = sprite.TextureLowerRight.Y;

        PropVertex first = Vertex(corner, texLeft, texTop, prop, materialIndex);

        PropVertex second = Vertex(
            (corner.X + alongY.X, corner.Y + alongY.Y, corner.Z + alongY.Z),
            texLeft, texBottom, prop, materialIndex);

        PropVertex third = Vertex(
            (corner.X + alongY.X + alongX.X,
                corner.Y + alongY.Y + alongX.Y,
                corner.Z + alongY.Z + alongX.Z),
            texRight, texBottom, prop, materialIndex);

        PropVertex fourth = Vertex(
            (corner.X + alongX.X, corner.Y + alongX.Y, corner.Z + alongX.Z),
            texRight, texTop, prop, materialIndex);

        into.Add(first);
        into.Add(second);
        into.Add(third);

        into.Add(first);
        into.Add(third);
        into.Add(fourth);
    }

    private static PropVertex Vertex(
        (float X, float Y, float Z) at, float u, float v, BspDetailProp prop, int materialIndex) =>
        new(
            at.X, at.Y, at.Z, u, v, materialIndex,

            // **The sprite's own origin rides along, as a static prop's placement does.**
            // `MapWorld` judges a prop by where it STANDS rather than by its triangles, because a
            // 3D skybox is made of ordinary triangles at valid positions and only the placement
            // distinguishes them. Leaving this at zero would put every detail sprite at the map
            // origin for that test — inside the bounds by accident rather than by measurement,
            // which is the shape `docs/memory/an-empty-box-must-never-cull.md` records.
            OriginX: prop.Origin.X,
            OriginY: prop.Origin.Y,
            Red: Light(prop.Lighting.Red),
            Green: Light(prop.Lighting.Green),
            Blue: Light(prop.Lighting.Blue),

            // **Facing up, because a detail sprite has no meaningful normal.** It is a billboard on
            // the ground; the engine lights it from the baked colour above rather than from a
            // normal, so this only has to be something the shader can normalise.
            NormalX: 0f,
            NormalY: 0f,
            NormalZ: 1f);

    /// <summary>One baked channel, in the vertex colour's own range.</summary>
    /// <remarks>
    /// **`CDetailModel::GetColorModulation`, `detailobjectsystem.cpp:902`** reads the lump's colour
    /// through `TexLightToLinear`, which is `channel * 2^exponent` — already applied by
    /// <see cref="BspDetailProps"/>, so what arrives here is linear light where **255 at exponent
    /// zero is full brightness**, the same unit `BspLightmaps.Decode` normalises against.
    ///
    /// **No overbright doubling, and that is the difference from a static prop.** vrad stores a
    /// prop's VERTEX lighting halved and the shader doubles it back, but a detail prop's base
    /// colour is written straight — `VectorToColorRGBExp32( totalColor, prop.m_Lighting )`,
    /// `vraddetailprops.cpp:801`, with the halving applied only to the lightstyle entries below it.
    /// Doubling here would put a field of glowing grass on a map at dusk.
    ///
    /// Clamped rather than carried, for the reason `PropModels.Colour` gives: this renderer works
    /// in display space and has no tone map to give over-range light anywhere to go.
    /// </remarks>
    private static float Light(float linear) => Math.Clamp(linear / 255f, 0f, 1f);
}
