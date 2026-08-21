using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// How the engine hangs an item off a named point on its wearer.
/// </summary>
/// <remarks>
/// **This is the other way an item rides a player, and not implementing it puts things on the
/// floor.** A hat shares bone names with the player and is bone-merged; a halo, an MvM canteen and a
/// spellbook do not. <c>hwn_spellbook_complete.mdl</c> has one bone, called <c>mvm</c>, and no player
/// skeleton has a bone by that name — so <c>MergeMatchingBones</c> matches nothing and the item falls
/// back to the wearer's transform, which on a player is their feet (RISKS B82).
///
/// The engine parents those to an ATTACHMENT instead. Two halves, and this project reads neither:
///
/// **The model half.** <c>mstudioattachment_t</c> (<c>studio.h:511</c>) carries a name, the bone it
/// hangs from, and a local matrix:
///
/// <code>
/// int sznameindex; unsigned int flags; int localbone; matrix3x4_t local; int unused[8];
/// </code>
///
/// and <c>SetupBones_AttachmentHelper</c> composes it against that bone's world matrix:
///
/// <code>
/// ConcatTransforms( GetBone( iBone ), pattachment.local, world );
/// ...
/// PutAttachment( i + 1, world );
/// </code>
///
/// **Note the `i + 1`.** Attachments are stored one-based, so a parent attachment of 0 means "not
/// attached" and 1 is the model's first. An implementation that indexed from zero would hang every
/// item off the wrong point — and off a real one, so it would look like a placement bug rather than
/// an off-by-one.
///
/// **The entity half.** <c>m_iParentAttachment</c> is networked on <c>DT_BaseEntity</c>
/// (<c>baseentity.cpp:288</c>) at <c>NUM_PARENTATTACHMENT_BITS</c>, which is 6
/// (<c>baseentity_shared.h:41</c>), unsigned.
/// </remarks>
public sealed class ParentAttachmentConformanceTests
{
    /// <summary>The property that says which attachment an entity hangs from.</summary>
    private const string ParentAttachment = "m_iParentAttachment";

    /// <summary>Where the engine keeps it.</summary>
    private const string BaseEntityTable = "DT_BaseEntity";

    /// <summary><c>NUM_PARENTATTACHMENT_BITS</c>, <c>baseentity_shared.h:41</c>.</summary>
    private const int ParentAttachmentBits = 6;

    /// <summary>Source's <c>SPROP_UNSIGNED</c>.</summary>
    private const int UnsignedFlag = 1 << 0;

    [Test]
    public void ParentAttachment_OnEveryDemo_Is6BitsUnsignedOnBaseEntity()
    {
        // **Every era carries it**, which is what makes B82 fixable rather than a modern-only
        // feature: the property is on DT_BaseEntity from the 2007 build onward, so a halo in a 2009
        // recording can be placed as correctly as one in a modern match.
        //
        // Asserted against each demo's own schema rather than against the header, for the reason
        // this project keeps relearning: the header is one build's snapshot and the demo is the
        // build that recorded it.
        List<string> checkedDemos = [];

        foreach (string path in Corpus.FilesWithSchema())
        {
            SendProperty found = Corpus.Schema(path).Tables
                .Where(table => string.Equals(table.Name, BaseEntityTable, StringComparison.Ordinal))
                .SelectMany(table => table.Properties)
                .First(property => string.Equals(
                    property.Name, ParentAttachment, StringComparison.Ordinal));

            found.BitCount.ShouldBe(
                ParentAttachmentBits, $"{Path.GetFileName(path)}: NUM_PARENTATTACHMENT_BITS is 6");

            (found.Flags & UnsignedFlag).ShouldNotBe(
                0, $"{Path.GetFileName(path)}: the parent attachment is SPROP_UNSIGNED");

            checkedDemos.Add(Path.GetFileName(path));
        }

        // A positive control: an empty sweep satisfies every loop above vacuously, and this project
        // has been caught by absence claims that were facts about the search.
        checkedDemos.ShouldNotBeEmpty("no demo declares a parent attachment at all");
    }

    [Test]
    public void ParentAttachment_ItsRange_CannotAddressEveryAttachmentAModelMayHave()
    {
        // **Six bits is 0..63, and 0 means "none" because the engine stores attachments one-based**
        // (`PutAttachment( i + 1, world )`). So the wire can name attachments 1 through 63 — the
        // first 63 of a model.
        //
        // Stated as arithmetic rather than measured, because it is a property of the field's width
        // and not of any recording: a model with more than 63 attachments has some that cannot be
        // named, and that is the engine's limit rather than this reader's. Worth writing down so a
        // future "attachment 0 is missing" is recognised as the one-based convention rather than
        // chased as a decode fault.
        int highest = (1 << ParentAttachmentBits) - 1;

        highest.ShouldBe(63);
    }
}
