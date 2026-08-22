using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;

// Namespace matches CorpusSoundTests beside it rather than the folder: inside
// `Tf2DemoSalvage.Corpus.Tests.Net`, the identifier `Corpus` binds to the NAMESPACE
// `Tf2DemoSalvage.Corpus` instead of to the corpus helper class, and every call to it fails to
// compile. The sibling files already sit here for that reason.
namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// How many precached sound names carry Valve's prefix characters, counted on real demos.
/// </summary>
/// <remarks>
/// **Written before any sound playback exists, to find out whether a known trap is a real one
/// here.** `public/soundchars.h` declares ten characters that may appear as one of the first two
/// characters of a sound name — <c>*</c> streaming, <c>#</c> bypass DSP, <c>)</c> spatialised
/// stereo, <c>^</c> distance variant, <c>@</c> omnidirectional, and five more — and
/// <c>PSkipSoundChars</c> skips them before the name is used as a path.
///
/// So the naive implementation, <c>archives.Read("sound/" + name)</c>, returns null for every one
/// of them. **It fails as SILENCE**, which is indistinguishable from a sound that has not been
/// implemented yet, on a feature whose whole output is sound. That is the worst possible failure
/// mode and the reason to measure the population before writing the reader rather than after.
///
/// `[Explicit]` because it is a measurement rather than a gate; its numbers belong in a finding.
/// </remarks>
[Explicit("Counts sound-name prefixes across the corpus; run deliberately.")]
public sealed class SoundCharProbe
{
    /// <summary>Valve's ten, from <c>public/soundchars.h</c>.</summary>
    private const string SoundChars = "*?!#>< ^@)}";

    [Test]
    public void SoundNames_AcrossTheCorpus_AreCountedByPrefix()
    {
        Dictionary<char, int> byChar = [];
        int total = 0;
        int prefixed = 0;
        int demos = 0;

        foreach (string path in Corpus.Files())
        {
            List<string> names = PrecachedNames(path);

            if (names.Count == 0)
            {
                continue;
            }

            demos++;

            foreach (string name in names)
            {
                total++;

                // "as one of 1st 2 chars", so a name may carry two of them.
                bool any = false;

                foreach (char c in name.Take(2))
                {
                    if (SoundChars.Contains(c, StringComparison.Ordinal) && c != ' ')
                    {
                        byChar[c] = byChar.GetValueOrDefault(c) + 1;
                        any = true;
                    }
                }

                if (any)
                {
                    prefixed++;
                }
            }
        }

        TestContext.Out.WriteLine($"demos with a sound table: {demos}");
        TestContext.Out.WriteLine($"precached names: {total}");
        TestContext.Out.WriteLine(
            $"names carrying a prefix character: {prefixed} "
            + $"({(total == 0 ? 0 : prefixed * 100.0 / total):0.0}%)");

        foreach ((char c, int count) in byChar.OrderByDescending(entry => entry.Value))
        {
            TestContext.Out.WriteLine($"  '{c}'  {count}");
        }

        total.ShouldBeGreaterThan(0, "no sound names were read, so the counts mean nothing");
    }

    /// <summary>Every name in a demo's <c>soundprecache</c> table.</summary>
    private static List<string> PrecachedNames(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        ushort protocol = Corpus.ProtocolOf(path);
        NetDecodeState state = new() { NetworkProtocol = protocol };
        SoundNames names = new();

        foreach (DemoCommand command in
            DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)).Take(400))
        {
            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            foreach (INetMessage message in
                NetMessageReader.Read(command.Payload.Span, state).Messages)
            {
                if (message is CreateStringTableMessage table)
                {
                    names.Add(table);
                }
            }
        }

        List<string> found = [];

        for (int index = 0; index < 65536 && found.Count < names.Count; index++)
        {
            if (names.Resolve(index) is { } name)
            {
                found.Add(name);
            }
        }

        return found;
    }
}
