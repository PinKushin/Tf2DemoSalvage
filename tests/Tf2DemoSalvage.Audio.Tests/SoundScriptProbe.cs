using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>What TF2's shipped soundscripts actually contain, before a reader is written for them.</summary>
[Explicit("Scans the game's script files; run deliberately.")]
public sealed class SoundScriptProbe
{
    [Test]
    public void SoundLevelNone_WhatDeclaresIt_IsListedToSettleB143()
    {
        // **B143 step 1, and it needs no decompiler.** Valve's macros disagree at zero:
        // ATTN_TO_SNDLVL(0) is 0 and recipientfilter leaves every recipient in at attenuation zero,
        // but SNDLVL_TO_ATTN(0) returns 4.0 — near maximum attenuation. SoundGain took SNDLVL_NONE
        // to mean unattenuated, and what actually declares it is evidence either way: announcer
        // lines, UI and music behave globally, while footsteps and cloth would not.
        if (GameInstall.Vpk("tf2_misc") is not { } directory)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        VpkArchive archive = VpkArchive.Open(directory);
        SoundScriptCatalog catalog = SoundScriptCatalog.Load(
            path => archive.ReadFile(path.ToUpperInvariant()));

        List<SoundScriptEntry> silent =
            [.. catalog.Entries.Values.Where(entry => entry.SoundLevel == 0)];

        TestContext.Out.WriteLine($"SNDLVL_NONE entries: {silent.Count}");

        // Grouped by the first segment of the wave path, which is what says whether these are
        // global sounds or local ones — `ui/`, `vo/` and `music/` behave one way, `player/` and
        // `weapons/` the other.
        foreach (IGrouping<string, SoundScriptEntry> group in silent
            .GroupBy(entry => entry.Waves[0].Split('/')[0].ToUpperInvariant())
            .OrderByDescending(group => group.Count()))
        {
            TestContext.Out.WriteLine($"  {group.Count(),5}  {group.Key}");
        }

        TestContext.Out.WriteLine("--- a sample of the names ---");

        foreach (SoundScriptEntry entry in silent.Take(15))
        {
            TestContext.Out.WriteLine($"  {entry.Name}  ->  {entry.Waves[0]}");
        }

        silent.Count.ShouldBeGreaterThan(0, "nothing declares SNDLVL_NONE, so this measured nothing");
    }

    [Test]
    public void SoundScripts_TheirKeysAndShape_AreCounted()
    {
        if (GameInstall.Vpk("tf2_misc") is not { } directory)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        VpkArchive archive = VpkArchive.Open(directory);

        List<string> scripts = [.. archive.Paths
            .Where(p => p.Contains("GAME_SOUNDS", StringComparison.OrdinalIgnoreCase)
                     || p.Contains("game_sounds", StringComparison.OrdinalIgnoreCase))];

        TestContext.Out.WriteLine($"soundscript files: {scripts.Count}");

        foreach (string path in scripts.Take(12))
        {
            TestContext.Out.WriteLine($"  {path}");
        }

        // The manifest names which of them the game loads.
        string? manifest = scripts.FirstOrDefault(
            p => p.Contains("MANIFEST", StringComparison.OrdinalIgnoreCase));

        if (manifest is not null && archive.ReadFile(manifest) is { } bytes)
        {
            TestContext.Out.WriteLine($"--- {manifest} ---");
            TestContext.Out.WriteLine(
                System.Text.Encoding.UTF8.GetString(bytes)[..Math.Min(3000, bytes.Length)]);
        }

        // And one real script, so the syntax is read rather than assumed.
        string? weapons = scripts.FirstOrDefault(
            p => p.Contains("WEAPON", StringComparison.OrdinalIgnoreCase));

        if (weapons is not null && archive.ReadFile(weapons) is { } weaponBytes)
        {
            TestContext.Out.WriteLine($"--- {weapons}, past the comment header ---");

            string[] lines = System.Text.Encoding.UTF8.GetString(weaponBytes)
                .Split('\n', StringSplitOptions.None);

            foreach (string line in lines.Skip(55).Take(46))
            {
                TestContext.Out.WriteLine(line.TrimEnd());
            }
        }

        scripts.Count.ShouldBeGreaterThan(0, "no soundscript was found, so nothing was measured");
    }

    [Test]
    public void Waves_TheOnesAMapLoopsForever_AreCheckedForALoopMarker()
    {
        // **Whether a wave loops is a property of the FILE, not of how it was started.** Source
        // reads a `cue ` chunk and the engine repeats from that marker; nothing on the wire says
        // "loop". So a viewer that fails to find the marker turns every piece of map ambience into
        // a one-shot that plays once at the demo's first tick and is never heard again — which is
        // indistinguishable from never starting it, and is what the owner reports as "the pc hum
        // isnt playing at all".
        //
        // cp_process's six `ambient_generic` entities all play `Ambient.MachineHum`, started once
        // at tick 4 and again at each round restart. If this reports `loops False`, that is the
        // whole defect.
        if (GameInstall.Vpk("tf2_sound_misc") is not { } directory)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        VpkArchive archive = VpkArchive.Open(directory);

        foreach (string wave in new[]
        {
            "sound/ambient/machine_hum.wav",
            "sound/ambient/machine_hum2.wav",
            "sound/ambient/indoors.wav",
            "sound/ambient/atmosphere/hole_hit1.wav",
        })
        {
            byte[]? bytes = archive.ReadFile(wave.ToUpperInvariant());

            if (bytes is null)
            {
                TestContext.Out.WriteLine($"  {wave}: NOT FOUND");
                continue;
            }

            if (RiffWave.Read(bytes) is not { } parsed)
            {
                TestContext.Out.WriteLine($"  {wave}: {bytes.Length} bytes, DID NOT PARSE");
                continue;
            }

            TestContext.Out.WriteLine(
                $"  {wave}: {bytes.Length} bytes, format {parsed.Format}, " +
                $"{parsed.Channels}ch {parsed.SampleRate}Hz, " +
                $"loops {parsed.Loops} at {parsed.LoopStart}");
        }
    }
}
