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
    /// the first's; a name whose first holder is not a soundscape leaves the proxy with no master — and it is KEPT, at
    /// the index -1 its constructor gave it (<c>:105</c>), because the soundscape system listed it when it was
    /// constructed (<c>:108</c>) and nothing takes it out.
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

        placed.Count.ShouldBe(5, "three soundscapes and both proxies");
        placed[3].X.ShouldBe(5f);
        placed[3].Name.ShouldBe("test.second", "the first entity named master");
        placed[3].Index.ShouldBe(1);
        placed[4].X.ShouldBe(8f);
        placed[4].Index.ShouldBe(-1, "the relay's proxy found an info_target first, so it has no master");
    }

    /// <remarks>
    /// **A proxy's master can be any <c>CEnvSoundscape</c>** — the <c>dynamic_cast</c> at <c>soundscape.cpp:45</c> accepts
    /// <c>env_soundscape_triggerable</c> and <c>env_soundscape_proxy</c>, both derived from it (<c>soundscape.h:75,93</c>).
    /// A triggerable has its index from its own <c>Precache</c> at spawn, before any entity activates
    /// (<c>mapentities.cpp:257-308</c>), so a proxy of one copies it. Census, `soundscape-entities` probe: 64 installed
    /// proxies have a triggerable master — 39 on pl_venice — and played nothing.
    /// </remarks>
    [Test]
    public void From_AProxyOfATriggerable_TakesTheTriggerablesSoundscapeAndPositions()
    {
        IReadOnlyList<SoundscapePlacement> placed = SoundscapePlacements.From(
            Entities(
                "{\n\"classname\" \"env_soundscape_triggerable\"\n\"targetname\" \"trig\"\n\"soundscape\" \"test.second\"\n" +
                "\"origin\" \"0 0 0\"\n\"position0\" \"hum\"\n}\n" +
                "{\n\"classname\" \"env_soundscape_proxy\"\n\"MainSoundscapeName\" \"trig\"\n\"origin\" \"5 6 7\"\n}\n" +
                "{\n\"classname\" \"info_target\"\n\"targetname\" \"hum\"\n\"origin\" \"10 20 30\"\n}\n"),
            Catalog).Placements;

        // The triggerable itself is not placed: its own place in the radius contest is B483, filed open.
        SoundscapePlacement proxy = placed.ShouldHaveSingleItem();

        proxy.X.ShouldBe(5f);
        proxy.Index.ShouldBe(1, "test.second, the triggerable's");
        proxy.Name.ShouldBe("test.second");
        proxy.Positions[0].ShouldBe((10f, 20f, 30f), "the triggerable's position0");
    }

    /// <remarks>
    /// **A proxy of a proxy copies what its master holds WHEN IT ACTIVATES.** <c>Activate</c> runs in spawn-list order
    /// (<c>mapentities.cpp:299-308</c>); a proxy's own <c>Precache</c> is empty (<c>soundscape.h:86</c>), so a master
    /// proxy that has not activated yet still has index -1 and its OWN position keys, and that is what is copied. One that
    /// has activated hands on its master's. Census: 13 installed proxies of a proxy, 12 of them on ctf_applejack.
    ///
    /// *Interpolated:* the order. The spawn list is <c>qsort</c>ed on hierarchy depth and a short class priority list
    /// (<c>mapentities.cpp:107-127,199</c>), neither of which separates two unparented soundscapes, so their order is the
    /// sort's: lump order where <c>qsort</c> is stable — glibc's merge sort, on a Linux server — and not promised by MSVC's.
    /// </remarks>
    [Test]
    public void From_AProxyOfAProxy_CopiesWhatThatProxyHoldsWhenItActivates()
    {
        const string Master =
            "{\n\"classname\" \"env_soundscape\"\n\"targetname\" \"master\"\n\"soundscape\" \"test.second\"\n" +
            "\"origin\" \"0 0 0\"\n\"position0\" \"hum\"\n}\n";
        const string Relay =
            "{\n\"classname\" \"env_soundscape_proxy\"\n\"targetname\" \"relay\"\n\"MainSoundscapeName\" \"master\"\n" +
            "\"origin\" \"1 0 0\"\n\"position1\" \"own\"\n}\n";
        const string Second =
            "{\n\"classname\" \"env_soundscape_proxy\"\n\"MainSoundscapeName\" \"relay\"\n\"origin\" \"2 0 0\"\n}\n";
        const string Targets =
            "{\n\"classname\" \"info_target\"\n\"targetname\" \"hum\"\n\"origin\" \"10 20 30\"\n}\n" +
            "{\n\"classname\" \"info_target\"\n\"targetname\" \"own\"\n\"origin\" \"40 50 60\"\n}\n";

        IReadOnlyList<SoundscapePlacement> inOrder = SoundscapePlacements.From(Entities(Master + Relay + Second + Targets), Catalog)
            .Placements;

        inOrder.Count.ShouldBe(3);

        SoundscapePlacement after = inOrder[2];

        after.X.ShouldBe(2f);
        after.Index.ShouldBe(1, "the relay activated first and already held test.second");
        after.Positions[0].ShouldBe((10f, 20f, 30f), "and the master's position0");
        after.Positions[1].ShouldBeNull();

        IReadOnlyList<SoundscapePlacement> reversed = SoundscapePlacements.From(Entities(Master + Second + Relay + Targets), Catalog)
            .Placements;

        reversed.Count.ShouldBe(3);

        SoundscapePlacement before = reversed[1];

        before.X.ShouldBe(2f);
        before.Index.ShouldBe(-1, "the relay had not activated, so its index was still the constructor's -1");
        before.Positions[0].ShouldBeNull();
        before.Positions[1].ShouldBe((40f, 50f, 60f), "the relay's OWN position1, which it had not yet replaced");
    }

    /// <remarks>
    /// <c>Warning( "env_soundscape_proxy can't find target soundscape: '%s'\n", ... )</c> and nothing else
    /// (<c>soundscape.cpp:56-59</c>): the proxy stays in the system's list with index -1 and contends like any other. When
    /// it wins, the client is told an entity and no soundscape, and starts nothing (<c>c_soundscape.cpp:562-575</c>) — so
    /// whatever was playing carries on. Census: 27 installed proxies name no master.
    /// </remarks>
    [Test]
    public void Choose_AProxyWithNoMaster_ContendsAndCarriesNoSoundscape()
    {
        SoundscapePlacements placements = SoundscapePlacements.From(
            Entities(
                Soundscape("300 0 0", string.Empty) +
                "{\n\"classname\" \"env_soundscape_proxy\"\n\"MainSoundscapeName\" \"nobody\"\n\"origin\" \"100 0 0\"\n}\n"),
            Catalog);

        placements.Placements.Count.ShouldBe(2);

        SoundscapePlacement chosen = placements.Choose(0f, 0f, 0f, Clear).ShouldNotBeNull();

        chosen.X.ShouldBe(100f, "the masterless proxy is nearer, and it is in the contest");
        chosen.Index.ShouldBe(-1);
    }

    /// <remarks>
    /// <c>CBaseEntity *pEntity = gEntList.FindEntityByName( NULL, m_positionNames[i], this, this ); if ( pEntity ) {
    /// ... audio.localSound.Set( i, pEntity-&gt;GetAbsOrigin() ); }</c> (<c>soundscape.cpp:222-227</c>): the FIRST entity
    /// of the name, and wherever it stands — an entity that declares no <c>origin</c> stands at the world origin. Census:
    /// no installed map names such a target, so this is the rule a third-party map meets.
    /// </remarks>
    [Test]
    public void From_APositionTargetWithNoOrigin_IsAtTheWorldOriginAndTheFirstOfItsNameWins()
    {
        SoundscapePlacement placed = SoundscapePlacements.From(
            Entities(
                Soundscape("0 0 0", "\"position0\" \"bare\"\n\"position1\" \"twice\"") +
                "{\n\"classname\" \"info_target\"\n\"targetname\" \"bare\"\n}\n" +
                "{\n\"classname\" \"info_target\"\n\"targetname\" \"twice\"\n}\n" +
                "{\n\"classname\" \"info_target\"\n\"targetname\" \"twice\"\n\"origin\" \"7 8 9\"\n}\n"),
            Catalog).Placements.ShouldHaveSingleItem();

        placed.Positions[0].ShouldBe((0f, 0f, 0f), "a named entity with no origin is at the world origin, not absent");
        placed.Positions[1].ShouldBe((0f, 0f, 0f), "the FIRST entity of the name, not the first with an origin");
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
                // The FIRST entity of the name, at the world origin when it declares none (`soundscape.cpp:222-226`).
                if (loop.Position is { } slot and >= 0 and < 8 &&
                    loop.Volume != default &&
                    Named(entity, $"position{slot}") is { } target &&
                    entities.FirstOrDefault(candidate =>
                            Named(candidate, "targetname") is { } name &&
                            name.Equals(target, System.StringComparison.OrdinalIgnoreCase)) is { } found)
                {
                    expected.Add((loop.Wave, Origin(found) ?? (0f, 0f, 0f)));
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

    /// <remarks>
    /// **The output-level check for a triggerable master, on the map that has the most**: pl_venice's 39 proxies of an
    /// `env_soundscape_triggerable` were dropped, so a listener near one heard whatever the last ordinary soundscape left.
    /// Each must now carry its triggerable's soundscape — read straight from the lump here, not through the placement.
    /// </remarks>
    [Test]
    public void From_PlVenicesProxiesOfATriggerable_CarryTheTriggerablesSoundscape()
    {
        string bsp = Tf2DemoSalvage.SdkReference.GameInstall.RequireFile("maps/pl_venice.bsp");
        SoundscapeCatalog catalog = SoundscapeCatalog.Load(
            Tf2DemoSalvage.Content.Assets.GameArchives.Open(Tf2DemoSalvage.SdkReference.GameInstall.Require()).Read,
            "pl_venice");
        IReadOnlyList<BspEntity> entities = BspEntities.ReadFrom(System.IO.File.ReadAllBytes(bsp));
        SoundscapePlacements placements = SoundscapePlacements.From(entities, catalog);

        int checkedProxies = 0;

        foreach (BspEntity entity in entities)
        {
            if (!entity.ClassName.Equals("env_soundscape_proxy", System.StringComparison.OrdinalIgnoreCase) ||
                Named(entity, "MainSoundscapeName") is not { } master ||
                entities.FirstOrDefault(candidate =>
                        Named(candidate, "targetname") is { } name &&
                        name.Equals(master, System.StringComparison.OrdinalIgnoreCase)) is not { } main ||
                !main.ClassName.Equals("env_soundscape_triggerable", System.StringComparison.OrdinalIgnoreCase) ||
                Named(main, "soundscape") is not { } soundscape ||
                Origin(entity) is not { } at)
            {
                continue;
            }

            SoundscapePlacement placed = placements.Placements
                .Single(placement => (placement.X, placement.Y, placement.Z) == at);

            placed.Name.ShouldBe(soundscape, $"the proxy at {at} names {master}");

            checkedProxies++;
        }

        checkedProxies.ShouldBe(39, "the census: 39 proxies of a triggerable on pl_venice");
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
