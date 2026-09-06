using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// The <c>dprp</c> game lump — the grass a Source map is scattered with (B360).
/// </summary>
/// <remarks>
/// **`DetailObjectLump_t`, `gamebspfile.h:87`**, and the payload is three counted arrays in the
/// order `CDetailObjectSystem::LevelInitPreEntity` reads them
/// (<c>detailobjectsystem.cpp:1464</c>): the model dictionary, the sprite dictionary, then the
/// objects.
///
/// **Synthetic, because a synthetic fixture has ground truth** (D38): every value below was put
/// there by this test, where a real map can only be compared against a second reading of itself.
///
/// **The stride is 52 and Valve's padding is DECLARED**, which is unusual and is why it can be
/// counted rather than guessed: `m_Padding2[3]` and `m_Padding3[3]` are fields. The layout is
/// 0..11 origin, 12..23 angles, 24..25 model index, 26..27 leaf, 28..31 lighting, 32..35 light
/// styles, 36 style count, 37 sway, 38 shape angle, 39 shape size, 40 orientation, 41..43 padding,
/// **44 type**, 45..47 padding, 48..51 scale.
/// </remarks>
public sealed class DetailPropConformanceTests
{
    [Test]
    public void ReadPayload_AnObject_TakesEveryFieldFromItsOwnOffset()
    {
        (IReadOnlyList<string> models, IReadOnlyList<BspDetailSprite> sprites,
            IReadOnlyList<BspDetailProp> objects) = BspDetailProps.ReadPayload(Payload());

        models.ShouldBe(["models/props_foliage/grass.mdl"]);
        sprites.Count.ShouldBe(1);
        objects.Count.ShouldBe(1);

        BspDetailProp prop = objects[0];

        prop.Origin.ShouldBe((1f, 2f, 3f));
        prop.Angles.ShouldBe((4f, 5f, 6f));
        prop.DetailModel.ShouldBe(7);
        prop.Leaf.ShouldBe(8);
        prop.SwayAmount.ShouldBe((byte)11);
        prop.ShapeAngle.ShouldBe((byte)12);
        prop.ShapeSize.ShouldBe((byte)13);
        prop.Orientation.ShouldBe(2);
        prop.Scale.ShouldBe(1.5f);
    }

    /// <remarks>
    /// **The type is at offset 44, not 45**, and this is the assertion that pins it. `m_Padding3`
    /// FOLLOWS the type rather than preceding it, so a reader off by one byte reads the first
    /// padding byte — zero on every real map — and reports every detail prop in the game as
    /// `DETAIL_PROP_TYPE_MODEL`. That is a silent, plausible answer: it would send 28,699 sprites
    /// down a model path and draw nothing, with no error anywhere.
    /// </remarks>
    [Test]
    public void ReadPayload_TheType_ComesFromOffset44RatherThanTheByteAfterIt()
    {
        byte[] payload = Payload();

        // Offset of the object's type byte: the counts, the name, the sprite, then 44 in.
        int typeAt = sizeof(int) + NameBytes + sizeof(int) + SpriteBytes + sizeof(int) + 44;

        payload[typeAt].ShouldBe((byte)1, "the fixture declares a sprite");
        payload[typeAt + 1].ShouldBe((byte)0, "and the byte after it is padding");

        BspDetailProps.ReadPayload(payload).Objects[0].Type.ShouldBe(DetailPropType.Sprite);
    }

    /// <remarks>
    /// The sprite dictionary is four <c>Vector2D</c> — the quad's corners in world units about the
    /// origin, then the texture coordinates of the same two corners.
    /// </remarks>
    [Test]
    public void ReadPayload_ASprite_TakesItsCornersAndTexcoordsInOrder()
    {
        BspDetailSprite sprite = BspDetailProps.ReadPayload(Payload()).Sprites[0];

        sprite.UpperLeft.ShouldBe((-8f, 0f));
        sprite.LowerRight.ShouldBe((8f, 16f));
        sprite.TextureUpperLeft.ShouldBe((0f, 0f));
        sprite.TextureLowerRight.ShouldBe((0.5f, 0.25f));
    }

    /// <remarks>
    /// **<see cref="BspDetailProps.Count"/> walks the two dictionaries rather than assuming an
    /// offset**, because their lengths vary per map — 0 models and 6 sprites on
    /// `koth_harvest_final`, 1 and 3 on `cp_granary`. A fixed offset would read the object count
    /// out of the middle of a sprite.
    /// </remarks>
    [Test]
    public void Count_WalksTheDictionaries_RatherThanAssumingWhereTheObjectsBegin()
    {
        BspDetailProps.Count(Payload()).ShouldBe(1);
    }

    /// <remarks>
    /// **The exponent of a <c>ColorRGBExp32</c> is a SIGNED char and is usually negative** — that is
    /// how a byte mantissa encodes light below one. `TexLightToLinear` is `channel * 2^exponent`,
    /// so an exponent of −1 halves every channel.
    ///
    /// **Read unsigned, 0xFF becomes 2^255 and every channel is infinity**, which is not an error
    /// anywhere: the grass comes out pure white on a map at dusk. The fixture's exponent is
    /// deliberately the byte that separates the two readings rather than a small negative one.
    /// </remarks>
    [Test]
    public void ReadPayload_TheLightingExponent_IsSignedRatherThanUnsigned()
    {
        byte[] payload = Payload();

        int lightingAt = sizeof(int) + NameBytes + sizeof(int) + SpriteBytes + sizeof(int) + 28;

        payload[lightingAt] = 128;
        payload[lightingAt + 1] = 64;
        payload[lightingAt + 2] = 32;
        payload[lightingAt + 3] = 0xFF;

        BspDetailProps.ReadPayload(payload).Objects[0].Lighting.ShouldBe((64f, 32f, 16f));
    }

    /// <remarks>
    /// **`bFlipped` is not in the lump — it alternates by POSITION.** `UnserializeModels` declares
    /// `bool bFlipped = true;` before the loop and toggles it at the top of every iteration
    /// (<c>detailobjectsystem.cpp:1771-1774</c>), so the first object reads false and every second
    /// one after it reads true. The engine mirrors the flipped ones horizontally by reaching for a
    /// second dictionary whose x coordinates are already swapped.
    ///
    /// **It counts every OBJECT, not every sprite**, so a model in the middle of the list still
    /// advances the toggle. Four objects are needed to see the pattern: two would be satisfied by
    /// "the first is false" alone.
    /// </remarks>
    [Test]
    public void ReadPayload_TheFlipFlag_AlternatesByPositionStartingUnflipped()
    {
        IReadOnlyList<BspDetailProp> objects = BspDetailProps.ReadPayload(Payload(objects: 4)).Objects;

        objects.Count.ShouldBe(4);

        objects[0].Flipped.ShouldBeFalse();
        objects[1].Flipped.ShouldBeTrue();
        objects[2].Flipped.ShouldBeFalse();
        objects[3].Flipped.ShouldBeTrue();
    }

    /// <remarks>
    /// A payload too short to hold what it declares is a stranger's file, and D32 requires the
    /// refusal rather than a partial read.
    /// </remarks>
    [Test]
    public void ReadPayload_AnObjectCountThatDoesNotFit_IsRefused()
    {
        byte[] payload = Payload();

        // Overwrite the object count with one the payload cannot hold.
        BinaryPrimitives.WriteInt32LittleEndian(
            payload.AsSpan(sizeof(int) + NameBytes + sizeof(int) + SpriteBytes), 100_000);

        Should.Throw<System.IO.InvalidDataException>(
            () => BspDetailProps.ReadPayload(payload));
    }

    /// <remarks>
    /// **A dictionary that exactly fills the payload leaves no room for the next count.** Reading
    /// one anyway throws out of the span as an `ArgumentOutOfRangeException`, which is not what a
    /// caller catches: `MapAssets` handles `InvalidDataException` and lets everything else escape,
    /// so a malformed map would take the whole load down rather than costing itself its grass.
    ///
    /// The payload here is a count of one model name and exactly that name — no more.
    /// </remarks>
    [Test]
    public void ReadPayload_ADictionaryThatFillsThePayload_IsRefusedAsMalformedData()
    {
        byte[] truncated = new byte[sizeof(int) + NameBytes];

        BinaryPrimitives.WriteInt32LittleEndian(truncated, 1);

        Should.Throw<System.IO.InvalidDataException>(
            () => BspDetailProps.ReadPayload(truncated));
    }

    /// <remarks>
    /// **A count near 2^32/128 wraps a 32-bit product to a SMALL positive offset**, which then
    /// passes a bounds check and puts the reader inside a model name rather than on the sprite
    /// count — a plausible, silent, wrong answer rather than a refusal. 33,554,433 times 128 is
    /// 2^32 + 128, so the wrapped offset is 132 and the payload below is long enough to hold it.
    ///
    /// <see cref="BspDetailProps.Count"/> is the entry point that skips the dictionaries without
    /// reading them, so it is the one where the arithmetic has to be widened.
    ///
    /// **A distinctive number is planted where the WRAPPED walk would land**, because a payload of
    /// zeros would report zero under either reading and the test could not fail. Wrapped, the model
    /// dictionary ends at 132, the sprite count read there is 0, and the object count is read at
    /// 136 — so 4,242 sitting at 136 is what a 32-bit multiply returns and a 64-bit one never sees.
    /// </remarks>
    [Test]
    public void Count_AModelCountThatOverflowsItsOwnStride_ReportsNothing()
    {
        byte[] payload = new byte[1024];

        BinaryPrimitives.WriteInt32LittleEndian(payload, 33_554_433);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(136), 4_242);

        BspDetailProps.Count(payload).ShouldBe(0);
    }

    /// <remarks>
    /// The control: a payload declaring nothing at all is a map with no detail props, which is an
    /// ordinary state — `koth_viaduct` measures exactly zero — and must not throw.
    /// </remarks>
    [Test]
    public void ReadPayload_APayloadDeclaringNothing_IsEmptyRatherThanRefused()
    {
        byte[] empty = new byte[12];

        (IReadOnlyList<string> models, IReadOnlyList<BspDetailSprite> sprites,
            IReadOnlyList<BspDetailProp> objects) = BspDetailProps.ReadPayload(empty);

        models.ShouldBeEmpty();
        sprites.ShouldBeEmpty();
        objects.ShouldBeEmpty();
    }

    private const int NameBytes = 128;
    private const int SpriteBytes = 32;
    private const int ObjectBytes = 52;

    /// <summary>One name, one sprite and <paramref name="objects"/> objects, every field distinctive.</summary>
    /// <remarks>
    /// The objects are identical copies, which is what the flip test wants: anything that
    /// distinguishes them would let a wrong reading agree with the right one for the wrong reason.
    /// </remarks>
    private static byte[] Payload(int objects = 1)
    {
        byte[] payload = new byte[
            sizeof(int) + NameBytes + sizeof(int) + SpriteBytes + sizeof(int) +
            (ObjectBytes * objects)];

        Span<byte> span = payload;

        BinaryPrimitives.WriteInt32LittleEndian(span, 1);
        Encoding.ASCII.GetBytes("models/props_foliage/grass.mdl", span[sizeof(int)..]);

        int at = sizeof(int) + NameBytes;

        BinaryPrimitives.WriteInt32LittleEndian(span[at..], 1);
        at += sizeof(int);

        foreach ((int offset, float value) in
            new[] { (0, -8f), (4, 0f), (8, 8f), (12, 16f), (16, 0f), (20, 0f), (24, 0.5f), (28, 0.25f) })
        {
            BinaryPrimitives.WriteSingleLittleEndian(span[(at + offset)..], value);
        }

        at += SpriteBytes;

        BinaryPrimitives.WriteInt32LittleEndian(span[at..], objects);
        at += sizeof(int);

        for (int index = 0; index < objects; index++)
        {
            int start = at + (index * ObjectBytes);

            foreach ((int offset, float value) in
                new[] { (0, 1f), (4, 2f), (8, 3f), (12, 4f), (16, 5f), (20, 6f), (48, 1.5f) })
            {
                BinaryPrimitives.WriteSingleLittleEndian(span[(start + offset)..], value);
            }

            BinaryPrimitives.WriteUInt16LittleEndian(span[(start + 24)..], 7);
            BinaryPrimitives.WriteUInt16LittleEndian(span[(start + 26)..], 8);

            span[start + 36] = 10;     // light style count
            span[start + 37] = 11;     // sway
            span[start + 38] = 12;     // shape angle
            span[start + 39] = 13;     // shape size
            span[start + 40] = 2;      // orientation: screen aligned, vertical
            span[start + 44] = 1;      // type: sprite
        }

        return payload;
    }
}
