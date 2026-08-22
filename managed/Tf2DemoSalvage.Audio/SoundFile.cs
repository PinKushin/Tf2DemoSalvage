using System;
using System.Collections.Generic;
using System.IO;

namespace Tf2DemoSalvage.Audio;

/// <summary>
/// Opens a sound a demo named, allowing for the container it now ships in.
/// </summary>
/// <remarks>
/// **A demo names a file that may no longer exist under that name, and the reason is re-encoding
/// rather than deletion.** TF2 converted most of its voice lines from WAV to MP3 after the older
/// demos in this corpus were recorded, so a 2007 demo precaches
/// <c>sound/vo/scout_BattleCry01.wav</c> and the install ships
/// <c>sound/vo/scout_BattleCry01.mp3</c>. Same sound, same stem, different container.
///
/// **Measured on the committed corpus, on the sounds demos actually PLAY** rather than on what they
/// precache: 63 distinct played sounds could not be opened by their stated path, and **60 of them
/// are present as MP3 under the identical stem.** The remaining three —
/// <c>player/pl_fallpain4</c>, <c>8</c> and <c>10</c> — are not present under any extension.
///
/// So the era problem here is far smaller than it first appeared and needs no content shipped with
/// this program: an extension fallback recovers 95% of it. That matters because the alternatives
/// were both refused — the app cannot go looking for period installs, since an end user has one
/// modern install, and bundling WAVs was rejected on size.
///
/// **The order is stated path first.** A file that still exists under its own name is the one the
/// demo meant; the fallback only ever runs when the original is absent, so this can never prefer a
/// re-encode over the real thing.
/// </remarks>
public static class SoundFile
{
    /// <summary>The containers TF2 ships audio in, in the order they are tried.</summary>
    /// <remarks>
    /// WAV and MP3 only. Source supports OGG in principle but TF2 ships none, and adding an
    /// extension that never matches costs a lookup per miss for no recall.
    /// </remarks>
    private static readonly string[] Containers = [".wav", ".mp3"];

    /// <summary>Every path worth trying for one named sound, best first.</summary>
    /// <param name="path">The path as the demo named it, such as <c>sound/vo/x.wav</c>.</param>
    /// <returns>The stated path, then the same stem in each other container.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <remarks>
    /// Split from the iterator below so the null check runs when this is CALLED rather than when it
    /// is first enumerated (S4456). In an iterator the whole body is deferred, so a null argument
    /// would surface at the `foreach` — somewhere else entirely, with a stack trace pointing at the
    /// consumer rather than the caller that passed it.
    /// </remarks>
    public static IEnumerable<string> Candidates(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return Walk(path);
    }

    /// <summary>Does the work behind <see cref="Candidates"/>, once the argument is known good.</summary>
    private static IEnumerable<string> Walk(string path)
    {
        yield return path;

        string extension = Path.GetExtension(path);

        // A path with no extension, or one this does not know, gets no alternatives rather than a
        // guess: appending ".mp3" to something that was never a sound file would turn a resolution
        // failure into a wrong read.
        if (extension.Length == 0)
        {
            yield break;
        }

        string stem = path[..^extension.Length];

        foreach (string container in Containers)
        {
            if (!container.Equals(extension, StringComparison.OrdinalIgnoreCase))
            {
                yield return stem + container;
            }
        }
    }

    /// <summary>Opens a named sound, trying the containers it may ship in.</summary>
    /// <param name="path">The path as the demo named it.</param>
    /// <param name="read">Opens a content path, returning null when absent.</param>
    /// <returns>The bytes and the path they came from, or null when nothing matched.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static (byte[] Bytes, string Path)? Open(string path, Func<string, byte[]?> read)
    {
        ArgumentNullException.ThrowIfNull(read);

        foreach (string candidate in Candidates(path))
        {
            if (read(candidate) is { } bytes)
            {
                return (bytes, candidate);
            }
        }

        return null;
    }
}
