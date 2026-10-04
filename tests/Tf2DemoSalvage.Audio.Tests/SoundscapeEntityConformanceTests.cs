using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>What the SERVER makes of a map's soundscape entities, read from <c>game/server/soundscape.cpp</c> (B464).</summary>
/// <remarks>
/// **A SourceTV demo carries no player's audio params, so this project plays the server's part** (B173): it reads the
/// entities as <c>CEnvSoundscape</c> reads its keyfields, and chooses as <c>UpdateForPlayer</c> chooses. Each case
/// below is a branch of that code with its line, written before the implementation it checks.
///
/// Census of the 239 installed maps, `soundscape-entities` probe, 2026-10-04: 4,781 `env_soundscape`, 6,616 proxies,
/// 109 soundscapes whose `position&lt;N&gt;` keys leave a gap, 1,094 proxies of a master that carries positions, and 82
/// entities with `StartDisabled` set (78 on ctf_helltrain_event, 4 on pass_district).
/// </remarks>
public sealed class SoundscapeEntityConformanceTests
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

    /// <remarks>
    /// <c>WriteAudioParamsTo</c> (<c>soundscape.cpp:217-229</c>) sets bit <c>i</c> and <c>localSound[i]</c> for each
    /// slot whose name resolves, so slot <c>i</c> KEEPS its index; the client's <c>"position" "N"</c> reads
    /// <c>localSound[N]</c> gated on bit N (<c>c_soundscape.cpp:797-804</c>). A soundscape setting only `position1`
    /// — the commonest gap in the census — must answer position 1, and position 0 must be absent.
    /// </remarks>
    [Test]
    public void From_APositionGap_KeepsEachTargetAtItsOwnSlot()
    {
        SoundscapePlacement placed = SoundscapePlacements.From(
            Entities(
                "{\n\"classname\" \"env_soundscape\"\n\"soundscape\" \"test.first\"\n\"origin\" \"0 0 0\"\n" +
                "\"position1\" \"a\"\n\"position3\" \"missing\"\n\"position4\" \"b\"\n}\n" +
                "{\n\"classname\" \"info_target\"\n\"targetname\" \"a\"\n\"origin\" \"1 2 3\"\n}\n" +
                "{\n\"classname\" \"info_target\"\n\"targetname\" \"b\"\n\"origin\" \"4 5 6\"\n}\n"),
            Catalog).Placements.ShouldHaveSingleItem();

        placed.Positions.Count.ShouldBe(8, "NUM_AUDIO_LOCAL_SOUNDS slots, set or not");
        placed.Positions[0].ShouldBeNull("position0 is not set");
        placed.Positions[1].ShouldBe((1f, 2f, 3f));
        placed.Positions[3].ShouldBeNull("position3 names nothing, so its bit stays clear");
        placed.Positions[4].ShouldBe((4f, 5f, 6f));
    }

    /// <remarks>
    /// <c>CEnvSoundscapeProxy::Activate</c> (<c>soundscape.cpp:49-55</c>): <i>"Copy the relevant parameters from our
    /// main soundscape"</i> — the index AND every <c>m_positionNames[i]</c>. A proxy writes its master's targets into
    /// the player's audio params; its own keys never reach them.
    /// </remarks>
    [Test]
    public void From_AProxy_TakesItsMastersPositionsNotItsOwn()
    {
        IReadOnlyList<SoundscapePlacement> placed = SoundscapePlacements.From(
            Entities(
                "{\n\"classname\" \"env_soundscape\"\n\"targetname\" \"master\"\n\"soundscape\" \"test.first\"\n" +
                "\"origin\" \"0 0 0\"\n\"position0\" \"hum\"\n}\n" +
                "{\n\"classname\" \"env_soundscape_proxy\"\n\"MainSoundscapeName\" \"master\"\n\"origin\" \"5 6 7\"\n" +
                "\"position1\" \"own\"\n}\n" +
                "{\n\"classname\" \"info_target\"\n\"targetname\" \"hum\"\n\"origin\" \"10 20 30\"\n}\n" +
                "{\n\"classname\" \"info_target\"\n\"targetname\" \"own\"\n\"origin\" \"40 50 60\"\n}\n"),
            Catalog).Placements;

        placed.Count.ShouldBe(2);
        placed[1].Positions[0].ShouldBe((10f, 20f, 30f), "the master's position0");
        placed[1].Positions[1].ShouldBeNull("the proxy's own position1 is overwritten by the master's empty one");
    }

    /// <remarks>
    /// <c>gEntList.FindEntityByName( NULL, m_MainSoundscapeName )</c> then <c>dynamic_cast&lt; CEnvSoundscape* &gt;</c>
    /// (<c>soundscape.cpp:42-45</c>): the FIRST entity of that name, of any class. Two soundscapes sharing a name give
    /// the first's; a name whose first holder is not a soundscape leaves the proxy with none, and it is skipped.
    /// </remarks>
    [Test]
    public void From_AProxyOfASharedName_TakesTheFirstEntityOfThatName()
    {
        IReadOnlyList<SoundscapePlacement> placed = SoundscapePlacements.From(
            Entities(
                "{\n\"classname\" \"env_soundscape\"\n\"targetname\" \"master\"\n\"soundscape\" \"test.second\"\n\"origin\" \"0 0 0\"\n}\n" +
                "{\n\"classname\" \"env_soundscape\"\n\"targetname\" \"master\"\n\"soundscape\" \"test.first\"\n\"origin\" \"0 0 0\"\n}\n" +
                "{\n\"classname\" \"info_target\"\n\"targetname\" \"relay\"\n\"origin\" \"0 0 0\"\n}\n" +
                "{\n\"classname\" \"env_soundscape\"\n\"targetname\" \"relay\"\n\"soundscape\" \"test.first\"\n\"origin\" \"0 0 0\"\n}\n" +
                "{\n\"classname\" \"env_soundscape_proxy\"\n\"MainSoundscapeName\" \"master\"\n\"origin\" \"5 6 7\"\n}\n" +
                "{\n\"classname\" \"env_soundscape_proxy\"\n\"MainSoundscapeName\" \"relay\"\n\"origin\" \"8 9 10\"\n}\n"),
            Catalog).Placements;

        placed.Count.ShouldBe(4, "three soundscapes and one proxy; the relay's proxy found an info_target first");
        placed[3].X.ShouldBe(5f);
        placed[3].Name.ShouldBe("test.second", "the first entity named master");
    }

    /// <remarks>
    /// <c>DEFINE_KEYFIELD( m_bDisabled, FIELD_BOOLEAN, "StartDisabled" )</c> (<c>soundscape.cpp:91</c>), parsed as
    /// <c>atoi( szValue ) != 0</c> (<c>saverestore_gamedll.cpp:62</c>) — so "1" and " 2" disable and "0" and a word do
    /// not. A proxy inherits the keyfield.
    /// </remarks>
    [Test]
    public void From_StartDisabled_IsReadAsAtoiNotZero()
    {
        IReadOnlyList<SoundscapePlacement> placed = SoundscapePlacements.From(
            Entities(
                Soundscape("0 0 0", "\"StartDisabled\" \"1\"") +
                Soundscape("0 0 0", "\"StartDisabled\" \" 2\"") +
                Soundscape("0 0 0", "\"StartDisabled\" \"0\"") +
                Soundscape("0 0 0", "\"StartDisabled\" \"yes\"") +
                Soundscape("0 0 0", string.Empty) +
                "{\n\"classname\" \"env_soundscape_proxy\"\n\"MainSoundscapeName\" \"master\"\n\"origin\" \"0 0 0\"\n" +
                "\"StartDisabled\" \"1\"\n}\n"),
            Catalog).Placements;

        placed.Count.ShouldBe(6);
        placed[0].Enabled.ShouldBeFalse();
        placed[1].Enabled.ShouldBeFalse();
        placed[2].Enabled.ShouldBeTrue();
        placed[3].Enabled.ShouldBeTrue("atoi of a word is 0");
        placed[4].Enabled.ShouldBeTrue("m_bDisabled = false in the constructor");
        placed[5].Enabled.ShouldBeFalse("the proxy's own keyfield");
    }

    /// <remarks>
    /// <c>UpdateForPlayer</c> returns at once when <c>!IsEnabled()</c> (<c>soundscape.cpp:247-256</c>), so a disabled
    /// soundscape never writes the player's audio params however near it is. The control is the same pair enabled.
    /// </remarks>
    [Test]
    public void Choose_ADisabledSoundscape_NeverContends()
    {
        string lump = Soundscape("100 0 0", "\"StartDisabled\" \"1\"") + Soundscape("300 0 0", string.Empty);

        SoundscapePlacements.From(Entities(lump), Catalog).Choose(0f, 0f, 0f, Clear).ShouldNotBeNull().X.ShouldBe(300f);

        SoundscapePlacements.From(Entities(lump.Replace("\"1\"", "\"0\"", System.StringComparison.Ordinal)), Catalog)
            .Choose(0f, 0f, 0f, Clear).ShouldNotBeNull().X.ShouldBe(100f, "the control: enabled, the nearer one wins");
    }

    /// <remarks>
    /// The held soundscape disabled: <c>if ( update.pCurrentSoundscape == this ) { pCurrentSoundscape = NULL;
    /// currentDistance = 0; bInRange = false; }</c> (<c>soundscape.cpp:249-254</c>) — so ANY contender in range and in
    /// sight takes over, however far, because nothing is in range to beat. The player's params are not rewritten, so
    /// with no contender the held one stays.
    /// </remarks>
    [Test]
    public void Choose_AHeldSoundscapeThatIsDisabled_YieldsToAnyContenderInRange()
    {
        string lump = Soundscape("100 0 0", "\"StartDisabled\" \"1\"") + Soundscape("900 0 0", string.Empty);

        SoundscapePlacements placements = SoundscapePlacements.From(Entities(lump), Catalog);
        SoundscapePlacement held = placements.Placements[0];

        placements.Choose(0f, 0f, 0f, Clear, held).ShouldNotBeNull().X
            .ShouldBe(900f, "the held one is disabled, so nothing is in range");
        placements.Choose(0f, 0f, 0f, (from, _) => from.X < 500f, held).ShouldNotBeNull().X
            .ShouldBe(100f, "with no contender in sight the held soundscape stays: nothing rewrote the params");

        SoundscapePlacements enabled = SoundscapePlacements.From(
            Entities(lump.Replace("\"1\"", "\"0\"", System.StringComparison.Ordinal)), Catalog);

        enabled.Choose(0f, 0f, 0f, Clear, enabled.Placements[0]).ShouldNotBeNull().X
            .ShouldBe(100f, "the control: enabled and nearer, the held one keeps the contender out");
    }

    /// <remarks>
    /// **The output-level check, on shipped maps and the shipped scripts**: every `env_soundscape` that sets position
    /// keys is entered, and each loop that sounds must sound at the entity its OWN slot names, read straight from the lump
    /// here rather than through the placement. Both maps leave gaps — ctf_well sets 1, 2 and 4, mvm_mannworks 1 and 2 —
    /// and compacted, a loop at `position 1` played at `position2`'s target and the last one was suppressed.
    ///
    /// Not koth_lazarus, the census's other example: its soundscapes live in the map's own pakfile script, which the
    /// viewer does not load at all (B465), so every placement there is index -1.
    /// </remarks>
    /// <param name="map">The installed map.</param>
    [TestCase("ctf_well")]
    [TestCase("mvm_mannworks")]
    public void MoveTo_AShippedMapsPositionedSoundscapes_PlayEachLoopAtTheTargetItsSlotNames(string map)
    {
        string bsp = Tf2DemoSalvage.SdkReference.GameInstall.RequireFile($"maps/{map}.bsp");
        SoundscapeCatalog catalog = SoundscapeCatalog.Load(
            Tf2DemoSalvage.Content.Assets.GameArchives.Open(Tf2DemoSalvage.SdkReference.GameInstall.Require()).Read,
            map);
        IReadOnlyList<BspEntity> entities = BspEntities.ReadFrom(System.IO.File.ReadAllBytes(bsp));
        SoundscapePlacements placements = SoundscapePlacements.From(entities, catalog);

        int heardAtTheirSlots = 0;
        int gapped = 0;

        foreach (BspEntity entity in entities)
        {
            if (!entity.ClassName.Equals("env_soundscape", System.StringComparison.OrdinalIgnoreCase) ||
                Origin(entity) is not { } at)
            {
                continue;
            }

            int[] slots = [.. Enumerable.Range(0, 8).Where(slot => Named(entity, $"position{slot}") is not null)];

            if (slots.Length == 0)
            {
                continue;
            }

            if (placements.Placements.FirstOrDefault(placement => (placement.X, placement.Y, placement.Z) == at) is not
                    { Name: { Length: > 0 } } placement ||
                catalog.At(placement.Index) is not { } script)
            {
                TestContext.Out.WriteLine($"gapped {Named(entity, "soundscape")} at {at}: no placement or no script");
                continue;
            }

            SoundscapeMixer mixer = new();
            mixer.MoveTo(placement, script);

            // **The wave AT the place, not the places alone**: compacted, ctf_well's `Well.DeepInside` still sounds at the
            // same three targets — with machine_hum where computer_tape belongs, and so on down the list.
            List<(string Wave, (float X, float Y, float Z) At)> heard =
            [
                .. mixer.Advance(0f)
                    .Where(voice => voice.Position is not null)
                    .Select(voice => (voice.Wave, voice.Position!.Value)),
            ];

            List<(string Wave, (float X, float Y, float Z) At)> expected = [];

            foreach (SoundscapeSound loop in script.Looping)
            {
                if (loop.Position is { } slot and >= 0 and < 8 &&
                    loop.Volume != default &&
                    Named(entity, $"position{slot}") is { } target &&
                    entities.FirstOrDefault(candidate =>
                            Named(candidate, "targetname") is { } name &&
                            name.Equals(target, System.StringComparison.OrdinalIgnoreCase) &&
                            Origin(candidate) is not null) is { } found)
                {
                    expected.Add((loop.Wave, Origin(found)!.Value));
                }
            }

            TestContext.Out.WriteLine(
                $"{script.Name} slots {string.Join(",", slots)}: {expected.Count} positioned loops expected, {heard.Count} heard");
            heard.OrderBy(point => point).ShouldBe(expected.OrderBy(point => point));
            heardAtTheirSlots += expected.Count;

            gapped += expected.Count > 0 && slots[^1] != slots.Length - 1 ? 1 : 0;
        }

        heardAtTheirSlots.ShouldBeGreaterThan(0, "the control: a soundscape here must place a loop");
        gapped.ShouldBeGreaterThan(0, "and one of them must have the gap this exists to check");
    }

    private static string? Named(BspEntity entity, string key) =>
        entity.TryGetValue(key, out string value) && value.Length > 0 ? value : null;

    private static (float X, float Y, float Z)? Origin(BspEntity entity)
    {
        if (Named(entity, "origin") is not { } text)
        {
            return null;
        }

        float[] parts = [.. text.Split(' ', System.StringSplitOptions.RemoveEmptyEntries)
            .Select(part => float.Parse(part, System.Globalization.CultureInfo.InvariantCulture))];

        return (parts[0], parts[1], parts[2]);
    }

    private static string Soundscape(string origin, string extra) =>
        "{\n\"classname\" \"env_soundscape\"\n\"targetname\" \"master\"\n\"soundscape\" \"test.first\"\n" +
        $"\"origin\" \"{origin}\"\n{extra}\n}}\n";
}
