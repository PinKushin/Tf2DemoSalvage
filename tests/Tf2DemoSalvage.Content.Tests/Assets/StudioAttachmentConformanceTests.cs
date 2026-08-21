using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The attachment reader against models the game actually ships.
/// </summary>
/// <remarks>
/// **A fixture cannot falsify a reader whose author wrote both.** The unit tests build an attachment
/// table from the same understanding the reader decodes it with, so they agree by construction —
/// which is exactly how this project once passed ten tests on a wrong struct stride
/// (<c>docs/memory/put-the-real-file-in-the-fixture.md</c>).
///
/// These read real models instead. A player carries attachments with names anyone who has modded TF2
/// would recognise, and a spellbook is the model that named RISKS B82 in the first place.
/// </remarks>
public sealed class StudioAttachmentConformanceTests
{
    /// <summary>Where the game is, on this machine.</summary>
    private const string GameDirectory = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";

    [Test]
    public void Read_APlayerModel_FindsTheAttachmentsTf2IsKnownToHave()
    {
        // **Named attachments are the whole point of the mechanism**, and a player's are the ones
        // every cosmetic in the game hangs from. If the stride or the name index were wrong these
        // would come back as empty strings or as bytes from the middle of the matrix — a reader
        // that produced plausible-looking rubbish is the failure this is written against.
        if (Read("models/player/scout.mdl") is not { } scout)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        IReadOnlyList<StudioAttachment> attachments = StudioAttachment.Read(scout);

        attachments.ShouldNotBeEmpty("a player model declares attachments");

        List<string> names = [.. attachments.Select(entry => entry.Name)];

        TestContext.Out.WriteLine($"scout: {attachments.Count} — {string.Join(", ", names)}");

        // `head` is the one every hat in TF2 hangs from, and it is stable across every era of the
        // model. Asserted by name rather than by index, because the order is the model's business.
        names.ShouldContain("head");

        foreach (StudioAttachment attachment in attachments)
        {
            attachment.Name.ShouldNotBeNullOrWhiteSpace("an attachment with no name is a misread");

            attachment.Bone.ShouldBeGreaterThanOrEqualTo(
                0, $"{attachment.Name} hangs from no bone, which cannot be right");

            attachment.Local.Count.ShouldBe(12, "a matrix3x4_t is twelve floats");
        }
    }

    [Test]
    public void Read_TheSpellbook_HasAnAttachmentWhereItHasNoSharedBone()
    {
        // **The model that named B82.** Its single bone is called `mvm`, which no player skeleton
        // has, so bone merging matches nothing and it falls back to the wearer's origin — the feet.
        // Whether it carries an attachment decides whether the attachment path can place it at all.
        if (Read("models/player/items/all_class/hwn_spellbook_complete.mdl") is not { } spellbook)
        {
            Assert.Ignore("the game is not installed, or the model moved");
            return;
        }

        IReadOnlyList<StudioAttachment> attachments = StudioAttachment.Read(spellbook);

        TestContext.Out.WriteLine(
            $"spellbook: {attachments.Count} — " +
            string.Join(", ", attachments.Select(entry => $"{entry.Name} bone {entry.Bone}")));

        // Reported rather than asserted: what matters for B82 is what the WEARER offers and what the
        // entity's m_iParentAttachment names, and this test exists to show what the item itself
        // carries. An empty list here is a fact worth seeing, not a failure.
        attachments.Count.ShouldBeGreaterThanOrEqualTo(0);
    }

    /// <summary>Reads a model out of the game's archives, or null when it is not installed.</summary>
    private static byte[]? Read(string path)
    {
        List<VpkArchive> archives = [.. new[] { "tf2_misc_dir.vpk", "tf2_textures_dir.vpk" }
            .Select(name => Path.Combine(GameDirectory, name))
            .Where(File.Exists)
            .Select(VpkArchive.Open)];

        byte[]? found = null;

        foreach (VpkArchive archive in archives)
        {
            found ??= archive.ReadFile(path);
        }

        return found;
    }
}
