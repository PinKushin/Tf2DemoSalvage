using System;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Core.Container;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// A demo the viewer has opened: what its header says, and where to find it.
/// </summary>
/// <remarks>
/// **The header, unless the header is empty.** Enabling the transport needs a tick count and a map
/// name, and both are normally in the first 1072 bytes — walking a 39 MB demo to the end to fill in
/// a scrub bar would make opening one feel broken. Entity decoding happens later and per tick,
/// driven by playback.
///
/// The exception is a truncated recording, whose header states zero because nothing went back to
/// write the real counts. Those are measured instead; see <see cref="DemoSurvey"/> for why that is
/// forty-three percent of a real archive rather than a rare accident.
///
/// Separate from <see cref="DemoLibrary"/> on purpose: the library knows which demos exist, this
/// knows what one of them contains. A playlist of two hundred demos reads no headers until one is
/// chosen.
/// </remarks>
public sealed class LoadedDemo
{
    /// <summary>Ticks per second TF2 runs at, used only when the header states no duration.</summary>
    /// <remarks>
    /// **A demo does not record its own tick rate**, so a measured length has to assume one. TF2's
    /// default of 66.667 is right for every competitive recording and wrong for a server running a
    /// custom rate — which is why this is a fallback rather than the normal path, and why a demo
    /// with a stated duration keeps it.
    /// </remarks>
    private const double TicksPerSecond = 66.667;

    private LoadedDemo(string path, DemoHeader header, DemoSurvey survey)
    {
        Path = path;
        MapName = header.MapName;
        NetworkProtocol = header.NetworkProtocol;
        LastTick = Math.Max(0, survey.LastTick);
        LengthWasMeasured = !survey.HeaderStatedLength;
        Truncated = survey.Truncated;

        Duration = header.PlaybackTimeSeconds > 0f
            ? TimeSpan.FromSeconds(header.PlaybackTimeSeconds)
            : TimeSpan.FromSeconds(LastTick / TicksPerSecond);
    }

    /// <summary>Full path to the file.</summary>
    public string Path { get; }

    /// <summary>Map the demo was recorded on.</summary>
    public string MapName { get; }

    /// <summary>Network protocol the recording client spoke.</summary>
    public int NetworkProtocol { get; }

    /// <summary>Highest tick the demo reaches.</summary>
    public int LastTick { get; }

    /// <summary>How long the recording runs.</summary>
    public TimeSpan Duration { get; }

    /// <summary>Whether the length had to be derived by walking the file.</summary>
    public bool LengthWasMeasured { get; }

    /// <summary>Whether the file stops in the middle of a command.</summary>
    public bool Truncated { get; }

    /// <summary>Reads a demo's header.</summary>
    /// <param name="path">Path to a <c>.dem</c> file.</param>
    /// <returns>The loaded demo.</returns>
    /// <exception cref="FileNotFoundException">There is no file at that path.</exception>
    /// <exception cref="InvalidDataException">The file is not a demo, or is truncated.</exception>
    /// <remarks>
    /// Reads exactly the header rather than the file. Besides being fast, it means a corrupt body
    /// still opens far enough to show what the recording claims to be — which is the whole point
    /// of this project, and the opposite of what the game does with a demo it cannot play.
    /// </remarks>
    public static LoadedDemo Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"There is no demo at '{path}'.", path);
        }

        byte[] headerBytes = new byte[DemoHeader.SizeBytes];

        using (FileStream stream = File.OpenRead(path))
        {
            int read = stream.ReadAtLeast(headerBytes, headerBytes.Length, throwOnEndOfStream: false);

            if (read < headerBytes.Length)
            {
                throw new InvalidDataException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{path}' is {read} bytes, too short to hold a {DemoHeader.SizeBytes}-byte " +
                    $"demo header."));
            }
        }

        DemoHeader header = DemoHeader.Parse(headerBytes);

        if (header.PlaybackTicks > 0)
        {
            return new LoadedDemo(
                path, header, new DemoSurvey(header.PlaybackTicks, true, Truncated: false));
        }

        // The header says the demo is empty. That is almost never true - it is what a recording
        // looks like when nothing went back to fill the counts in - so the file gets measured.
        return new LoadedDemo(path, header, DemoSurvey.Measure(File.ReadAllBytes(path)));
    }

    /// <summary>A one-line description for the status bar.</summary>
    /// <returns>Map, duration and protocol, and how the duration was arrived at.</returns>
    /// <remarks>
    /// A measured length is marked as such. The number is a real one, but it was derived rather
    /// than read, and a viewer that silently presents the two as identical gives the user no way
    /// to tell a complete recording from a salvaged one.
    /// </remarks>
    public string Describe()
    {
        string measured = LengthWasMeasured ? " (truncated, length measured)" : string.Empty;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{System.IO.Path.GetFileName(Path)} — {MapName}, {Duration:mm\\:ss}, " +
            $"protocol {NetworkProtocol}{measured}");
    }
}
