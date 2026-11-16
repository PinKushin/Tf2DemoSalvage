using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// `CTextureCompositor` (findings 111) and its stage shader (`compositor_ps2x.fxc`): each value worked by hand from the
/// formulas, over one-texel textures so filtering and wrapping cannot move it.
/// </summary>
public sealed class TextureCompositorConformanceTests
{
    [TestCase(0x5555555555555555ul, 0xFFFFFFFFu, 0u)]
    [TestCase(0xAAAAAAAAAAAAAAAAul, 0u, 0xFFFFFFFFu)]
    [TestCase(0x6ul, 2u, 1u)]
    public void SplitSeed_ASeed_DealsItsEvenBitsToSeedAAndItsOddBitsToSeedB(ulong seed, uint seedA, uint seedB)
    {
        // `GetSeed` (findings 111 §2.1): bit 2i to bit i of A, bit 2i+1 to bit i of B. 0x6 is bits 1 and 2: bit 2 -> A bit 1,
        // bit 1 -> B bit 0.
        TextureCompositor.SplitSeed(seed).ShouldBe((seedA, seedB));
    }

    [Test]
    public void Composite_TwoGreyTexturesMultiplied_WritesTheirLinearProductBackInSrgb()
    {
        // sRGB 128 is linear 0.21586; squared 0.046596; back to sRGB 0.23903, so 60.95, rounded 61 (sRGB read, sRGB write).
        byte[] pixels = Composite(Multiply(Lookup("a"), Lookup("a")), ("a", Grey(128))).ShouldNotBeNull();

        (pixels[0], pixels[1], pixels[2], pixels[3]).ShouldBe(((byte)61, (byte)61, (byte)61, (byte)255));
    }

    [Test]
    public void Composite_AnOffsetOfHalf_DoublesTheLevelInSrgb()
    {
        // `AdjustLevels`: sRGB 64/255 = 0.25098 against black 0 and white 128/255 = 0.50196 is 0.5 (gamma 1), linear 0.214,
        // written back as sRGB 0.5, so 127.5, rounded 128.
        byte[] pixels = Composite(Multiply(Lookup("a", ("adjust_offset", "128"))), ("a", Grey(64))).ShouldNotBeNull();

        pixels[0].ShouldBe((byte)128);
    }

    [Test]
    public void Composite_ALerpOfTwoInputs_IsTheShadersRedFallback()
    {
        // `main_lerp` needs three inputs; otherwise it returns ( 1, 0, 0, 1 ).
        byte[] pixels = Composite(Node("combine_lerp", Lookup("a"), Lookup("a")), ("a", Grey(128))).ShouldNotBeNull();

        (pixels[0], pixels[1], pixels[2], pixels[3]).ShouldBe(((byte)255, (byte)0, (byte)0, (byte)255));
    }

    [Test]
    public void Composite_ALerpOfThreeInputs_MovesTheFirstTowardTheSecondByTheThirdsRed()
    {
        // lerp( black, white, linear( 128/255 ) = 0.21586 ) = 0.21586, back to sRGB 128.
        byte[] pixels = Composite(
            Node("combine_lerp", Lookup("black"), Lookup("white"), Lookup("grey")),
            ("black", Grey(0)), ("white", Grey(255)), ("grey", Grey(128))).ShouldNotBeNull();

        pixels[0].ShouldBe((byte)128);
    }

    [TestCase("64", 255)]
    [TestCase("80", 0)]
    public void Composite_ASelectOnAGroupsValue_IsWhiteOnlyWhereASelectorMatches(string selector, int expected)
    {
        // The groups texture is read raw (no sRGB read): 64/255 * 255/16 = 4; selector 64 is 4 (match), 80 is 5.
        PaintKitNode select = Node("select", Leaf("groups", "g"), Leaf("select", selector));
        byte[] pixels = Composite(Multiply(select), ("g", Grey(64))).ShouldNotBeNull();

        pixels[0].ShouldBe((byte)expected);
    }

    [TestCase(2, 10)]
    [TestCase(3, 200)]
    public void Composite_ALookupWithABlueTexture_TakesItOnBlueAndTheBaseOnRed(int team, int expected)
    {
        // Team 2 takes `texture_red` when there is one, else `texture`; team 3 takes `texture_blue` (findings 111 §3.1).
        PaintKitNode lookup = Lookup("a", ("texture_blue", "b"));
        byte[] pixels = Composite(Multiply(lookup), team, ("a", Grey(10)), ("b", Grey(200))).ShouldNotBeNull();

        pixels[0].ShouldBe((byte)expected);
    }

    [Test]
    public void TextureName_ABlueTexture_PutsTheTeamInTheName()
    {
        // Only a `texture_blue` makes the composite team-specific (§4.2).
        TextureCompositor blue = TextureCompositor.Create(Recipe(Multiply(Lookup("a", ("texture_blue", "b")))), 4, 2, 3, 0x6ul).ShouldNotBeNull();
        TextureCompositor plain = TextureCompositor.Create(Recipe(Multiply(Lookup("a"))), 4, 2, 3, 0x6ul).ShouldNotBeNull();

        (blue.TextureName("1_2_wear_01", 0u), plain.TextureName("1_2_wear_01", 0u)).ShouldBe((
            "proc/texcomp/1_2_wear_01_flags00000000_seedhi00000002_seedlo00000001_team3_w4_h2",
            "proc/texcomp/1_2_wear_01_flags00000000_seedhi00000002_seedlo00000001_w4_h2"));
    }

    [Test]
    public void Create_AVariableReference_IsSubstitutedFromTheRootsDefinitions()
    {
        // `$[name]` replaced by the root's `$name` (§1.3); an unknown one fails the whole creation.
        PaintKitNode known = Recipe(Multiply(Lookup("$[color]")));
        known.SetString("$color", "a");
        PaintKitNode unknown = Recipe(Multiply(Lookup("$[missing]")));

        byte[] pixels = TextureCompositor.Create(known, 1, 1, 2, 0ul).ShouldNotBeNull().Composite(Loader(("a", Grey(128)))).ShouldNotBeNull();

        (pixels[0], TextureCompositor.Create(unknown, 1, 1, 2, 0ul)).ShouldBe(((byte)128, (TextureCompositor?)null));
    }

    [Test]
    public void Create_AnUnknownStageName_Fails()
    {
        // "Compositor Error: Unexpected key '%s' while parsing definition." (§1.4).
        TextureCompositor.Create(Recipe(Multiply(Node("texture_lookups"))), 1, 1, 2, 0ul).ShouldBeNull();
    }

    [Test]
    public void Composite_ATextureThatDoesNotLoad_Fails()
    {
        // "Failed to load texture '%s', this is non-recoverable" (§3.1).
        TextureCompositor.Create(Recipe(Multiply(Lookup("missing"))), 1, 1, 2, 0ul).ShouldNotBeNull().Composite(_ => null).ShouldBeNull();
    }

    private static byte[]? Composite(PaintKitNode stage, params (string Name, CompositorTexture Texture)[] textures) =>
        Composite(stage, 2, textures);

    private static byte[]? Composite(PaintKitNode stage, int team, params (string Name, CompositorTexture Texture)[] textures) =>
        TextureCompositor.Create(Recipe(stage), 2, 2, team, 0x1234ul)?.Composite(Loader(textures));

    private static Func<string, CompositorTexture?> Loader(params (string Name, CompositorTexture Texture)[] textures)
    {
        Dictionary<string, CompositorTexture> byName = new(StringComparer.Ordinal);

        foreach ((string name, CompositorTexture texture) in textures)
        {
            byName[name] = texture;
        }

        return name => byName.GetValueOrDefault(name);
    }

    private static CompositorTexture Grey(byte value) => new([(1, 1, new[] { value, value, value, (byte)255 })]);

    private static PaintKitNode Recipe(PaintKitNode stage)
    {
        PaintKitNode recipe = new("wear_level_1");
        recipe.Add(stage);
        return recipe;
    }

    private static PaintKitNode Multiply(params PaintKitNode[] children) => Node("combine_multiply", children);

    private static PaintKitNode Lookup(string texture, params (string Key, string Value)[] keys)
    {
        PaintKitNode lookup = Node("texture_lookup", Leaf("texture", texture));

        foreach ((string key, string value) in keys)
        {
            lookup.Add(Leaf(key, value));
        }

        return lookup;
    }

    private static PaintKitNode Node(string name, params PaintKitNode[] children)
    {
        PaintKitNode node = new(name);

        foreach (PaintKitNode child in children)
        {
            node.Add(child);
        }

        return node;
    }

    private static PaintKitNode Leaf(string name, string value) => new(name) { Value = value };
}
