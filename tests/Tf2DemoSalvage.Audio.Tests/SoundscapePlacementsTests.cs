using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>Where a map's soundscapes stand and which one a listener hears, on hand-written entity lumps (B217).</summary>
/// <remarks>
/// **Synthetic, so the machine without TF2 runs it** (D38). `SoundscapeSelectionConformanceTests` reads a real map from the
/// install and ignores itself on the mutation box, which left every mutant here uncovered — 53 of them, zero killed. The
/// fixtures are entity-lump text in the map's own format, parsed by the production reader.
/// </remarks>
public sealed class SoundscapePlacementsTests
{
    private static readonly SoundscapeCatalog Catalog = SoundscapeCatalog.Load(
        path => path switch
        {
            "scripts/soundscapes_manifest.txt" =>
                Encoding.UTF8.GetBytes("\"soundscapes_manifest\"\n{\n    \"file\"    \"scripts/soundscapes_test.txt\"\n}\n"),
            "scripts/soundscapes_test.txt" =>
                Encoding.UTF8.GetBytes("\"test.first\"\n{\n}\n\"test.second\"\n{\n}\n"),
            _ => null,
        });

    private static IReadOnlyList<BspEntity> Entities(string lump) => BspEntities.Parse(Encoding.UTF8.GetBytes(lump));

    private static bool Clear((float X, float Y, float Z) from, (float X, float Y, float Z) to) => true;

    [Test]
    public void From_ASoundscapeTheCatalogKnows_CarriesItsIndexOriginAndRadius()
    {
        SoundscapePlacement placed = SoundscapePlacements.From(
            Entities("{\n\"classname\" \"env_soundscape\"\n\"soundscape\" \"test.second\"\n\"origin\" \"10 20 30\"\n\"radius\" \"256\"\n}\n"),
            Catalog).Placements.ShouldHaveSingleItem();

        placed.Name.ShouldBe("test.second");
        placed.Index.ShouldBe(1);
        (placed.X, placed.Y, placed.Z).ShouldBe((10f, 20f, 30f));
        placed.Radius.ShouldBe(256f);
        placed.Cluster.ShouldBe(-1, "no leaf tree was given");
    }

    [Test]
    public void From_ASoundscapeTheCatalogDoesNotKnowAndNoRadius_IsIndexMinusOneAndUnbounded()
    {
        SoundscapePlacement placed = SoundscapePlacements.From(
            Entities("{\n\"classname\" \"env_soundscape\"\n\"soundscape\" \"test.missing\"\n\"origin\" \"0 0 0\"\n}\n"),
            Catalog).Placements.ShouldHaveSingleItem();

        placed.Index.ShouldBe(-1);
        placed.Radius.ShouldBe(-1f);
    }

    [Test]
    public void From_AProxy_TakesItsMastersSoundscapeAndItsOwnOrigin()
    {
        IReadOnlyList<SoundscapePlacement> placed = SoundscapePlacements.From(
            Entities(
                "{\n\"classname\" \"env_soundscape\"\n\"targetname\" \"master\"\n\"soundscape\" \"test.first\"\n\"origin\" \"0 0 0\"\n}\n" +
                "{\n\"classname\" \"env_soundscape_proxy\"\n\"MainSoundscapeName\" \"master\"\n\"origin\" \"5 6 7\"\n}\n"),
            Catalog).Placements;

        placed.Count.ShouldBe(2);
        placed[1].Name.ShouldBe("test.first");
        placed[1].Index.ShouldBe(0);
        (placed[1].X, placed[1].Y, placed[1].Z).ShouldBe((5f, 6f, 7f));
        placed[1].Id.ShouldBe(1);
    }

    [Test]
    public void From_AProxyWithAnUnknownMasterOrAnEntityWithNoOrigin_IsSkipped()
    {
        SoundscapePlacements.From(
            Entities(
                "{\n\"classname\" \"env_soundscape_proxy\"\n\"MainSoundscapeName\" \"nobody\"\n\"origin\" \"5 6 7\"\n}\n" +
                "{\n\"classname\" \"env_soundscape\"\n\"soundscape\" \"test.first\"\n}\n" +
                "{\n\"classname\" \"env_soundscape\"\n\"soundscape\" \"test.first\"\n\"origin\" \"1 2\"\n}\n" +
                "{\n\"classname\" \"info_target\"\n\"soundscape\" \"test.first\"\n\"origin\" \"1 2 3\"\n}\n"),
            Catalog).Placements.ShouldBeEmpty();
    }

    [Test]
    public void From_PositionKeys_ResolveToTheirTargetsOriginsInSlotOrder()
    {
        SoundscapePlacement placed = SoundscapePlacements.From(
            Entities(
                "{\n\"classname\" \"env_soundscape\"\n\"soundscape\" \"test.first\"\n\"origin\" \"0 0 0\"\n" +
                "\"position0\" \"a\"\n\"position2\" \"b\"\n\"position3\" \"\"\n\"position4\" \"missing\"\n}\n" +
                "{\n\"classname\" \"info_target\"\n\"targetname\" \"b\"\n\"origin\" \"4 5 6\"\n}\n" +
                "{\n\"classname\" \"info_target\"\n\"targetname\" \"A\"\n\"origin\" \"1 2 3\"\n}\n"),
            Catalog).Placements.ShouldHaveSingleItem();

        placed.Positions.ShouldBe([(1f, 2f, 3f), (4f, 5f, 6f)]);
    }

    private static SoundscapePlacements Two(string firstRadius, string secondRadius) =>
        SoundscapePlacements.From(
            Entities(
                $"{{\n\"classname\" \"env_soundscape\"\n\"soundscape\" \"test.first\"\n\"origin\" \"100 0 0\"\n\"radius\" \"{firstRadius}\"\n}}\n" +
                $"{{\n\"classname\" \"env_soundscape\"\n\"soundscape\" \"test.second\"\n\"origin\" \"300 0 0\"\n\"radius\" \"{secondRadius}\"\n}}\n"),
            Catalog);

    [Test]
    public void Choose_WithNothingHeld_TakesTheNearestInRadius()
    {
        Two("500", "500").Choose(0f, 0f, 0f, Clear)?.Name.ShouldBe("test.first");
        Two("500", "500").Choose(280f, 0f, 0f, Clear)?.Name.ShouldBe("test.second");
    }

    [Test]
    public void Choose_AListenerOutsideEveryRadius_ChoosesNothing()
    {
        Two("50", "50").Choose(0f, 0f, 1000f, Clear).ShouldBeNull();
    }

    [Test]
    public void Choose_AnEdgeOfTheRadius_IsOutside()
    {
        // `radius > range`: exactly at the radius is not within it.
        Two("100", "10").Choose(0f, 0f, 0f, Clear).ShouldBeNull();
    }

    [Test]
    public void Choose_ANegativeRadius_IsInRangeEverywhere()
    {
        Two("-1", "10").Choose(0f, 0f, 90000f, Clear)?.Name.ShouldBe("test.first");
    }

    [Test]
    public void Choose_ABlockedLineOfSight_IsNotChosen()
    {
        Two("500", "500").Choose(0f, 0f, 0f, (from, _) => from.X > 200f)?.Name.ShouldBe("test.second");
    }

    [Test]
    public void Choose_AHeldSoundscapeStillInRange_KeepsItAgainstAFartherOne()
    {
        SoundscapePlacements placements = Two("1000", "1000");
        SoundscapePlacement held = placements.Placements[1];

        // Listener at 150: held is 150 away, the other only 50 — the nearer one replaces it.
        placements.Choose(150f, 0f, 0f, Clear, held)?.Name.ShouldBe("test.first");

        // Listener at 250: held is 50 away, the other 150 — the held one stays.
        placements.Choose(250f, 0f, 0f, Clear, held)?.Name.ShouldBe("test.second");
    }

    [Test]
    public void Choose_AHeldSoundscapeNowOutOfSight_LetsAFartherOneTakeOver()
    {
        SoundscapePlacements placements = Two("1000", "1000");
        SoundscapePlacement held = placements.Placements[1];

        placements.Choose(250f, 0f, 0f, (from, _) => from.X < 200f, held)?.Name.ShouldBe("test.first");
    }

    [Test]
    public void Choose_NothingElseQualifies_KeepsWhatIsHeldEvenOutOfRange()
    {
        SoundscapePlacements placements = Two("10", "10");
        SoundscapePlacement held = placements.Placements[0];

        placements.Choose(0f, 0f, 5000f, Clear, held)?.Name.ShouldBe("test.first");
    }
}
