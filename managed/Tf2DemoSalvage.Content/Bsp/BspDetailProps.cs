using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>How a detail prop is drawn — <c>DetailPropType_t</c>, <c>gamebspfile.h:57</c>.</summary>
public enum DetailPropType
{
    /// <summary>A studio model, named by the model dictionary.</summary>
    Model = 0,

    /// <summary>A single quad from the shared detail sprite sheet.</summary>
    Sprite = 1,

    /// <summary>Two quads crossed at right angles — grass seen from any side.</summary>
    ShapeCross = 2,

    /// <summary>Three quads in a triangle.</summary>
    ShapeTri = 3,
}

/// <summary>One scattered detail object — <c>DetailObjectLump_t</c>, <c>gamebspfile.h:87</c>.</summary>
/// <param name="Origin">Where it stands, in world units.</param>
/// <param name="Angles">Its orientation, pitch-yaw-roll.</param>
/// <param name="DetailModel">An index into the model dictionary, or into the sprite dictionary.</param>
/// <param name="Leaf">The BSP leaf it sits in, which is how the engine culls it.</param>
/// <param name="Type">Whether it is a model, a sprite, or one of the two shapes.</param>
/// <param name="Orientation">
/// <c>DetailPropOrientation_t</c>: normal, screen-aligned, or screen-aligned about Z only.
/// </param>
/// <param name="SwayAmount">How far it moves in the wind, 0 for still.</param>
/// <param name="ShapeAngle">The angle parameter of a cross or tri shape.</param>
/// <param name="ShapeSize">The size parameter of a cross or tri shape.</param>
/// <param name="Scale">A multiplier, used for sprites.</param>
public readonly record struct BspDetailProp(
    (float X, float Y, float Z) Origin,
    (float Pitch, float Yaw, float Roll) Angles,
    int DetailModel,
    int Leaf,
    DetailPropType Type,
    int Orientation,
    byte SwayAmount,
    byte ShapeAngle,
    byte ShapeSize,
    float Scale);

/// <summary>One entry of the sprite sheet — <c>DetailSpriteDictLump_t</c>.</summary>
/// <param name="UpperLeft">The quad's upper-left corner, in world units about the origin.</param>
/// <param name="LowerRight">Its lower-right corner.</param>
/// <param name="TextureUpperLeft">The texture coordinate of that corner.</param>
/// <param name="TextureLowerRight">The texture coordinate of the other.</param>
/// <remarks>
/// **Every detail sprite comes from ONE material.** Valve's own note on the struct: *"All detail
/// prop sprites must lie in the material detail/detailsprites"*, so this dictionary is a set of
/// sub-rectangles of a single sheet rather than a list of materials.
/// </remarks>
public readonly record struct BspDetailSprite(
    (float X, float Y) UpperLeft,
    (float X, float Y) LowerRight,
    (float X, float Y) TextureUpperLeft,
    (float X, float Y) TextureLowerRight);

/// <summary>
/// The map's detail props — the <c>dprp</c> game lump.
/// </summary>
/// <remarks>
/// **The grass.** `vbsp` scatters these from a material's `%detailtype`, and they are the reason a
/// Source outdoor map reads as ground rather than as a texture: thousands of tiny sprites and
/// models, drawn as one batch, culled per leaf and faded by distance. They are a separate system
/// from static props and live in a separate sub-lump of the same game lump (B360).
///
/// **The payload is three counted arrays in order** — model dictionary, sprite dictionary, then the
/// objects — which is `CDetailObjectSystem::LevelInitPreEntity` reading
/// `UnserializeModelDict`, `UnserializeDetailSprites`, `UnserializeModels`
/// (<c>detailobjectsystem.cpp:1464</c>).
///
/// **Below version 4 the engine refuses the lump outright:**
///
/// <code>
///   if (engine->GameLumpVersion( GAMELUMP_DETAIL_PROPS ) &lt; 4)
///   {
///       Warning("Map uses old detail prop file format.. ignoring detail props\n");
///       return;
///   }
/// </code>
///
/// so a map at version 3 draws no detail props AT ALL in the engine, and reading one here would put
/// grass on a map TF2 leaves bare. That branch is carried rather than tolerated.
/// </remarks>
public static class BspDetailProps
{
    /// <summary>'dprp', as it appears in the game lump directory.</summary>
    public const int Id = 0x64707270;

    /// <summary>The oldest version the engine will read — <c>GAMELUMP_DETAIL_PROPS_VERSION</c>.</summary>
    /// <remarks>
    /// **A lower version is IGNORED, not upgraded** (<c>detailobjectsystem.cpp:1449</c>). The
    /// constant is 4 and the comparison is a strict less-than, so 4 is the first acceptable value.
    /// </remarks>
    public const int OldestVersion = 4;

    /// <summary>Bytes per model dictionary entry — <c>DETAIL_NAME_LENGTH</c>.</summary>
    private const int NameBytes = 128;

    /// <summary>Bytes per sprite dictionary entry: four <c>Vector2D</c>.</summary>
    private const int SpriteBytes = 32;

    /// <summary>
    /// Bytes per object. Twelve of origin, twelve of angles, two indices, four of lighting, four of
    /// light styles, eight of bytes and explicit padding, and a float of scale.
    /// </summary>
    /// <remarks>
    /// **Valve's padding is DECLARED here rather than implied**, which is unusual and helpful:
    /// `m_Padding2[3]` and `m_Padding3[3]` are fields, so the stride is the field sum and there is
    /// no compiler alignment to guess at. It still has to be checked against the payload, because
    /// a stride that is wrong by a byte reads every object after the first from the wrong place and
    /// produces plausible garbage rather than an error.
    /// </remarks>
    private const int ObjectBytes = 52;

    /// <summary>How many detail objects a payload declares, without reading them.</summary>
    /// <param name="payload">The <c>dprp</c> payload.</param>
    /// <returns>The object count, or 0 when the payload cannot be walked that far.</returns>
    /// <remarks>
    /// **For measuring whether a map has any**, which is the question that decides whether drawing
    /// them would change a picture. It walks the two dictionaries rather than trusting a fixed
    /// offset, because their lengths vary per map.
    /// </remarks>
    public static int Count(ReadOnlyMemory<byte> payload)
    {
        ReadOnlySpan<byte> span = payload.Span;

        return TrySkipDictionaries(span, out int at) &&
            at + sizeof(int) <= span.Length
                ? BinaryPrimitives.ReadInt32LittleEndian(span[at..])
                : 0;
    }

    /// <summary>Every detail object the map scatters, with the two dictionaries they index.</summary>
    /// <param name="file">The whole map file.</param>
    /// <returns>The models, sprites and objects; all empty when the map declares none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is null.</exception>
    /// <exception cref="InvalidDataException">A count does not fit in the payload.</exception>
    /// <remarks>
    /// **Empty rather than an exception for a map with no `dprp`**, and for one below version 4:
    /// both are ordinary states the engine handles by drawing nothing, and a viewer that threw
    /// would refuse a map TF2 opens.
    /// </remarks>
    public static (IReadOnlyList<string> Models,
        IReadOnlyList<BspDetailSprite> Sprites,
        IReadOnlyList<BspDetailProp> Objects) Read(ReadOnlyMemory<byte> file)
    {
        foreach (BspGameLumpEntry entry in BspGameLumps.Directory(file))
        {
            if (entry.Id != Id)
            {
                continue;
            }

            // The engine's own refusal, carried: an old lump draws nothing rather than something.
            return entry.Version < OldestVersion
                ? ([], [], [])
                : ReadPayload(BspGameLumps.Payload(file, entry).Span);
        }

        return ([], [], []);
    }

    /// <summary>Walks past the two dictionaries to where the objects begin.</summary>
    private static bool TrySkipDictionaries(ReadOnlySpan<byte> payload, out int at)
    {
        at = 0;

        if (payload.Length < sizeof(int))
        {
            return false;
        }

        int names = BinaryPrimitives.ReadInt32LittleEndian(payload);

        at = sizeof(int) + (names * NameBytes);

        if (names < 0 || at < 0 || at + sizeof(int) > payload.Length)
        {
            return false;
        }

        int sprites = BinaryPrimitives.ReadInt32LittleEndian(payload[at..]);

        at += sizeof(int) + (sprites * SpriteBytes);

        return sprites >= 0 && at >= 0 && at <= payload.Length;
    }

    /// <summary>The same, from a payload already located and decompressed.</summary>
    /// <param name="payload">The <c>dprp</c> bytes: three counted arrays in order.</param>
    /// <returns>The models, sprites and objects.</returns>
    /// <exception cref="InvalidDataException">A count does not fit in the payload.</exception>
    /// <remarks>
    /// **Public so the format can be tested without a map** (D38). A synthetic payload has ground
    /// truth — the test put the values there — where a real BSP can only be compared against a
    /// second reading of itself, and building a whole BSP to exercise a sub-lump is a fixture
    /// nobody would maintain.
    /// </remarks>
    public static (IReadOnlyList<string> Models,
        IReadOnlyList<BspDetailSprite> Sprites,
        IReadOnlyList<BspDetailProp> Objects) ReadPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < sizeof(int))
        {
            throw new InvalidDataException("The detail prop lump is too short to hold its count.");
        }

        int nameCount = BinaryPrimitives.ReadInt32LittleEndian(payload);

        Require(nameCount, NameBytes, sizeof(int), payload.Length, "model names");

        List<string> models = new(nameCount);

        for (int index = 0; index < nameCount; index++)
        {
            ReadOnlySpan<byte> name =
                payload.Slice(sizeof(int) + (index * NameBytes), NameBytes);

            int end = name.IndexOf((byte)0);

            models.Add(Encoding.ASCII.GetString(end < 0 ? name : name[..end]));
        }

        int at = sizeof(int) + (nameCount * NameBytes);

        int spriteCount = BinaryPrimitives.ReadInt32LittleEndian(payload[at..]);

        Require(spriteCount, SpriteBytes, at + sizeof(int), payload.Length, "sprites");

        at += sizeof(int);

        List<BspDetailSprite> sprites = new(spriteCount);

        for (int index = 0; index < spriteCount; index++)
        {
            ReadOnlySpan<byte> entry = payload.Slice(at + (index * SpriteBytes), SpriteBytes);

            sprites.Add(new BspDetailSprite(
                (Float(entry), Float(entry[4..])),
                (Float(entry[8..]), Float(entry[12..])),
                (Float(entry[16..]), Float(entry[20..])),
                (Float(entry[24..]), Float(entry[28..]))));
        }

        at += spriteCount * SpriteBytes;

        int objectCount = BinaryPrimitives.ReadInt32LittleEndian(payload[at..]);

        Require(objectCount, ObjectBytes, at + sizeof(int), payload.Length, "objects");

        at += sizeof(int);

        List<BspDetailProp> objects = new(objectCount);

        for (int index = 0; index < objectCount; index++)
        {
            ReadOnlySpan<byte> entry = payload.Slice(at + (index * ObjectBytes), ObjectBytes);

            objects.Add(new BspDetailProp(
                (Float(entry), Float(entry[4..]), Float(entry[8..])),
                (Float(entry[12..]), Float(entry[16..]), Float(entry[20..])),
                BinaryPrimitives.ReadUInt16LittleEndian(entry[24..]),
                BinaryPrimitives.ReadUInt16LittleEndian(entry[26..]),

                // **The layout, counted out, because one byte wrong here reads plausible garbage.**
                // 0..11 origin, 12..23 angles, 24..25 model, 26..27 leaf, 28..31 `ColorRGBExp32`
                // and 32..35 the light styles — both of which the engine relights at load — then
                // 36 style count, 37 sway, 38 shape angle, 39 shape size, 40 orientation,
                // 41..43 `m_Padding2`, **44 the type**, 45..47 `m_Padding3`, 48..51 the scale.
                //
                // The type is at 44 and not 45: `m_Padding3` follows it rather than preceding it.
                (DetailPropType)entry[44],
                entry[40],
                entry[37],
                entry[38],
                entry[39],
                Float(entry[48..])));
        }

        return (models, sprites, objects);
    }

    /// <summary>Refuses a count that does not fit, naming what was being read.</summary>
    private static void Require(int count, int stride, int from, int length, string what)
    {
        if (count < 0 || from + ((long)count * stride) > length)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"The detail prop lump declares {count:N0} {what} at {stride} bytes each from " +
                $"offset {from:N0}, which do not fit in its {length:N0} bytes."));
        }
    }

    private static float Float(ReadOnlySpan<byte> at) =>
        BinaryPrimitives.ReadSingleLittleEndian(at);
}
