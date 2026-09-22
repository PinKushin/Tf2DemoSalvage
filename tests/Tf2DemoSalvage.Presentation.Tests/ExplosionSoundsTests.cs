using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>An explosion's sound, emitted as the client does it — a sound no demo carries (B415).</summary>
/// <remarks>
/// **`C_BaseEntity::EmitSound( filter, SOUND_FROM_WORLD, pszSound, &amp;vecOrigin )`**, then
/// `CSoundEmitterSystem::EmitSoundByHandle` (`SoundEmitterSystem.cpp:450-525`): the script entry's channel,
/// soundlevel, a volume and pitch drawn from its ranges and one of its waves, handed to the engine from entity 0 at the
/// blast's origin. No flags are set, so nothing overrides the script's own values.
/// </remarks>
public sealed class ExplosionSoundsTests
{
    [Test]
    public void For_ABlastWithAScriptedSound_IsASoundFromTheWorldAtTheBlast()
    {
        SceneSound sound = ExplosionSounds.For(
            [Blast(tick: 100, x: 1f, y: 2f, z: 3f)],
            static _ => "Test.Explode",
            Scripts(Entry("Test.Explode", channel: 1, level: 95, ")weapons/explode1.wav"))).ShouldHaveSingleItem();

        sound.ShouldBe(new SceneSound(
            Tick: 100,
            Name: ")weapons/explode1.wav",
            SoundNumber: ExplosionSounds.NotPrecached,
            EntityIndex: ExplosionSounds.FromWorld,
            Channel: 1,
            Volume: 1f,
            SoundLevel: 95,
            Pitch: 100,
            DelaySeconds: 0f,
            OriginX: 1f,
            OriginY: 2f,
            OriginZ: 3f));
    }

    /// <remarks>`GetParametersForSoundEx` fails for a name no script declares, and `EmitSoundByHandle` returns.</remarks>
    [Test]
    public void For_ASoundNoScriptNames_IsSilent()
    {
        ExplosionSounds.For([Blast()], static _ => "Nobody.Declares", Scripts(Entry("Test.Explode", 1, 95, "a.wav")))
            .ShouldBeEmpty();
    }

    /// <remarks>
    /// **An `rndwave` is a draw, and the draw must not change on a seek.** The engine draws from its global stream,
    /// whose state no demo records; each blast here is its own seeded stream, so the same blast always picks the
    /// same wave — and different blasts do not all pick the first.
    /// </remarks>
    [Test]
    public void For_AnRndwave_PicksOneOfItsWavesTheSameWayEveryTime()
    {
        SceneExplosion[] blasts = [.. Enumerable.Range(0, 64).Select(tick => Blast(tick: tick))];
        IReadOnlyDictionary<string, SoundScriptEntry> scripts =
            Scripts(Entry("Test.Explode", 1, 95, "a.wav", "b.wav", "c.wav"));

        IReadOnlyList<SceneSound> first = ExplosionSounds.For(blasts, static _ => "Test.Explode", scripts);
        IReadOnlyList<SceneSound> again = ExplosionSounds.For(blasts, static _ => "Test.Explode", scripts);

        first.Select(sound => sound.Name).ShouldBe(again.Select(sound => sound.Name));
        first.Select(sound => sound.Name).Distinct().Count().ShouldBe(3);
    }

    /// <remarks>`volume_t::Random()` is `RandomFloat( start, start + range )`; a ranged pitch is the same, truncated to int.</remarks>
    [Test]
    public void For_RangedVolumeAndPitch_DrawWithinThem()
    {
        SoundScriptEntry ranged = new(
            "Test.Explode", 1, new SoundRange(0.5f, 0.75f), new SoundRange(90f, 110f), 95, ["a.wav"]);

        IReadOnlyList<SceneSound> sounds = ExplosionSounds.For(
            [.. Enumerable.Range(0, 64).Select(tick => Blast(tick: tick))],
            static _ => "Test.Explode",
            Scripts(ranged));

        sounds.ShouldAllBe(sound => sound.Volume >= 0.5f && sound.Volume <= 0.75f);
        sounds.ShouldAllBe(sound => sound.Pitch >= 90 && sound.Pitch <= 110);
        sounds.Select(sound => sound.Pitch).Distinct().Count().ShouldBeGreaterThan(5);
    }

    [Test]
    public void Merged_TwoTickOrderedLists_AreOneTickOrderedListWithTheDemosFirstOnATie()
    {
        SceneSound[] demo = [Sound(10, "demo10"), Sound(20, "demo20"), Sound(30, "demo30")];
        SceneSound[] effects = [Sound(5, "blast5"), Sound(20, "blast20"), Sound(40, "blast40")];

        ExplosionSounds.Merged(demo, effects).Select(sound => sound.Name)
            .ShouldBe(["blast5", "demo10", "demo20", "blast20", "demo30", "blast40"]);
    }

    private static SceneExplosion Blast(int tick = 1, float x = 0f, float y = 0f, float z = 0f) =>
        new(tick, x, y, z, (0f, 0f, 1f), WeaponId: 22, Entity: SceneExplosion.NoEntity, SceneExplosion.NoCustomParticle);

    private static SoundScriptEntry Entry(string name, int channel, int level, params string[] waves) =>
        new(name, channel, new SoundRange(1f, 1f), new SoundRange(100f, 100f), level, waves);

    private static Dictionary<string, SoundScriptEntry> Scripts(params SoundScriptEntry[] entries) =>
        entries.ToDictionary(entry => entry.Name, StringComparer.OrdinalIgnoreCase);

    private static SceneSound Sound(int tick, string name) =>
        new(tick, name, -1, 0, 0, 1f, 75, 100, 0f, 0f, 0f, 0f);
}
