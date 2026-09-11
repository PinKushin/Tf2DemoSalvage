using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Tf2DemoSalvage.Scene;

/// <summary>One sequence of an old install's model: its label and its activity.</summary>
/// <param name="Label">The sequence's name, which is what it is matched to today by.</param>
/// <param name="Activity">Its activity, or empty — the fallback key when the label is gone today.</param>
public readonly record struct EraSequence(string Label, string Activity);

/// <summary>
/// The sequence lists older TF2 installs shipped, for the models whose lists differ from today's
/// (B380, D160).
/// </summary>
/// <remarks>
/// **A networked <c>m_nSequence</c> is an index into the model the recording client loaded**, and
/// Valve reordered some models after 2008 — the sticky launcher gained a <c>ref</c> at index 0, so a
/// 2008 demo's <c>draw</c> plays as today's <c>fire</c>. The engine resolves the index against whatever
/// model it has (`studio_virtualmodel.cpp:142`), so the only faithful source is the old list, which is
/// what this carries: labels and activities, never Valve's meshes or animation data (D160).
///
/// **Which era a demo belongs to comes from its network protocol**, by the table's own
/// <c>protocol</c> lines. A protocol with no line — 24, which runs from 2013 to today — has no table
/// yet, and its demos are read against today's files as before.
/// </remarks>
public sealed class SequenceEras
{
    private const string ResourceName = "Tf2DemoSalvage.Scene.sequence-eras.txt";

    private static readonly Lazy<SequenceEras> ShippedTable = new(LoadShipped);

    private readonly Dictionary<int, string> _eraByProtocol;

    private readonly Dictionary<string, Dictionary<string, IReadOnlyList<EraSequence>>> _models;

    private SequenceEras(
        Dictionary<int, string> eraByProtocol,
        Dictionary<string, Dictionary<string, IReadOnlyList<EraSequence>>> models)
    {
        _eraByProtocol = eraByProtocol;
        _models = models;
    }

    /// <summary>The table this program ships, embedded in the assembly.</summary>
    /// <exception cref="InvalidOperationException">The table is not embedded, which is a build fault.</exception>
    public static SequenceEras Shipped => ShippedTable.Value;

    /// <summary>Reads a table.</summary>
    /// <param name="text">The table's text, in the format `Data/sequence-eras.txt` documents.</param>
    /// <returns>The table.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <exception cref="InvalidDataException">A line is malformed, or a model line comes before any era.</exception>
    public static SequenceEras Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Dictionary<int, string> eraByProtocol = [];
        Dictionary<string, Dictionary<string, IReadOnlyList<EraSequence>>> models =
            new(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, IReadOnlyList<EraSequence>>? era = null;
        int number = 0;

        foreach (string raw in text.Split('\n'))
        {
            number++;
            string line = raw.TrimEnd('\r');

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (words[0] == "protocol")
            {
                if (words.Length != 3 ||
                    !int.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out int protocol))
                {
                    throw new InvalidDataException($"Line {number} is not 'protocol <number> <era>': {line}");
                }

                eraByProtocol[protocol] = words[2];
                continue;
            }

            if (words[0] == "era")
            {
                if (words.Length != 2)
                {
                    throw new InvalidDataException($"Line {number} is not 'era <name>': {line}");
                }

                if (!models.TryGetValue(words[1], out era))
                {
                    era = new Dictionary<string, IReadOnlyList<EraSequence>>(StringComparer.OrdinalIgnoreCase);
                    models[words[1]] = era;
                }

                continue;
            }

            if (era is null)
            {
                throw new InvalidDataException($"Line {number} names a model before any era: {line}");
            }

            string[] fields = line.Split('\t');
            List<EraSequence> sequences = new(fields.Length - 1);

            for (int field = 1; field < fields.Length; field++)
            {
                int colon = fields[field].IndexOf(':', StringComparison.Ordinal);

                sequences.Add(colon < 0
                    ? new EraSequence(fields[field], string.Empty)
                    : new EraSequence(fields[field][..colon], fields[field][(colon + 1)..]));
            }

            era[Normalise(fields[0])] = sequences;
        }

        return new SequenceEras(eraByProtocol, models);
    }

    /// <summary>The era a demo's network protocol selects, or null when the table has none for it.</summary>
    /// <param name="networkProtocol">The protocol the demo's header declares.</param>
    /// <returns>The era's name, or null.</returns>
    public string? EraFor(int networkProtocol) =>
        _eraByProtocol.TryGetValue(networkProtocol, out string? era) ? era : null;

    /// <summary>A model's sequence list in an era, or null when it did not differ from today's.</summary>
    /// <param name="era">The era's name.</param>
    /// <param name="modelPath">The model, as the demo or the archives name it.</param>
    /// <returns>The old list, index by index, or null.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Asked per prop per frame, so it allocates nothing on the common path**: a path with no
    /// backslash is looked up as it is, and the dictionaries ignore case.
    /// </remarks>
    public IReadOnlyList<EraSequence>? For(string era, string modelPath)
    {
        ArgumentNullException.ThrowIfNull(era);
        ArgumentNullException.ThrowIfNull(modelPath);

        return _models.TryGetValue(era, out Dictionary<string, IReadOnlyList<EraSequence>>? models) &&
            models.TryGetValue(Normalise(modelPath), out IReadOnlyList<EraSequence>? sequences)
                ? sequences
                : null;
    }

    private static string Normalise(string path) => path.Replace('\\', '/');

    private static SequenceEras LoadShipped()
    {
        using Stream stream = typeof(SequenceEras).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"{ResourceName} is not embedded in {typeof(SequenceEras).Assembly.GetName().Name}.");

        using StreamReader reader = new(stream);

        return Parse(reader.ReadToEnd());
    }
}

/// <summary>Takes an old demo's sequence number to today's model (B380, D160).</summary>
public static class EraSequenceTranslation
{
    /// <summary>Translates one sequence number.</summary>
    /// <param name="era">The model's sequence list in the demo's era.</param>
    /// <param name="sequence">The number the demo networked.</param>
    /// <param name="byLabel">Today's model: the index of the sequence with a label, or −1.</param>
    /// <param name="byActivity">Today's model: the first index with an activity, or −1.</param>
    /// <returns>Today's index for the same animation, or −1 when today's model has none.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **By label first**: the old index names a label, and today's model is asked for that label as
    /// <c>LookupSequence</c> would. **Then by activity**, where the label is gone — the melee
    /// <c>swing</c> became <c>swing_a</c> to <c>swing_c</c> — because the activity is what the server
    /// asked for (<c>SendWeaponAnim( ACT_VM_HITCENTER )</c>). The engine picks among several sequences
    /// of one activity by weighted random (<c>SelectWeightedSequence</c>); this takes the first, which
    /// is a named divergence.
    ///
    /// **−1 rather than the old index when nothing answers.** An index past the old list named nothing
    /// even then, and keeping a number whose meaning has changed would play an animation no part of the
    /// demo asked for; −1 is what <c>StudioSequenceTable.At</c> answers null for.
    /// </remarks>
    public static int Translate(
        IReadOnlyList<EraSequence> era,
        int sequence,
        Func<string, int> byLabel,
        Func<string, int> byActivity)
    {
        ArgumentNullException.ThrowIfNull(era);
        ArgumentNullException.ThrowIfNull(byLabel);
        ArgumentNullException.ThrowIfNull(byActivity);

        if (sequence < 0 || sequence >= era.Count)
        {
            return -1;
        }

        EraSequence old = era[sequence];

        if (old.Label.Length > 0 && byLabel(old.Label) is >= 0 and int named)
        {
            return named;
        }

        if (old.Activity.Length > 0 && byActivity(old.Activity) is >= 0 and int acted)
        {
            return acted;
        }

        return -1;
    }
}
