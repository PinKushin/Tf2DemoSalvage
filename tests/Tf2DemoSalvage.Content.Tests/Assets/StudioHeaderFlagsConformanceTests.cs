using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The <c>studiohdr_t.flags</c> word: where it is, what its bits mean, and that we read it.
/// </summary>
/// <remarks>
/// **The one field of the model header this reader stepped over for months.** `StudioLayout` already
/// described it in prose — its `HeaderBoneCountOffset` note reads *"flags sits between view_bbmax and
/// numbones"* — while nothing decoded it. It is needed now because
/// <c>STUDIOHDR_FLAGS_TRANSLUCENT_TWOPASS</c> is the only thing that entitles a model to be drawn
/// twice, and this renderer has been drawing every model twice without asking (see
/// <c>TwoPassConformanceTests</c> in `Rendering.Tests`).
///
/// **The bulk test is a cross-field prediction rather than a restatement of the offset**, which is
/// what makes it able to fail. Valve's comment on <c>STUDIOHDR_FLAGS_STATIC_PROP</c> is *"This is
/// set any time the .qc files has $staticprop in it / Means there's no bones and no transforms"* —
/// so the bit and the bone count four bytes later must agree on every shipped model. Reading the
/// word from the wrong place breaks that agreement immediately, whereas an assertion that the
/// constant equals 152 would pass against any number this file happened to contain.
/// </remarks>
public sealed class StudioHeaderFlagsConformanceTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    /// <summary>How many shipped models the correlation is measured over.</summary>
    /// <remarks>
    /// Enough that both kinds are certainly present and small enough to stay under a second. The
    /// test asserts that both kinds ARE present rather than assuming the sample is mixed — a sample
    /// that turned out to be all props would otherwise pass while measuring one thing.
    /// </remarks>
    private const int Sample = 200;

    /// <summary>That the flag values this project names are the SDK's.</summary>
    /// <remarks>
    /// **<c>FORCE_OPAQUE</c> is here because it is the other half of the same decision.** Its
    /// comment — *"Use this when there are translucent parts to the model but we're not going to
    /// sort it"* — establishes that the engine's <c>IsTranslucent(model)</c> means ANY material
    /// rather than all of them. A flag whose job is to suppress that answer only makes sense if the
    /// answer would otherwise have been yes.
    /// </remarks>
    [Test]
    public void StudioModelFlags_AgainstTheSdk_MatchTheHeadersDefines()
    {
        string header = Skip.Unless(SourceSdk.Text("src/public/studio.h"), SourceSdk.Missing);

        Define(header, "STUDIOHDR_FLAGS_FORCE_OPAQUE").ShouldBe(StudioModelFlags.ForceOpaque);

        Define(header, "STUDIOHDR_FLAGS_TRANSLUCENT_TWOPASS")
            .ShouldBe(StudioModelFlags.TranslucentTwoPass);

        Define(header, "STUDIOHDR_FLAGS_STATIC_PROP").ShouldBe(StudioModelFlags.StaticProp);

        // The comment IS the specification for what two-pass means, so it is asserted rather than
        // paraphrased into a doc comment where nothing would notice it going stale.
        Flat(header).ShouldContain(
            "// Use this when we want to render the opaque parts during the opaque pass");

        Flat(header).ShouldContain("// Means there's no bones and no transforms");
    }

    /// <summary>That <c>flags</c> sits between the clipping box and the bone count.</summary>
    [Test]
    public void StudioLayout_ForTheHeadersFlags_SitsBetweenTheClippingBoxAndTheBones()
    {
        string body = Body("studiohdr_t");

        Order(body, "view_bbmax").ShouldBeLessThan(Order(body, "flags"));
        Order(body, "flags").ShouldBeLessThan(Order(body, "numbones"));

        StudioLayout.HeaderFlagsOffset.ShouldBe(152);

        // Bracketed by two numbers that are not arithmetic: `view_bbmax` is load-bearing in the
        // render-bounds path and `numbones` has decoded correctly for months.
        StudioLayout.HeaderFlagsOffset.ShouldBe(
            StudioLayout.HeaderViewBoundsMaxOffset + 12, "flags follows view_bbmax, three floats");

        StudioLayout.HeaderBoneCountOffset.ShouldBe(
            StudioLayout.HeaderFlagsOffset + 4, "numbones follows flags");
    }

    /// <summary>That the static-prop bit agrees with the bone count on shipped models.</summary>
    /// <remarks>
    /// **The experiment.** Manipulation: none needed — the prediction is Valve's, made before this
    /// field was read. Condition: real shipped models of both kinds. Measurement: the bit at 152 and
    /// the count at 156. Control: the models WITHOUT the bit, which must have skeletons.
    ///
    /// A wrong offset falsifies this rather than degrading it. Reading four bytes late would put
    /// <c>numbones</c> in the flags word, so "has the static-prop bit" would become "bone count has
    /// bit 4 set" — true of roughly half of all bone counts and uncorrelated with anything.
    /// </remarks>
    [Test]
    public void Flags_ForShippedModels_AgreeWithTheBoneCountAboutStaticProps()
    {
        if (Models() is not { Count: > 0 } archives)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        List<(string Path, int Flags, int Bones)> read = [];

        foreach (string path in archives.Take(Sample))
        {
            if (Header(path) is not { } header)
            {
                continue;
            }

            read.Add((path, header.Flags, header.Bones));
        }

        read.Count.ShouldBeGreaterThan(
            20, "the sample must actually contain models, or this measures nothing");

        List<(string Path, int Flags, int Bones)> props =
            [.. read.Where(each => (each.Flags & StudioModelFlags.StaticProp) != 0)];

        List<(string Path, int Flags, int Bones)> animated =
            [.. read.Where(each => (each.Flags & StudioModelFlags.StaticProp) == 0)];

        TestContext.Out.WriteLine(
            $"{read.Count} models: {props.Count} static props, {animated.Count} not");

        // **Both kinds present, asserted rather than assumed.** A sample of only one kind would
        // pass every assertion below while establishing nothing — case 3 in the standing list of
        // ways a test goes blind, "no control".
        props.ShouldNotBeEmpty("some shipped models are compiled $staticprop");
        animated.ShouldNotBeEmpty("some shipped models have skeletons");

        foreach ((string path, int flags, int bones) in props)
        {
            bones.ShouldBeLessThanOrEqualTo(
                1,
                $"{path} carries STATIC_PROP (flags 0x{flags:X}) so studio.h says it has no bones");
        }

        // The other direction, so the correlation is not one-sided: a model with a real skeleton
        // must NOT be claiming to be a static prop.
        animated.Count(each => each.Bones > 1).ShouldBeGreaterThan(
            0, "the sample contains models with real skeletons");
    }

    /// <summary>That the reader surfaces the word it now decodes.</summary>
    /// <remarks>
    /// **The wiring assertion, which the bulk test above cannot make.** That one reads the bytes
    /// itself, so it would pass unchanged if <c>StudioModelInfo.Flags</c> were never populated —
    /// exactly the shape of no-op this project has shipped three times with a green suite.
    /// </remarks>
    [Test]
    public void Read_APlayerModel_SurfacesTheHeadersFlags()
    {
        if (File("models/player/scout.mdl") is not { } scout)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        StudioModelInfo model = StudioModel.Read(scout);

        TestContext.Out.WriteLine($"scout.mdl flags 0x{model.Flags:X8}");

        model.Flags.ShouldBe(
            BinaryPrimitives.ReadInt32LittleEndian(
                scout.AsSpan(StudioLayout.HeaderFlagsOffset)),
            "the property must be the word at 152, not a default");

        // A player is animated, so it cannot be a static prop. This is the one bit whose value is
        // known without measuring anything.
        (model.Flags & StudioModelFlags.StaticProp).ShouldBe(
            0, "a player model is not compiled $staticprop");
    }

    /// <summary>The <c>flags</c> and <c>numbones</c> words of a model in the archives.</summary>
    private static (int Flags, int Bones)? Header(string path)
    {
        if (File(path) is not { } bytes ||
            bytes.Length < StudioLayout.HeaderBoneCountOffset + sizeof(int))
        {
            return null;
        }

        return (
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(StudioLayout.HeaderFlagsOffset)),
            BinaryPrimitives.ReadInt32LittleEndian(
                bytes.AsSpan(StudioLayout.HeaderBoneCountOffset)));
    }

    /// <summary>Every model path the game ships, or empty when it is not installed.</summary>
    private static List<string> Models() =>
        !GameInstall.Available
            ? []
            : [.. Archives()
                .SelectMany(archive => archive.Paths)
                .Where(path => path.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)];

    private static byte[]? File(string path)
    {
        byte[]? found = null;

        foreach (VpkArchive archive in Archives())
        {
            found ??= archive.ReadFile(path);
        }

        return found;
    }

    private static List<VpkArchive> Archives() =>
        !GameInstall.Available
            ? []
            : [.. new[] { "tf2_misc_dir.vpk", "tf2_textures_dir.vpk" }
                .Select(name => Path.Combine(GameInstall.Require(), name))
                .Where(System.IO.File.Exists)
                .Select(VpkArchive.Open)];

    /// <summary>The value of a <c>#define</c>, so a constant is checked against the SDK's number.</summary>
    private static int Define(string header, string name)
    {
        Match found = Regex.Match(
            header,
            $@"#define\s+{Regex.Escape(name)}\s+0x([0-9A-Fa-f]+)",
            RegexOptions.None,
            Limit);

        found.Success.ShouldBeTrue($"studio.h defines {name}");

        return Convert.ToInt32(found.Groups[1].Value, 16);
    }

    /// <summary>One struct's body, so field order is measured inside it and not across the file.</summary>
    /// <remarks>
    /// **Same reason as `StudioBoundsConformanceTests.Body`, which learned it the hard way**:
    /// `flags` is declared in nine structs in `studio.h`, so a whole-file search reports an order
    /// that is true of the file and meaningless about the layout.
    /// </remarks>
    private static string Body(string name)
    {
        Match found = Regex.Match(
            Skip.Unless(SourceSdk.Text("src/public/studio.h"), SourceSdk.Missing),
            $@"struct {Regex.Escape(name)}\s*\r?\n\{{(.*?)\r?\n\}};",
            RegexOptions.Singleline,
            Limit);

        found.Success.ShouldBeTrue($"studio.h defines {name}");

        return found.Groups[1].Value;
    }

    private static int Order(string body, string field)
    {
        Match found = Regex.Match(body, $@"\b{Regex.Escape(field)}\b", RegexOptions.None, Limit);

        found.Success.ShouldBeTrue($"the struct declares {field}");

        return found.Index;
    }

    private static string Flat(string source) =>
        Regex.Replace(source, @"[ \t]+", " ", RegexOptions.None, Limit);
}
