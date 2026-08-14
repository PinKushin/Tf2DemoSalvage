using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// The other models a model borrows its animations from.
/// </summary>
/// <remarks>
/// **A TF2 player model carries almost none of its own animation.** Measured: scout.mdl declares
/// 306 sequences and two local animations of one frame each — the reference pose that stands it
/// upright, and nothing more. The 1,012 animations it actually plays are in
/// <c>scout_animations.mdl</c>, five megabytes of it, named here.
///
/// <c>studiohdr_t.numincludemodels</c> at 336 and <c>includemodelindex</c> at 340, entries of
/// eight bytes: a label index and a name index, both relative to the entry. The offsets are
/// counted from <c>studio.h</c>'s published field order and anchored on <c>numbodyparts</c> at
/// 232, which this project had already verified against real files — and a health pack reporting
/// zero includes is the control that says they are not landing on arbitrary data.
/// </remarks>
public static class StudioModelGroups
{
    /// <summary><c>studiohdr_t.numincludemodels</c> and <c>includemodelindex</c>.</summary>
    private const int IncludeCountOffset = 336;
    private const int IncludeIndexOffset = 340;

    /// <summary>Bytes per <c>mstudiomodelgroup_t</c>: a label index and a name index.</summary>
    private const int GroupStride = 8;

    private const int NameOffset = 4;

    /// <summary>Most models one may include, as a guard against a malformed header.</summary>
    private const int MaximumGroups = 64;

    /// <summary>Reads the paths of the models this one includes.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <returns>The paths in declaration order, which is the order their sequences merge in.</returns>
    public static IReadOnlyList<string> Read(ReadOnlyMemory<byte> file)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        if (bytes.Length < IncludeIndexOffset + 4)
        {
            return [];
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes[IncludeCountOffset..]);
        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes[IncludeIndexOffset..]);

        if (count <= 0 || count > MaximumGroups)
        {
            return [];
        }

        if (at < 0 || (long)at + ((long)count * GroupStride) > bytes.Length)
        {
            return [];
        }

        List<string> paths = new(count);

        for (int index = 0; index < count; index++)
        {
            int entry = at + (index * GroupStride);

            string name = StudioStrings.At(
                bytes, entry + BinaryPrimitives.ReadInt32LittleEndian(bytes[(entry + NameOffset)..]));

            if (name.Length > 0)
            {
                // Backslashes, because the tools that wrote these ran on Windows and the archives
                // are keyed with forward slashes.
                paths.Add(name.Replace('\\', '/'));
            }
        }

        return paths;
    }
}

/// <summary>
/// One model's sequences merged with those of the models it includes.
/// </summary>
/// <remarks>
/// **This is what a demo's <c>m_nSequence</c> actually indexes.** The engine builds a
/// <c>virtualmodel_t</c> whose sequence list spans the base model and every model it includes, and
/// <c>AppendSequences</c> (<c>public/studio_virtualmodel.cpp:142</c>) merges them **by label**:
/// the base model's sequences first, then each included model contributes only those whose names
/// are not already present.
///
/// Merging by name rather than concatenating is the part that matters. An animation model
/// re-declares sequences the base model already has, so concatenating would shift every later
/// index and resolve a demo's sequence number to the wrong animation from that point on.
///
/// **A virtual sequence resolves to a group and a local sequence**, and that group's own model
/// holds both the sequence description and the animation it names — so the virtual ANIMATION list
/// never has to be built, which is most of the complexity avoided.
/// </remarks>
public sealed class StudioSequenceTable
{
    private readonly List<(int Group, int Local)> _entries;

    private StudioSequenceTable(List<(int Group, int Local)> entries) => _entries = entries;

    /// <summary>How many sequences the merged list holds.</summary>
    public int Count => _entries.Count;

    /// <summary>Merges several models' sequences the way the engine does.</summary>
    /// <param name="groups">Each model's group number and its own sequences, base model first.</param>
    /// <returns>The merged table.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="groups"/> is null.</exception>
    public static StudioSequenceTable Merge(
        IReadOnlyList<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        List<(int Group, int Local)> entries = [];
        List<bool> declared = [];
        Dictionary<string, int> byLabel = new(StringComparer.OrdinalIgnoreCase);

        foreach ((int group, IReadOnlyList<StudioSequence> sequences) in groups)
        {
            for (int local = 0; local < sequences.Count; local++)
            {
                StudioSequence sequence = sequences[local];

                // An unnamed sequence cannot be matched by name, so it is kept rather than folded
                // into whatever other unnamed one came first.
                if (sequence.Label.Length == 0 ||
                    !byLabel.TryGetValue(sequence.Label, out int already))
                {
                    if (sequence.Label.Length > 0)
                    {
                        byLabel[sequence.Label] = entries.Count;
                    }

                    entries.Add((group, local));
                    declared.Add(sequence.IsForwardDeclaration);
                    continue;
                }

                // **A forward declaration is replaced, not skipped.** A player model holds the
                // NAME of every sequence it can play and an empty animation behind it; the real
                // one arrives with an included animation model. Valve replaces in place so the
                // index does not move - a demo's sequence number has to keep meaning the same
                // thing - and skips only when what is already there is real.
                if (!declared[already])
                {
                    continue;
                }

                entries[already] = (group, local);
                declared[already] = sequence.IsForwardDeclaration;
            }
        }

        return new StudioSequenceTable(entries);
    }

    /// <summary>Which model and which of its own sequences a virtual sequence number means.</summary>
    /// <param name="sequence">The number a demo networked.</param>
    /// <returns>The group and its local sequence index, or <c>null</c> when out of range.</returns>
    /// <remarks>
    /// **Null rather than a fallback.** A demo can name a sequence a later game version added, and
    /// answering with some other animation would play convincing motion that never happened.
    /// </remarks>
    public (int Group, int Local)? At(int sequence) =>
        sequence >= 0 && sequence < _entries.Count ? _entries[sequence] : null;
}
