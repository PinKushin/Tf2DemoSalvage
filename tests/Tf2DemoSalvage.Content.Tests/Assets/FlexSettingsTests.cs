using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>A hand-built <c>.vfe</c> with two settings over two keys (D38, B513).</summary>
public sealed class FlexSettingsTests
{
    [Test]
    public void Read_ASetting_ResolvesEachWeightsKeyToItsControllerName()
    {
        FlexSettings file = FlexSettings.Read(Vfe());

        file.Count.ShouldBe(2);
        file.Setting("happyBig").ShouldBe([new FlexSettingWeight("smile", 0.75f, 1f), new FlexSettingWeight("lid", -0.5f, 0.5f)]);
        file.Setting("mad").ShouldBe([new FlexSettingWeight("lid", 1f, 1f)]);
    }

    [Test]
    public void Setting_TheName_IsComparedWithoutCase() => FlexSettings.Read(Vfe()).Setting("HAPPYBIG").ShouldNotBeNull();

    [Test]
    public void Setting_AnAbsentName_IsNull() => FlexSettings.Read(Vfe()).Setting("scared").ShouldBeNull();

    [Test]
    public void Read_WeightsPastTheEnd_Fail()
    {
        byte[] file = Vfe();
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(200 + 8), 5_000);

        Should.Throw<System.IO.InvalidDataException>(() => FlexSettings.Read(file));
    }

    /// <summary>Header at 0, settings at 200 (24 bytes each), weights at 300, key names at 400, strings from 500.</summary>
    private static byte[] Vfe()
    {
        byte[] file = new byte[1024];
        Span<byte> s = file;
        int strings = 500;

        Int(s, 76, 2);
        Int(s, 80, 200);
        Int(s, 96, 2);
        Int(s, 100, 400);
        Int(s, 400, String(s, ref strings, "smile"));
        Int(s, 404, String(s, ref strings, "lid"));

        List<(string Name, (int Key, float Weight, float Influence)[] Weights)> settings =
        [
            ("happyBig", [(0, 0.75f, 1f), (1, -0.5f, 0.5f)]),
            ("mad", [(1, 1f, 1f)]),
        ];

        int weightsAt = 300;

        for (int index = 0; index < settings.Count; index++)
        {
            int at = 200 + (index * 24);
            Int(s, at, String(s, ref strings, settings[index].Name) - at);
            Int(s, at + 8, settings[index].Weights.Length);
            Int(s, at + 20, weightsAt - at);

            foreach ((int key, float weight, float influence) in settings[index].Weights)
            {
                Int(s, weightsAt, key);
                BinaryPrimitives.WriteSingleLittleEndian(s[(weightsAt + 4)..], weight);
                BinaryPrimitives.WriteSingleLittleEndian(s[(weightsAt + 8)..], influence);
                weightsAt += 12;
            }
        }

        return file;
    }

    private static void Int(Span<byte> s, int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(s[at..], value);

    private static int String(Span<byte> s, ref int at, string text)
    {
        int start = at;
        Encoding.UTF8.GetBytes(text).CopyTo(s[at..]);
        at += Encoding.UTF8.GetByteCount(text) + 1;
        return start;
    }
}
