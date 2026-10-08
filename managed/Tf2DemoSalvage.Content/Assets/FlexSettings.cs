using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One weight of a flex setting, <c>flexweight_t</c> (<c>studio.h:2863</c>), with its key resolved to a name.</summary>
/// <param name="Controller">The controller name — <c>pLocalName( key )</c>.</param>
/// <param name="Weight">The value it pulls the controller toward, in the controller's own range.</param>
/// <param name="Influence">How hard, before the event's intensity scales it.</param>
public readonly record struct FlexSettingWeight(string Controller, float Weight, float Influence);

/// <summary>
/// An expression file, <c>expressions/*.vfe</c> — <c>flexsettinghdr_t</c> (<c>studio.h:2908</c>), B513.
/// </summary>
/// <remarks>
/// **What a TF2 face is made of.** Every taunt and voice scene animates a player's face through <c>EXPRESSION</c>
/// events naming one of these files and a setting in it; <c>C_BaseFlex::AddFlexSetting</c> blends the setting's
/// weights into the controllers (<c>c_baseflex.cpp:1846</c>).
/// </remarks>
public sealed class FlexSettings
{
    private const int SettingStride = 24;
    private const int WeightStride = 12;
    private const int MaximumCount = 1 << 16;

    private readonly Dictionary<string, IReadOnlyList<FlexSettingWeight>> _byName;
    private readonly List<IReadOnlyList<FlexSettingWeight>> _bySlot;
    private readonly int[] _indexes;

    private FlexSettings(
        Dictionary<string, IReadOnlyList<FlexSettingWeight>> byName, List<IReadOnlyList<FlexSettingWeight>> bySlot, int[] indexes)
    {
        _byName = byName;
        _bySlot = bySlot;
        _indexes = indexes;
    }

    /// <summary><c>pIndexedSetting( index )</c> (<c>studio.h:2927</c>) — how a phoneme code finds its viseme.</summary>
    /// <param name="index">The phoneme code.</param>
    /// <returns>The setting's weights, or null out of range or for a <c>-1</c> slot.</returns>
    public IReadOnlyList<FlexSettingWeight>? IndexedSetting(int index)
    {
        if (index < 0 || index >= _indexes.Length)
        {
            return null;
        }

        int slot = _indexes[index];

        return slot < 0 || slot >= _bySlot.Count ? null : _bySlot[slot];
    }

    /// <summary>How many settings the file declares.</summary>
    public int Count => _byName.Count;

    /// <summary>A setting's weights by name, compared without case as <c>V_stricmp</c> does; null when absent.</summary>
    /// <param name="name">The setting, an event's second parameter.</param>
    /// <returns>Its weights, or null — <c>AddFlexSetting</c> then returns having done nothing.</returns>
    public IReadOnlyList<FlexSettingWeight>? Setting(string name) =>
        _byName.TryGetValue(name, out IReadOnlyList<FlexSettingWeight>? weights) ? weights : null;

    /// <summary>Reads an expression file.</summary>
    /// <param name="file">Its bytes.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="InvalidDataException">A table runs past the file.</exception>
    /// <remarks>
    /// **The first setting of a name wins**, because <c>AddFlexSetting</c> stops at the first <c>V_stricmp</c> match.
    /// The setting's name and weights are relative to the setting; the key names are offsets from the header
    /// (<c>pLocalName</c>, <c>studio.h:2947</c>).
    /// </remarks>
    public static FlexSettings Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 108)
        {
            throw new InvalidDataException($"An expression file of {file.Length} bytes is too short for its header.");
        }

        int settings = Int(file, 76);
        int settingsAt = Int(file, 80);
        int keys = Int(file, 96);
        int keyNamesAt = Int(file, 100);

        Fits(file, settingsAt, settings, SettingStride);
        Fits(file, keyNamesAt, keys, 4);

        string[] names = new string[keys];

        for (int key = 0; key < keys; key++)
        {
            names[key] = StudioStrings.At(file, Int(file, keyNamesAt + (key * 4)));
        }

        Dictionary<string, IReadOnlyList<FlexSettingWeight>> byName = new(StringComparer.OrdinalIgnoreCase);
        List<IReadOnlyList<FlexSettingWeight>> bySlot = new(settings);

        int indexCount = Int(file, 88);
        int indexesAt = Int(file, 92);
        Fits(file, indexesAt, indexCount, 4);

        int[] indexes = new int[indexCount];

        for (int index = 0; index < indexCount; index++)
        {
            indexes[index] = Int(file, indexesAt + (index * 4));
        }

        for (int index = 0; index < settings; index++)
        {
            int at = settingsAt + (index * SettingStride);
            string name = StudioStrings.At(file, at + Int(file, at));
            int count = Int(file, at + 8);
            int weightsAt = at + Int(file, at + 20);

            Fits(file, weightsAt, count, WeightStride);

            List<FlexSettingWeight> weights = new(count);

            for (int weight = 0; weight < count; weight++)
            {
                int w = weightsAt + (weight * WeightStride);
                int key = Int(file, w);

                weights.Add(new FlexSettingWeight(
                    key >= 0 && key < names.Length ? names[key] : string.Empty,
                    BinaryPrimitives.ReadSingleLittleEndian(file[(w + 4)..]),
                    BinaryPrimitives.ReadSingleLittleEndian(file[(w + 8)..])));
            }

            byName.TryAdd(name, weights);
            bySlot.Add(weights);
        }

        return new FlexSettings(byName, bySlot, indexes);
    }

    private static int Int(ReadOnlySpan<byte> file, int at) => BinaryPrimitives.ReadInt32LittleEndian(file[at..]);

    private static void Fits(ReadOnlySpan<byte> file, int at, int count, int stride)
    {
        if (count < 0 || count > MaximumCount || (count > 0 && (at < 0 || (long)at + ((long)count * stride) > file.Length)))
        {
            throw new InvalidDataException($"An expression file puts {count} entries at {at} of {file.Length} bytes.");
        }
    }
}
