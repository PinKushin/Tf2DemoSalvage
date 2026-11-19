using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The war paint compositor's GPU passes (B754) against the values worked by hand from compositor_ps2x.fxc, and against the
/// CPU reference executing the same <see cref="CompositorPlan"/>.
/// </summary>
/// <remarks>
/// **Two instruments, so neither alone decides**: the predicted value catches both executors being wrong the same way, and
/// the CPU comparison catches a pass the hand values do not reach.
/// </remarks>
public sealed class CompositorRenderTests
{
    [Test]
    public void Composite_TwoGreyTexturesMultiplied_WritesTheirLinearProductBackInSrgb()
    {
        // sRGB 128 is linear 0.21586; squared 0.046596; back to sRGB 0.23903, so 60.95: 61.
        (byte[] gpu, byte[] cpu) = Both(Multiply(Lookup("a"), Lookup("a")), 4, ("a", Grey(128)));

        (gpu[0], gpu[3], cpu[0]).ShouldBe(((byte)61, (byte)255, (byte)61));
    }

    [Test]
    public void Composite_AnOffsetOfHalf_DoublesTheLevelInSrgb()
    {
        // `AdjustLevels`: sRGB 50 against black 0 and white 128 is 50/128 in sRGB, so 99.6: 100. Not 64, whose 127.5 is a
        // rounding tie that Direct3D lets a UNORM write take either way (WARP writes 127, hardware 128).
        (byte[] gpu, byte[] cpu) = Both(Multiply(Lookup("a", ("adjust_offset", "128"))), 4, ("a", Grey(50)));

        (gpu[0], cpu[0]).ShouldBe(((byte)100, (byte)100));
    }

    [TestCase("64", 255)]
    [TestCase("80", 0)]
    public void Composite_ASelectOnAGroupsValue_IsWhiteOnlyWhereASelectorMatches(string selector, int expected)
    {
        // The groups texture is read raw: 64 * 255 / 255 / 16 = 4; selector 64 is 4 (a match), 80 is 5.
        PaintKitNode select = Node("select", Leaf("groups", "g"), Leaf("select", selector));
        (byte[] gpu, byte[] cpu) = Both(Multiply(select), 4, ("g", Grey(64)));

        (gpu[0], cpu[0]).ShouldBe(((byte)expected, (byte)expected));
    }

    [Test]
    public void Composite_ALerpOfThreeInputs_MovesTheFirstTowardTheSecondByTheThirdsRed()
    {
        // lerp( black, white, linear( 128/255 ) ) is linear 0.21586, back to sRGB 128.
        (byte[] gpu, byte[] cpu) = Both(
            Node("combine_lerp", Lookup("black"), Lookup("white"), Lookup("grey")), 4,
            ("black", Grey(0)), ("white", Grey(255)), ("grey", Grey(128)));

        (gpu[0], cpu[0]).ShouldBe(((byte)128, (byte)128));
    }

    [Test]
    public void Composite_ATexturedLookupTurnedAQuarter_MatchesTheCpuReferenceEverywhere()
    {
        // A 4x4 texture of distinct texels at a 4x4 target, rotated 90 degrees: texel centres land on pixel centres to
        // within the float error of the rotation (cos 90 is not 0 in float). Direct3D 11 requires only 8 bits of subtexel
        // precision, so a sample that misses a centre by that error may take up to 1/256 of a neighbour: hardware rounds
        // it to nothing, WARP takes it (0 became 6 where the wrapped neighbour is 192, on the CI runner). So each GPU
        // pixel must lie between the CPU pixel and its 1/256 blend, in linear, toward any of its four wrapped neighbours,
        // plus the rounding of an sRGB write.
        byte[] texels = new byte[4 * 4 * 4];

        for (int i = 0; i < 16; i++)
        {
            texels[i * 4] = (byte)(i * 16);
            texels[(i * 4) + 1] = (byte)(255 - (i * 16));
            texels[(i * 4) + 2] = (byte)((i % 4) * 64);
            texels[(i * 4) + 3] = 255;
        }

        (byte[] gpu, byte[] cpu) = Both(
            Multiply(Lookup("a", ("rotation", "90 90"))), 4, ("a", new CompositorTexture([(4, 4, texels)])));

        int worst = 0;
        int outside = 0;

        for (int i = 0; i < gpu.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(gpu[i] - cpu[i]));
            (int low, int high) = WithinASubtexelOfANeighbour(cpu, 4, i);
            outside += gpu[i] < low - 1 || gpu[i] > high + 1 ? 1 : 0;
        }

        TestContext.Out.WriteLine($"COMPOSITOR GPU vs CPU worst channel difference {worst}");
        cpu.ShouldNotBe(new byte[cpu.Length], "the control: the reference drew something");
        outside.ShouldBe(0, "every channel within a 1/256 subtexel blend of the reference");
    }

    /// <summary>
    /// The sRGB range a channel can take when its sample misses the texel centre by less than a subtexel step: between
    /// its own value and a 1/256 linear blend toward any of its four neighbours, wrapping as the lookup's sampler does.
    /// </summary>
    private static (int Low, int High) WithinASubtexelOfANeighbour(byte[] image, int size, int index)
    {
        int pixel = index / 4;
        int channel = index % 4;
        (int x, int y) = (pixel % size, pixel / size);
        double own = Linear(image[index]);
        double low = own;
        double high = own;

        foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            int neighbour = ((((y + dy + size) % size) * size) + ((x + dx + size) % size)) * 4 + channel;
            double blended = own + ((Linear(image[neighbour]) - own) / 256d);
            low = Math.Min(low, blended);
            high = Math.Max(high, blended);
        }

        return ((int)Math.Floor(Srgb(low) * 255d), (int)Math.Ceiling(Srgb(high) * 255d));
    }

    private static double Linear(byte value)
    {
        double c = value / 255d;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double Srgb(double linear) =>
        linear <= 0.0031308 ? linear * 12.92 : (1.055 * Math.Pow(linear, 1d / 2.4)) - 0.055;

    [Test]
    public void Composite_ATextureThatDoesNotLoad_Fails()
    {
        // "Failed to load texture '%s', this is non-recoverable" (findings 111 §3.1): the engine keeps the original.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(4, 4);

        if (target is null)
        {
            Assert.Ignore("no Direct3D");
            return;
        }

        CompositorPlan plan = TextureCompositor.Create(Recipe(Multiply(Lookup("a"))), 4, 4, 2, 0ul).ShouldNotBeNull().Plan().ShouldNotBeNull();

        target.Composite(plan, _ => null).ShouldBeNull();
    }

    private static (byte[] Gpu, byte[] Cpu) Both(PaintKitNode stage, int size, params (string Name, CompositorTexture Texture)[] textures)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(size, size);

        if (target is null)
        {
            Assert.Ignore("no Direct3D");
            return ([], []);
        }

        Dictionary<string, CompositorTexture> byName = new(StringComparer.Ordinal);

        foreach ((string name, CompositorTexture texture) in textures)
        {
            byName[name] = texture;
        }

        TextureCompositor compositor = TextureCompositor.Create(Recipe(stage), size, size, 2, 0x1234ul).ShouldNotBeNull();
        CompositorPlan plan = compositor.Plan().ShouldNotBeNull();
        byte[] cpu = compositor.Composite(name => byName.GetValueOrDefault(name)).ShouldNotBeNull();
        byte[] gpu = target.Composite(plan, name => byName.TryGetValue(name, out CompositorTexture? texture) ? AsMap(texture) : null).ShouldNotBeNull();

        return (gpu, cpu);
    }

    private static MapTexture AsMap(CompositorTexture texture)
    {
        (int width, int height, byte[] pixels) = texture.Levels[0];
        return new MapTexture(width, height, width, height, TextureImage.Rgba(pixels), IsTransparent: false);
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
