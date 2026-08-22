using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// The prefix characters a sound name may carry, against the ones this project strips.
/// </summary>
/// <remarks>
/// **Written before any sound is played, because the failure mode is silence.** A precached sound
/// name is not always a path: <c>public/soundchars.h</c> declares ten characters that may lead one,
/// and <c>PSkipSoundChars</c> skips them before the remainder is used to open a file. So
/// <c>)weapons/sniper_railgun_double_shot.wav</c> is the file
/// <c>weapons/sniper_railgun_double_shot.wav</c>, drawn spatialised.
///
/// A reader that concatenates the name onto <c>sound/</c> gets a path that does not exist, returns
/// null, and plays nothing — **indistinguishable from a sound that is not implemented yet**, on a
/// feature whose entire output is sound. Nothing would report it.
///
/// **Measured on the committed corpus before writing any of this** (`SoundCharProbe`): 34,436
/// precached names across ten demos, **1,971 of them — 5.7% — carrying a prefix**. Not a corner:
///
/// | char | meaning | count |
/// |---|---|---|
/// | <c>)</c> | spatialised stereo | 1,783 |
/// | <c>#</c> | bypasses DSP | 122 |
/// | <c>&gt;</c> | doppler-encoded stereo | 60 |
/// | <c>*</c> | streaming | 28 |
/// | <c>^</c> | distance variant | 3 |
///
/// **The character set is parsed out of the header rather than restated**, so a character Valve
/// adds cannot be silently missed — which is the same reason every other conformance test here
/// derives its values.
/// </remarks>
public sealed class SoundCharConformanceTests
{
    private const string SoundChars = "src/public/soundchars.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void SoundChars_EveryOneValveDeclares_IsRecognisedHere()
    {
        Dictionary<char, string> declared = Declared();

        // The control: a pattern that matched nothing would make the loop below vacuous, and the
        // header declares exactly ten.
        declared.Count.ShouldBe(10, "soundchars.h declares ten CHAR_ constants");

        foreach ((char character, string name) in declared)
        {
            SoundName.IsSoundChar(character).ShouldBeTrue(
                $"{name} is '{character}' in soundchars.h, and a name leading with it would be "
                + "opened as a path that does not exist — which plays silence and reports nothing");
        }

        // **And the negative half, which is what stops the predicate being `=> true`.** An ordinary
        // path character must NOT be treated as a prefix, or every name loses its first letters.
        foreach (char ordinary in "abzABZ019/_.-")
        {
            SoundName.IsSoundChar(ordinary).ShouldBeFalse(
                $"'{ordinary}' is part of a path, not a prefix");
        }
    }

    [Test]
    public void Parse_ANameWithNoPrefix_IsUnchanged()
    {
        SoundName plain = SoundName.Parse("weapons/shotgun_shoot.wav");

        plain.Path.ShouldBe("weapons/shotgun_shoot.wav");
        plain.Characters.ShouldBeEmpty();
    }

    [Test]
    public void Parse_TheCharactersValveDeclares_AreStrippedAndRetained()
    {
        // Retained, not merely stripped: `*` selects streaming, `#` bypasses the DSP chain and `)`
        // spatialises a stereo file. Dropping them loses the instruction and keeps only the path,
        // which is the half-fix that looks complete.
        foreach ((char character, string name) in Declared())
        {
            SoundName parsed = SoundName.Parse($"{character}ambient/lights/fluorescent1.wav");

            parsed.Path.ShouldBe(
                "ambient/lights/fluorescent1.wav", $"{name} must not survive into the path");

            parsed.Characters.ShouldBe([character], $"{name} must be retained, not discarded");
        }
    }

    [Test]
    public void Parse_TwoPrefixes_AreBothStripped()
    {
        // **Valve's comment says "as one of 1st 2 chars", so two is a real case** — and the
        // FUNCTION goes further: PSkipSoundChars loops `while (IsSoundChar(*pcht)) pcht++`, with no
        // limit of two. Transcribed from the function rather than the comment, because the function
        // is what runs (docs/memory/read-the-encoder-not-the-decoder.md).
        string skip = Sdk();

        skip.ShouldContain("PSkipSoundChars", Case.Sensitive);
        skip.ShouldContain("if (!IsSoundChar(*pcht))", Case.Sensitive);

        SoundName parsed = SoundName.Parse(")#music/hl1_song10.mp3");

        parsed.Path.ShouldBe("music/hl1_song10.mp3");
        parsed.Characters.ShouldBe([')', '#']);
    }

    [Test]
    public void Parse_APrefixCharacterInsideThePath_IsNotStripped()
    {
        // **The condition that separates "skip a prefix" from "strip these characters".** Only
        // LEADING characters are prefixes; the same byte later in the name is part of the path, and
        // a reader using Trim or Replace would corrupt it silently.
        SoundName parsed = SoundName.Parse("vo/announcer_am_lastmanalive01.mp3");

        parsed.Path.ShouldBe("vo/announcer_am_lastmanalive01.mp3");

        // A path that genuinely contains one, so this is not merely an absence.
        SoundName odd = SoundName.Parse("weapons/odd)name.wav");

        odd.Path.ShouldBe("weapons/odd)name.wav");
        odd.Characters.ShouldBeEmpty();
    }

    [Test]
    public void Parse_AnEmptyOrAllPrefixName_YieldsAnEmptyPathRatherThanThrowing()
    {
        // Hostile input: the string table is attacker-supplied in the sense that matters, since a
        // demo comes from a stranger. An empty path is a sound that cannot be opened, which is the
        // right answer; an exception here would take down the whole sound pass.
        SoundName.Parse(string.Empty).Path.ShouldBeEmpty();
        SoundName.Parse(")))").Path.ShouldBeEmpty();
        SoundName.Parse(")))").Characters.Count.ShouldBe(3);
    }

    /// <summary>Every <c>CHAR_</c> constant the header declares, character to name.</summary>
    private static Dictionary<char, string> Declared()
    {
        Dictionary<char, string> found = [];

        foreach (Match match in Regex.Matches(
            Sdk(),
            @"#define\s+(?<name>CHAR_\w+)\s+'(?<char>.)'",
            RegexOptions.None,
            TimeSpan.FromSeconds(5)))
        {
            found[match.Groups["char"].Value[0]] = match.Groups["name"].Value;
        }

        return found;
    }

    /// <summary>Reads soundchars.h, or fails loudly.</summary>
    private static string Sdk() =>
        SourceSdk.Text(SoundChars)
        ?? throw new InvalidOperationException($"{SoundChars} is missing from the SDK");
}
