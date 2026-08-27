using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The IK chain table, against hand-built bytes and against models TF2 ships.
/// </summary>
/// <remarks>
/// **The engine's entire IK stage is gated on <c>numikchains()</c>**, which this reader has never
/// read — so <c>CalculateIKLocks</c> was not merely unwired, it had nothing to run on (B182). This
/// is the table, not the solve.
///
/// **Both halves, for the reason the bone flags needed both.** A fixture proves the reader agrees
/// with bytes written by the same hand; only Valve's own files can show it agrees with the format.
/// </remarks>
public sealed class StudioIkChainTests
{
    private const int HeaderSize = 408;

    private static string Game => GameInstall.Require();

    [Test]
    public void Read_AChainWithThreeLinks_ReportsThemRootFirst()
    {
        IReadOnlyList<StudioIkChain> chains = StudioIkChains.Read(
            Model(Chain("rfoot", linkType: 0, bones: [10, 11, 12])));

        chains.Count.ShouldBe(1);
        chains[0].Name.ShouldBe("rfoot");
        chains[0].Links.Select(link => link.Bone).ShouldBe([10, 11, 12]);
    }

    [Test]
    public void Read_TheKneeDirection_SurvivesOnTheLinkThatCarriesIt()
    {
        // **The field that decides which way a knee bends**, and the only one a distance-based
        // check cannot notice is missing: a leg solved without it still reaches the target.
        IReadOnlyList<StudioIkChain> chains = StudioIkChains.Read(
            Model(Chain("lfoot", linkType: 0, bones: [1, 2, 3], kneeOn: 1, knee: (0f, 1f, 0f))));

        chains[0].Links[1].KneeDirection.ShouldBe((0f, 1f, 0f));

        // The control: the other links must NOT carry it, or a reader writing one value into every
        // link would pass the assertion above.
        chains[0].Links[0].KneeDirection.ShouldBe((0f, 0f, 0f));
        chains[0].Links[2].KneeDirection.ShouldBe((0f, 0f, 0f));
    }

    [Test]
    public void Read_TwoChains_KeepTheirOwnLinksApart()
    {
        // Chains are addressed by index from an animation's IK rules, so a reader that read the
        // second chain's links from the first chain's offset would be wrong in a way that still
        // produces a plausible skeleton.
        IReadOnlyList<StudioIkChain> chains = StudioIkChains.Read(
            Model(
                Chain("rfoot", linkType: 0, bones: [10, 11, 12]),
                Chain("lfoot", linkType: 0, bones: [20, 21, 22])));

        chains.Count.ShouldBe(2);
        chains[0].Links.Select(link => link.Bone).ShouldBe([10, 11, 12]);
        chains[1].Links.Select(link => link.Bone).ShouldBe([20, 21, 22]);
    }

    [Test]
    public void Read_AModelWithNoChains_ReportsNoneRatherThanThrowing()
    {
        StudioIkChains.Read(new byte[HeaderSize]).ShouldBeEmpty();
    }

    [Test]
    public void Read_AHeaderClaimingMoreChainsThanTheFileHolds_IsRefused()
    {
        // Untrusted input, per D32: a model can come from a downloaded map, and a count read from a
        // corrupt header is the whole attack. Refused rather than clamped, because a chain table
        // that runs past the file has no correct partial reading.
        byte[] file = new byte[HeaderSize];

        BinaryPrimitives.WriteInt32LittleEndian(
            file.AsSpan(StudioLayout.HeaderIkChainCountOffset), 4);
        BinaryPrimitives.WriteInt32LittleEndian(
            file.AsSpan(StudioLayout.HeaderIkChainIndexOffset), HeaderSize - 8);

        Should.Throw<InvalidDataException>(() => StudioIkChains.Read(file));
    }

    [Test]
    public void Read_AChainWhoseLinksRunPastTheFile_LosesThatChainAndKeepsTheRest()
    {
        // A defect in one chain is not a defect in the model. Refusing everything would lose the
        // chains that are fine, which is the same reasoning as clamping a frame rather than
        // refusing a sequence.
        IReadOnlyList<StudioIkChain> chains = StudioIkChains.Read(
            Model(
                Chain("good", linkType: 0, bones: [1, 2, 3]),
                Chain("broken", linkType: 0, bones: [4, 5, 6], linkCountOverride: 1_000_000)));

        chains.Count.ShouldBe(2);
        chains[0].Links.Count.ShouldBe(3);
        chains[1].Links.ShouldBeEmpty();
    }

    [Test]
    public void Read_APlayerModelTf2Ships_DeclaresTheChainsItsFeetNeed()
    {
        IReadOnlyList<StudioIkChain> chains = Chains("models/player/heavy.mdl");

        // Reported by the run rather than asserted as a count, because how many chains a class
        // declares is a fact about one model version.
        TestContext.Out.WriteLine(
            $"heavy.mdl: {chains.Count} chains — " +
            string.Join(", ", chains.Select(chain => $"{chain.Name} ({chain.Links.Count} links)")));

        chains.ShouldNotBeEmpty("a humanoid declares IK chains for its feet");

        // Every link must address a real bone. A reader off by one field would return large or
        // negative indices here, which is the failure that produces a plausible-looking skeleton.
        int bones = StudioBones.Read(File("models/player/heavy.mdl")).Count;

        foreach (StudioIkChain chain in chains)
        {
            chain.Name.ShouldNotBeNullOrEmpty();
            chain.Links.ShouldNotBeEmpty();

            foreach (StudioIkLink link in chain.Links)
            {
                link.Bone.ShouldBeInRange(0, bones - 1);
            }
        }
    }

    /// <summary>One chain's bytes, its links, and its name.</summary>
    private sealed record BuiltChain(
        string Name, int LinkType, int[] Bones, int KneeOn, (float X, float Y, float Z) Knee,
        int? LinkCountOverride);

    private static BuiltChain Chain(
        string name,
        int linkType,
        int[] bones,
        int kneeOn = -1,
        (float X, float Y, float Z) knee = default,
        int? linkCountOverride = null) =>
        new(name, linkType, bones, kneeOn, knee, linkCountOverride);

    /// <summary>A minimal <c>.mdl</c>: a header, a chain table, the link runs, then the names.</summary>
    private static byte[] Model(params BuiltChain[] chains)
    {
        int table = HeaderSize;
        int linksAt = table + (chains.Length * StudioLayout.IkChainStride);

        int linkBytes = chains.Sum(chain => chain.Bones.Length * StudioLayout.IkLinkStride);
        int stringsAt = linksAt + linkBytes;

        List<byte> text = [];
        List<int> nameOffsets = [];

        foreach (BuiltChain chain in chains)
        {
            nameOffsets.Add(stringsAt + text.Count);

            foreach (char letter in chain.Name)
            {
                text.Add((byte)letter);
            }

            text.Add(0);
        }

        byte[] file = new byte[stringsAt + text.Count];

        BinaryPrimitives.WriteInt32LittleEndian(
            file.AsSpan(StudioLayout.HeaderIkChainCountOffset), chains.Length);
        BinaryPrimitives.WriteInt32LittleEndian(
            file.AsSpan(StudioLayout.HeaderIkChainIndexOffset), table);

        int linkCursor = linksAt;

        for (int index = 0; index < chains.Length; index++)
        {
            BuiltChain chain = chains[index];
            int at = table + (index * StudioLayout.IkChainStride);

            Write(file, at + StudioLayout.IkChainNameOffset, nameOffsets[index] - at);
            Write(file, at + StudioLayout.IkChainLinkTypeOffset, chain.LinkType);
            Write(
                file,
                at + StudioLayout.IkChainLinkCountOffset,
                chain.LinkCountOverride ?? chain.Bones.Length);
            Write(file, at + StudioLayout.IkChainLinkIndexOffset, linkCursor - at);

            for (int link = 0; link < chain.Bones.Length; link++)
            {
                int linkAt = linkCursor + (link * StudioLayout.IkLinkStride);

                Write(file, linkAt + StudioLayout.IkLinkBoneOffset, chain.Bones[link]);

                if (link == chain.KneeOn)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(
                        file.AsSpan(linkAt + StudioLayout.IkLinkKneeDirectionOffset), chain.Knee.X);
                    BinaryPrimitives.WriteSingleLittleEndian(
                        file.AsSpan(linkAt + StudioLayout.IkLinkKneeDirectionOffset + 4), chain.Knee.Y);
                    BinaryPrimitives.WriteSingleLittleEndian(
                        file.AsSpan(linkAt + StudioLayout.IkLinkKneeDirectionOffset + 8), chain.Knee.Z);
                }
            }

            linkCursor += chain.Bones.Length * StudioLayout.IkLinkStride;
        }

        text.CopyTo(file, stringsAt);

        return file;
    }

    private static void Write(byte[] into, int at, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(into.AsSpan(at), value);

    private static byte[] File(string path)
    {
        if (!Directory.Exists(Game))
        {
            Assert.Ignore("Team Fortress 2 is not installed");
            return [];
        }

        byte[]? file = GameArchives.Open(Game).Read(path);

        if (file is null)
        {
            Assert.Ignore($"{path} is not in this install");
            return [];
        }

        return file;
    }

    private static IReadOnlyList<StudioIkChain> Chains(string path) => StudioIkChains.Read(File(path));
}
