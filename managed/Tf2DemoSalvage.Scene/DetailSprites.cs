using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Turns the map's detail props into the quads the engine draws for them, for one view (B360, B361).
/// </summary>
/// <remarks>
/// **`CDetailModel::DrawTypeSprite`, `detailobjectsystem.cpp:1012`**, transcribed rather than
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
/// **The rectangle runs the other way vertically**: `ul.y` is the sprite's TOP and `lr.y` is the
/// ground, so the first corner is displaced upward and `lr.y - ul.y` is negative. Correcting the
/// sign puts every sprite underground.
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
/// **`m_bFlipped` is not in the lump: it ALTERNATES.** `UnserializeModels` declares
/// `bool bFlipped = true;` before the loop and toggles it at the top of every iteration
/// (`detailobjectsystem.cpp:1771-1774`), counting every object rather than every sprite, so the
/// first is unflipped and every second one after it is mirrored. The engine keeps a second
/// dictionary with the x coordinates already swapped (`m_DetailSpriteDictFlipped`,
/// `detailobjectsystem.cpp:1621`) and reaches for it under the same `!bFlipped` test, which is why
/// the two mechanisms look different and are one.
///
/// **This is built PER VIEW, not once at load, and that is the engine's arrangement rather than a
/// choice** (B361). Two things about a detail sprite depend on where the eye is:
///
/// - **its alpha**, from <see cref="DetailFade"/> — sprites fade out across the last
///   `cl_detailfade` units of `cl_detaildist` and are dropped beyond it;
/// - **its angles**, for the two screen-aligned orientations, which
///   `CDetailModel::ComputeAngles` (`detailobjectsystem.cpp:950`) recomputes from
///   `CurrentViewOrigin()`.
///
/// Baking them would fix the first at whatever the eye was at load and leave the second facing an
/// arbitrary direction — which is why the first version of this drew only the fixed-orientation
/// ones. On `koth_harvest_final` that was 20,117 of 28,699 sprites; **on `cp_granary` it was NONE
/// of 19,189**, because every sprite that map places is screen-aligned. Its 324 fixed-orientation
/// detail props are `DETAIL_PROP_TYPE_MODEL`, which this path does not draw either.
///
/// **Neither shape type is built, and that is not a gap.** `USE_DETAIL_SHAPES` is defined only for
/// Day of Defeat and Counter-Strike (<c>detailobjectsystem.cpp:30</c>), so `SHAPE_CROSS`,
/// `SHAPE_TRI` and the sway mechanic do not exist in TF2. Every detail prop measured on
/// `koth_harvest_final`, `cp_granary` and `cp_process_f12` is a plain `SPRITE`, which agrees.
/// </remarks>
public static class DetailSprites
{
    /// <summary>The material every detail sprite is drawn with when the map names none.</summary>
    /// <remarks>
    /// <c>#define DETAIL_SPRITE_MATERIAL "detail/detailsprites"</c>
    /// (<c>detailobjectsystem.cpp:44</c>), and Valve's own note on the dictionary says why there is
    /// only ONE per map: *"All detail prop sprites must lie in the material detail/detailsprites"*.
    /// The dictionary's entries are sub-rectangles of that single sheet.
    ///
    /// **The material's NAME is the map's to choose, though, and that note is out of date** (B364).
    /// `worldspawn`'s `detailmaterial` replaces it — <see cref="BspEntities.DetailSpriteMaterial"/>
    /// — and all 234 installed maps set the key, only 49 of them to this. So this is the fallback
    /// rather than the answer, and it is aliased from the reader that applies it so the two cannot
    /// drift apart.
    /// </remarks>
    public const string Material = BspEntities.DefaultDetailSpriteMaterial;

    /// <summary>Applies Valve's non-square sheet correction to a sprite dictionary.</summary>
    /// <param name="sprites">The dictionary, as the <c>dprp</c> lump wrote it.</param>
    /// <param name="ratio">The sheet's width divided by its height.</param>
    /// <returns>The dictionary, unchanged for a sheet no wider than it is tall.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sprites"/> is null.</exception>
    /// <remarks>
    /// **`CDetailObjectSystem::LevelInitPreEntity`, `detailobjectsystem.cpp:1481`**, which mutates
    /// the dictionary in place right after loading the material:
    ///
    /// <code>
    ///   float flRatio = (float)( pMat-&gt;GetMappingWidth() ) / pMat-&gt;GetMappingHeight();
    ///   if ( flRatio &gt; 1.0 )
    ///   {
    ///       for( int i = 0; i&lt;m_DetailSpriteDict.Count(); i++ )
    ///       {
    ///           m_DetailSpriteDict[i].m_TexUL.y *= flRatio;
    ///           m_DetailSpriteDict[i].m_TexLR.y *= flRatio;
    ///           ...
    ///       }
    ///   }
    /// </code>
    ///
    /// **The guard is `&gt; 1.0` and not `!= 1.0`, which is Valve's** — a sheet TALLER than it is
    /// wide is left alone. The symmetric version is what anybody would write, and it would move the
    /// V coordinates the wrong way on such a sheet.
    ///
    /// **Only V, and only the two texture corners.** The comment calls the sheet "cropped": a
    /// non-square VTF is authored as a square sheet with the bottom cut off, so the dictionary's V
    /// coordinates — written by `vbsp` against the square — have to be stretched back over what
    /// survived.
    ///
    /// **Every sheet TF2 ships is 512x512, so this fires on none of them** (measured on the default
    /// and on `_harvest`, `_granary`, `_trainyard`, `_2fort`, `_sawmill`, `_dustbowl` and
    /// `_viaduct_event`). It is here because the material is the MAP's choice (B364) and a
    /// community map naming a 512x256 sheet would otherwise draw every sprite from its top half.
    /// </remarks>
    public static IReadOnlyList<BspDetailSprite> ScaleForSheet(
        IReadOnlyList<BspDetailSprite> sprites, float ratio)
    {
        ArgumentNullException.ThrowIfNull(sprites);

        if (ratio <= 1f || sprites.Count == 0)
        {
            return sprites;
        }

        BspDetailSprite[] scaled = new BspDetailSprite[sprites.Count];

        for (int index = 0; index < sprites.Count; index++)
        {
            BspDetailSprite sprite = sprites[index];

            scaled[index] = sprite with
            {
                TextureUpperLeft = (sprite.TextureUpperLeft.X, sprite.TextureUpperLeft.Y * ratio),
                TextureLowerRight = (sprite.TextureLowerRight.X, sprite.TextureLowerRight.Y * ratio),
            };
        }

        return scaled;
    }

    /// <summary>What one view's build produced.</summary>
    /// <param name="Built">Quads emitted — six vertices each.</param>
    /// <param name="Faded">Sprites the distance fade dropped entirely.</param>
    /// <param name="Aligned">Of those built, how many were turned to face the eye.</param>
    public readonly record struct Frame(int Built, int Faded, int Aligned);

    /// <summary>Builds every detail sprite this view can see, farthest first.</summary>
    /// <param name="objects">The detail props, from <see cref="BspDetailProps"/>.</param>
    /// <param name="sprites">The sprite dictionary they index.</param>
    /// <param name="eye">Where the view is, in world units.</param>
    /// <param name="fade">The distance fade for this view.</param>
    /// <param name="into">The vertex list to append to.</param>
    /// <returns>What was built, dropped and turned.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Two triangles per quad, wound to match the world's own geometry** rather than the engine's
    /// quad primitive: Valve emits four vertices and lets the mesh builder make a quad, and this
    /// path takes triangles, so the four corners are issued as 0-1-2 and 0-2-3.
    ///
    /// **Sorted back to front across the whole view, where the engine sorts within a leaf.** Valve
    /// walks the visible leaves and sorts each leaf's sprites among themselves
    /// (`SortSpritesBackToFront`, `detailobjectsystem.cpp:2112`), which orders two sprites in
    /// different leaves by the order their leaves came out of the tree. Sorting the whole set is
    /// strictly better ordering for the same blend, and it is one sort rather than one per leaf.
    ///
    /// **A sprite the fade reduced to zero is not emitted at all**, which is what the engine does
    /// with it: `SortSpritesBackToFront` skips `GetAlpha() == 0` before it ever reaches a mesh.
    /// </remarks>
    public static Frame Build(
        IReadOnlyList<BspDetailProp> objects,
        IReadOnlyList<BspDetailSprite> sprites,
        (float X, float Y, float Z) eye,
        DetailFade fade,
        IList<DetailSpriteVertex> into)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(sprites);
        ArgumentNullException.ThrowIfNull(into);

        // **Gathered before anything is emitted, because the emission order IS the draw order.**
        // A blended quad is combined with what is already in the frame buffer, so the sort has to
        // happen between deciding what is visible and writing any vertex.
        List<(int Index, float Squared, byte Alpha)> visible = [];

        int faded = 0;

        for (int index = 0; index < objects.Count; index++)
        {
            BspDetailProp prop = objects[index];

            if (prop.Type != DetailPropType.Sprite ||
                prop.DetailModel < 0 || prop.DetailModel >= sprites.Count)
            {
                continue;
            }

            float x = prop.Origin.X - eye.X;
            float y = prop.Origin.Y - eye.Y;
            float z = prop.Origin.Z - eye.Z;

            float squared = (x * x) + (y * y) + (z * z);

            byte alpha = fade.Alpha(squared);

            if (alpha == 0)
            {
                // **Counted apart from "not a sprite", because they are different events.** A model
                // detail prop is a thing this path does not draw; a faded one is a sprite the view
                // put out of range, and only the second number moves when the camera does.
                faded++;
                continue;
            }

            visible.Add((index, squared, alpha));
        }

        visible.Sort(static (first, second) => second.Squared.CompareTo(first.Squared));

        int aligned = 0;

        foreach ((int index, float _, byte alpha) in visible)
        {
            BspDetailProp prop = objects[index];

            (float Pitch, float Yaw, float Roll) angles = Facing(prop, eye);

            if (prop.Orientation != 0)
            {
                aligned++;
            }

            Quad(prop, sprites[prop.DetailModel], angles, alpha / 255f, into);
        }

        return new Frame(visible.Count, faded, aligned);
    }

    /// <summary>Which way a sprite faces this view — <c>CDetailModel::ComputeAngles</c>.</summary>
    /// <remarks>
    /// **`detailobjectsystem.cpp:950`**, and it is three cases rather than two:
    ///
    /// <code>
    ///   case 0: break;                                    // keep the angles the lump stored
    ///   case 1: VectorSubtract( CurrentViewOrigin(), m_Origin, vecDir );
    ///           VectorAngles( vecDir, m_Angles ); break;
    ///   case 2: VectorSubtract( CurrentViewOrigin(), m_Origin, vecDir );
    ///           vecDir.z = 0.0f;
    ///           VectorAngles( vecDir, m_Angles ); break;
    /// </code>
    ///
    /// **The direction runs from the SPRITE to the EYE**, not the other way, and reversing it turns
    /// every sprite exactly away from the viewer — where a billboard shows its back, which for a
    /// two-sided material looks almost right and is mirrored.
    ///
    /// **Case 2 flattens the direction rather than the result.** Zeroing `z` before
    /// `VectorAngles` keeps the sprite upright while it turns about the vertical axis; clamping the
    /// pitch afterwards would not, because the yaw of a steeply-inclined direction is not the yaw of
    /// its horizontal part once the pitch is discarded.
    ///
    /// **An eye exactly at the sprite's origin is the degenerate case Valve's `VectorAngles` already
    /// handles**, by answering straight up or straight down with no yaw. It arrives here whenever a
    /// player stands in the grass, so it is an ordinary input rather than an edge.
    /// </remarks>
    /// <remarks>
    /// **Shared with detail MODELS, because `ComputeAngles` is a method on `CDetailModel` and that
    /// class is both kinds** (B363). `EnumerateLeaf` calls it without asking what type the object
    /// is (`detailobjectsystem.cpp:2775`), so a screen-aligned detail MODEL turns to face the eye
    /// exactly as a sprite does.
    /// </remarks>
    public static (float Pitch, float Yaw, float Roll) Facing(
        BspDetailProp prop, (float X, float Y, float Z) eye) =>
        prop.Orientation switch
        {
            1 => AngleVectors.Angles(
                eye.X - prop.Origin.X, eye.Y - prop.Origin.Y, eye.Z - prop.Origin.Z),
            2 => AngleVectors.Angles(
                eye.X - prop.Origin.X, eye.Y - prop.Origin.Y, 0f),
            _ => prop.Angles,
        };

    /// <summary>One sprite's four corners, as two triangles.</summary>
    private static void Quad(
        BspDetailProp prop,
        BspDetailSprite sprite,
        (float Pitch, float Yaw, float Roll) angles,
        float alpha,
        IList<DetailSpriteVertex> into)
    {
        (float pitch, float yaw, float roll) = angles;

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

        // **The swap, applied to every OTHER sprite** — see the remarks on `m_bFlipped`. The flag
        // alternates by position in the lump rather than being stored in it, so a builder that
        // swapped unconditionally would mirror half the map's grass the wrong way.
        float texLeft = prop.Flipped ? sprite.TextureUpperLeft.X : sprite.TextureLowerRight.X;
        float texRight = prop.Flipped ? sprite.TextureLowerRight.X : sprite.TextureUpperLeft.X;
        float texTop = sprite.TextureUpperLeft.Y;
        float texBottom = sprite.TextureLowerRight.Y;

        DetailSpriteVertex first = Vertex(corner, texLeft, texTop, prop, alpha);

        DetailSpriteVertex second = Vertex(
            (corner.X + alongY.X, corner.Y + alongY.Y, corner.Z + alongY.Z),
            texLeft, texBottom, prop, alpha);

        DetailSpriteVertex third = Vertex(
            (corner.X + alongY.X + alongX.X,
                corner.Y + alongY.Y + alongX.Y,
                corner.Z + alongY.Z + alongX.Z),
            texRight, texBottom, prop, alpha);

        DetailSpriteVertex fourth = Vertex(
            (corner.X + alongX.X, corner.Y + alongX.Y, corner.Z + alongX.Z),
            texRight, texTop, prop, alpha);

        into.Add(first);
        into.Add(second);
        into.Add(third);

        into.Add(first);
        into.Add(third);
        into.Add(fourth);
    }

    private static DetailSpriteVertex Vertex(
        (float X, float Y, float Z) at, float u, float v, BspDetailProp prop, float alpha) =>
        new(
            at.X, at.Y, at.Z, u, v,
            Red: Light(prop.Lighting.Red),
            Green: Light(prop.Lighting.Green),
            Blue: Light(prop.Lighting.Blue),
            Alpha: alpha,

            // **The same coordinates and a blend of zero, which makes the shader's frame lerp an
            // identity.** Detail sprites do not animate: `CDetailObjectSystem` picks one
            // sub-rectangle of the sheet per prop and holds it. The fields exist because particles
            // share this pass, and passing the frame twice is how a non-animating sprite says so.
            NextU: u,
            NextV: v,
            Blend: 0f);

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
