using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// <c>g_skinref[skin][skinref]</c> — which texture a mesh paints with, at a given skin.
/// </summary>
/// <remarks>
/// **The citation is Valve's own comment, and it is in an open file**:
/// <c>src/utils/motionmapper/motionmapper.h:134</c>
///
/// <code>
///   EXTERN  int g_skinref[256][MAXSTUDIOSKINS]; // [skin][skinref], returns texture index
/// </code>
///
/// with the structure it describes declared in <c>public/studio.h:2238</c> —
/// <c>numskinref</c>, <c>numskinfamilies</c>, <c>skinindex</c>, and
/// <c>pSkinref( int i )</c> reaching one flat array of shorts. A mesh's own
/// <c>mstudiomesh_t::material</c> (<c>studio.h:1365</c>) is NOT a texture index: it is a
/// <i>skinref</i>, and the skin chooses the row that turns it into one.
///
/// **Written down because getting it slightly wrong is invisible on almost every model (B229).**
/// The overwhelming majority of props have one skin family, where <c>skinref[0][r] == r</c> and any
/// reading at all agrees with any other. The divergence shows up only on a model with several
/// families, and then only when the families differ in what is actually SHIPPED.
///
/// This project privileged family zero — it resolved family zero's material for each mesh and
/// expressed every other family as a swap FROM that resolved index. On `cp_fulgur`,
/// `props_aquatic/pipe_256.mdl` has 15 families over one mesh; the map places skins 1 and 12, whose
/// textures are packed, and does not pack family zero's. Family zero resolved to −1, a swap keyed on
/// −1 was refused, and every pipe on the map drew in the missing-material chequer — while the game
/// itself draws them correctly, because the game never asks family zero anything.
///
/// **Synthetic, and that is the point (D38).** The table is hand-built here, so the test HAS ground
/// truth rather than comparing two readings of a file; it runs on CI and on the measurement boxes,
/// where a test needing a real model cannot.
/// </remarks>
public sealed class StudioSkinsConformanceTests
{
    /// <summary>
    /// Three references wide, three families tall, family-major — the layout `pSkinref` indexes.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT an identity table. A table where <c>skinref[f][r] == r</c> gives the same
    /// answer for every family, so it cannot distinguish a correct lookup from one that ignores the
    /// skin entirely — which is the failure this exists to catch. Row 0 is the identity, and the
    /// other two permute, so each row is distinguishable from the others AND from its own index.
    /// </remarks>
    private static readonly short[] Table =
    [
        0, 1, 2,   // family 0
        7, 1, 2,   // family 1 — only the first reference moves, as TF2's own props do
        9, 4, 2,   // family 2
    ];

    private const int References = 3;
    private const int Families = 3;

    [Test]
    public void TextureFor_FamilyZero_IsTheFirstRowRatherThanTheReferenceItself()
    {
        // The row happens to be the identity here, which is what makes the NEXT test meaningful:
        // both readings agree at family zero, and only differ above it.
        StudioSkins.TextureFor(Table, References, Families, skin: 0, reference: 0).ShouldBe(0);
        StudioSkins.TextureFor(Table, References, Families, skin: 0, reference: 1).ShouldBe(1);
        StudioSkins.TextureFor(Table, References, Families, skin: 0, reference: 2).ShouldBe(2);
    }

    [Test]
    public void TextureFor_AFamilyAboveZero_TakesItsOwnRowAndNotFamilyZeros()
    {
        // `[skin][skinref]`. An implementation that resolved family zero and then remapped from it
        // would still answer 7 here — the swap 0→7 exists — so this alone is not decisive. The test
        // below is.
        StudioSkins.TextureFor(Table, References, Families, skin: 1, reference: 0).ShouldBe(7);
        StudioSkins.TextureFor(Table, References, Families, skin: 2, reference: 1).ShouldBe(4);
    }

    [Test]
    public void TextureFor_AReferenceFamilyZeroSharesWithAnother_StillTakesItsOwnRow()
    {
        // **The case a swap-from-family-zero design cannot express, and the reason B229 existed.**
        // At family 0 references 1 and 2 name textures 1 and 2; at family 2 they name 4 and 2. A
        // design keyed on the FAMILY-ZERO TEXTURE has to answer "what does texture 1 become at
        // family 2" — which is only askable because no other reference happens to share it. The
        // moment two references collide at family zero the question has two answers, and the engine
        // never asks it: it indexes by reference.
        StudioSkins.TextureFor(Table, References, Families, skin: 2, reference: 0).ShouldBe(9);
        StudioSkins.TextureFor(Table, References, Families, skin: 2, reference: 2).ShouldBe(2);
    }

    [Test]
    public void TextureFor_ASkinBeyondTheTable_FallsBackToFamilyZero()
    {
        // `props_shared.cpp:1079` — `if ( nActualSkin > studioHdrModel.numskinfamilies() )
        // nActualSkin = 0;`. A placement naming a family the model does not have is data this
        // project does not control (D32), and the engine's answer is family zero rather than a
        // refusal.
        StudioSkins.TextureFor(Table, References, Families, skin: 3, reference: 0).ShouldBe(0);
        StudioSkins.TextureFor(Table, References, Families, skin: -1, reference: 1).ShouldBe(1);
    }

    [Test]
    public void TextureFor_AReferenceOutsideTheTable_IsRefused()
    {
        // A mesh naming a reference the table does not have cannot be answered, and −1 is what the
        // caller already treats as "draw the missing-material chequer". Guessing a row here would
        // paint a mesh with somebody else's texture, which is worse than magenta because nobody
        // investigates it.
        StudioSkins.TextureFor(Table, References, Families, skin: 0, reference: 3).ShouldBe(-1);
        StudioSkins.TextureFor(Table, References, Families, skin: 0, reference: -1).ShouldBe(-1);
    }

    [Test]
    public void TextureFor_AModelWithNoSkinTable_IsTheReferenceItself()
    {
        // **A model with no table is the common case, not an error.** Most props have one family
        // and many carry no table at all, and for them a mesh's `material` already IS the texture
        // index — so the identity is the answer rather than a refusal.
        StudioSkins.TextureFor([], references: 0, families: 0, skin: 0, reference: 2).ShouldBe(2);
    }

    [Test]
    public void TextureFor_ATruncatedTable_FallsBackToTheReference()
    {
        // The header's counts and the table's length are separate facts in a file this project does
        // not control, and a short table is the shape a malformed model takes. Reading past it would
        // throw during a map load; answering the reference draws the model with family zero's
        // materials, which is the same thing a model with no table gets.
        StudioSkins.TextureFor([0, 1], references: 3, families: 3, skin: 2, reference: 1).ShouldBe(1);
    }
}
