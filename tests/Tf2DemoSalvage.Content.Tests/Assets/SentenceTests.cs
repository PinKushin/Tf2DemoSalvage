using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Lip-sync data out of a hand-built sound cache, and a flex track's value, with answers the test put there (D38, B513).
/// </summary>
public sealed class SentenceTests
{
    [Test]
    public void Read_ACacheWithOneSentenceAndOneWithout_KeysTheSentenceByItsPath()
    {
        Dictionary<string, Sentence> cache = SoundCacheFile.Read(Cache());

        Sentence sentence = cache.ShouldHaveSingleItem().Value;
        cache.ShouldContainKey("sound\\vo\\scout_test01.wav");
        cache.ShouldContainKey(SoundCacheFile.Normalise("sound/vo/SCOUT_test01.wav"), "paths compare without case");

        sentence.Phonemes.ShouldBe([new SentencePhoneme(7, 0.1f, 0.3f), new SentencePhoneme(9, 0.3f, 0.5f)]);
        sentence.Emphasis.ShouldBe([(0.2f, 0.75f)]);
        sentence.SampleRate.ShouldBe(22050);
        sentence.Length.ShouldBe(2f);
    }

    [Test]
    public void Read_ARecordRunningPastTheFile_Fails()
    {
        byte[] file = Cache();
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(16), 100_000);

        Should.Throw<InvalidDataException>(() => SoundCacheFile.Read(file));
    }

    [Test]
    public void Intensity_NoEmphasis_IsNeutral() =>
        new Sentence([], [], 22050, 22050).Intensity(0.5f, 1f).ShouldBe(0.5f);

    [Test]
    public void Intensity_AtAnEmphasisSample_IsItsValue() =>
        new Sentence([], [(0.5f, 0.9f)], 22050, 22050).Intensity(0.5f, 1f).ShouldBe(0.9f, 1e-6f);

    [Test]
    public void TrackIntensity_BeforeTheEvent_IsTheTracksZeroInItsOwnRange()
    {
        // GetIntensityInternal: the zero value is (0 - min) / (max - min), 0.5 for −1..1, rescaled back to 0.
        SceneFlexTrack track = new("lid", true, false, -1f, 1f, [new(0f, 1f, 0)], []);

        ChoreoCurve.TrackIntensity(track, 1f, 2f, 0.5f, 0).ShouldBe(0f);
    }

    [Test]
    public void TrackIntensity_AComboWithNoBalance_GivesBothSidesTheMagnitude()
    {
        // No balance samples: the balance is its zero, 0.5, which scales neither side.
        SceneFlexTrack track = new("smile", true, true, 0f, 1f, [new(0f, 0.4f, 0), new(0.5f, 0.4f, 0), new(1f, 0.4f, 0)], []);

        float right = ChoreoCurve.TrackIntensity(track, 0f, 1f, 0.5f, 0);

        right.ShouldBe(0.4f, 1e-6f);
        ChoreoCurve.TrackIntensity(track, 0f, 1f, 0.5f, 1).ShouldBe(right);
    }

    /// <summary>Header (2, 5, crc, 2), then a version-4 sentence entry and an entry with no sentence.</summary>
    private static byte[] Cache()
    {
        using MemoryStream stream = new();
        using BinaryWriter w = new(stream);

        w.Write(2);
        w.Write(5);
        w.Write(0x1234);
        w.Write(2);

        Record(w, "sound\\vo\\scout_test01.wav", sentence: true);
        Record(w, "sound\\vo\\scout_silent.wav", sentence: false);

        return stream.ToArray();
    }

    private static void Record(BinaryWriter w, string name, bool sentence)
    {
        using MemoryStream body = new();
        using BinaryWriter b = new(body);

        b.Write(Encoding.ASCII.GetBytes(name + "\0"));
        b.Write(0);
        b.Write(22050 << 14);
        b.Write((byte)(sentence ? 1 : 0));
        b.Write(0);
        b.Write(0);
        b.Write(0);
        b.Write(44100);

        if (sentence)
        {
            b.Write((byte)4);
            b.Write((byte)0);
            b.Write((byte)0);
            b.Write((byte)0);
            b.Write(2);
            b.Write(7);
            b.Write(0.1f);
            b.Write(0.3f);
            b.Write(9);
            b.Write(0.3f);
            b.Write(0.5f);
            b.Write(1);
            b.Write(0.2f);
            b.Write(0.75f);
            b.Write(0);
        }

        // A trailing blob the record length steps over.
        b.Write(new byte[5]);

        w.Write((int)body.Length);
        w.Write(body.ToArray());
    }
}
