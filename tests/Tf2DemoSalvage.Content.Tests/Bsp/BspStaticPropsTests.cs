using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// The models a map places: the rocks, crates and fences a decoded map is missing without them.
/// </summary>
/// <remarks>
/// **Measured against a shipped map rather than a fixture**, because the thing that can be wrong
/// here is the reading of a layout Valve never published a stride table for. A hand-built fixture
/// would encode this project's own belief about the structure and then confirm it, which is the
/// failure mode <c>docs/memory/fixtures-are-the-weak-point.md</c> records.
///
/// The assertions are chosen so a wrong stride cannot pass: model paths must all begin
/// <c>models/</c> and end <c>.mdl</c>, and every placement must land inside the map's own world
/// bounds. Both are properties of the CONTENT, so reading the array at the wrong offset produces
/// float noise and paths of nonsense rather than a plausible answer.
/// </remarks>
public sealed class BspStaticPropsTests
{
    /// <summary>A shipped map with props in it, when the game is installed.</summary>
    private static string? MapFile => GameInstall.Find("maps/cp_process_final.bsp");

    private ReadOnlyMemory<byte> _map;

    [SetUp]
    public void RequireAMap()
    {
        if (MapFile is not { } path)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run these.");
            return;
        }

        _map = File.ReadAllBytes(path);
    }

    [Test]
    public void Read_AShippedMap_CarriesSkinFamilies()
    {
        // **The measurement that says whether reading this field was worth anything.** A decode of
        // a member no map ever sets is a decode of zeroes, and would look identical to not reading
        // it at all — so the test is not "the field parses" but "some prop in a real map asks for a
        // family other than the first".
        IReadOnlyList<BspStaticProp> props = BspStaticProps.Read(_map);

        int skinned = props.Count(prop => prop.Skin > 0);

        TestContext.Out.WriteLine(
            $"{skinned} of {props.Count} placements name a skin family other than 0");

        // Every skin must index a real family rather than being arbitrary data read from the wrong
        // offset: a wrong offset here would produce large or negative numbers from neighbouring
        // fields, and m_FadeMinDist sits immediately after it.
        props.ShouldAllBe(prop => prop.Skin >= 0 && prop.Skin < 32);

        skinned.ShouldBeGreaterThan(
            0,
            "cp_process_final dresses its control points with skinned props; if this is zero the " +
            "offset is wrong or the field is being read from padding");
    }

    [Test]
    public void Read_AShippedMap_PlacesModels()
    {
        IReadOnlyList<BspStaticProp> props = BspStaticProps.Read(_map);

        props.Count.ShouldBeGreaterThan(100, "a competitive map is dressed with hundreds of props");
    }

    [Test]
    public void Read_EveryPlacement_NamesAModelPath()
    {
        // **The measurement that catches a wrong stride.** Read at the wrong offset, the model
        // index is arbitrary and either lands outside the dictionary - which throws - or picks a
        // real path at random, which this cannot catch. What it does catch is a dictionary read at
        // the wrong offset, where nothing is a path at all.
        IReadOnlyList<BspStaticProp> props = BspStaticProps.Read(_map);

        foreach (BspStaticProp prop in props)
        {
            prop.Model.ShouldStartWith("models/");
            prop.Model.ShouldEndWith(".mdl");
        }
    }

    [Test]
    public void Read_EveryPlacement_StandsInsideTheMap()
    {
        // A Source map is bounded at +/-16,384 units, so a placement outside that is not a
        // placement. Reading the origin at the wrong offset gives float noise, which fails this
        // by orders of magnitude rather than by a little.
        const float WorldLimit = 16384f;

        IReadOnlyList<BspStaticProp> props = BspStaticProps.Read(_map);

        foreach (BspStaticProp prop in props)
        {
            prop.X.ShouldBeInRange(-WorldLimit, WorldLimit);
            prop.Y.ShouldBeInRange(-WorldLimit, WorldLimit);
            prop.Z.ShouldBeInRange(-WorldLimit, WorldLimit);
        }
    }

    [Test]
    public void Read_TheAngles_AreDegrees()
    {
        // Angles are written in degrees and the compiler normalises them, so anything beyond a
        // full turn either way says the field is not the angle field.
        IReadOnlyList<BspStaticProp> props = BspStaticProps.Read(_map);

        foreach (BspStaticProp prop in props)
        {
            prop.Pitch.ShouldBeInRange(-360f, 360f);
            prop.Yaw.ShouldBeInRange(-360f, 360f);
            prop.Roll.ShouldBeInRange(-360f, 360f);
        }
    }

    [Test]
    public void Read_MostProps_StandUpright()
    {
        // **A range check on the angles cannot find the angle field**, which is the trap this
        // replaces. Moving the offset four bytes still reads two real angle components and then
        // the model index reinterpreted as a float - a denormal, comfortably inside +/-360 - so
        // every bound held while the fields were wrong.
        //
        // What separates them is the SHAPE of the data. A mapper turns a prop about the vertical
        // axis and almost never tilts one, so pitch and roll are exactly zero for the large
        // majority while yaw is spread across the circle. Read one field along, the old yaw
        // becomes the pitch and that majority collapses.
        IReadOnlyList<BspStaticProp> props = BspStaticProps.Read(_map);

        int upright = props.Count(prop => prop.Pitch == 0f && prop.Roll == 0f);
        int turned = props.Select(prop => prop.Yaw).Distinct().Count();

        upright.ShouldBeGreaterThan(
            props.Count / 2, "most props are turned about the vertical axis and not tilted");
        turned.ShouldBeGreaterThan(10, "and their yaw should be spread, not constant");
    }

    [Test]
    public void Read_TheScale_IsUsableForEveryPlacement()
    {
        // A zero scale collapses a model to a point, so the reader substitutes 1. Nothing should
        // reach the renderer that would draw nothing.
        BspStaticProps.Read(_map).ShouldAllBe(prop => prop.Scale > 0f);
    }

    [Test]
    public void Read_TheDictionary_IsSharedRatherThanRepeated()
    {
        // Hundreds of placements over far fewer distinct models is what a dictionary is for, and
        // it is the shape that says the model index was read rather than fabricated: one distinct
        // path per placement would mean the index is noise.
        IReadOnlyList<BspStaticProp> props = BspStaticProps.Read(_map);

        int distinct = props.Select(prop => prop.Model).Distinct(StringComparer.Ordinal).Count();

        distinct.ShouldBeLessThan(props.Count);
    }

    [Test]
    public void Read_AMapWithNoGameLump_HasNoProps()
    {
        // A map may legitimately place nothing, and that is not a failure. Built by zeroing the
        // game lump's directory entry in a real map, so everything else about the file stays valid.
        byte[] stripped = _map.ToArray();
        BspHeader header = BspHeader.Parse(stripped);

        // The lump directory begins after the identifier and version, 16 bytes per entry.
        int entry = 8 + (35 * 16);

        stripped.AsSpan(entry, 8).Clear();

        BspStaticProps.Read(stripped).ShouldBeEmpty();
        header.Lump(35).Length.ShouldBeGreaterThan(0, "the original should have had one");
    }
}
