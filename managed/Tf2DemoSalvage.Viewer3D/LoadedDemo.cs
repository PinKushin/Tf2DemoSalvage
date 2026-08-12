using System;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Core.Container;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>
/// A demo the viewer has opened: what its header says, and where to find it.
/// </summary>
/// <remarks>
/// **The header only.** Enabling the transport needs a tick count and a map name, and both are in
/// the first 1072 bytes — walking a 39 MB demo to the end to fill in a scrub bar would make
/// opening one feel broken. Entity decoding happens later and per tick, driven by playback.
///
/// Separate from <see cref="DemoLibrary"/> on purpose: the library knows which demos exist, this
/// knows what one of them contains. A playlist of two hundred demos reads no headers until one is
/// chosen.
/// </remarks>
internal sealed class LoadedDemo
{
    private LoadedDemo(string path, DemoHeader header)
    {
        Path = path;
        MapName = header.MapName;
        NetworkProtocol = header.NetworkProtocol;
        LastTick = Math.Max(0, header.PlaybackTicks);
        Duration = TimeSpan.FromSeconds(Math.Max(0f, header.PlaybackTimeSeconds));
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

        byte[] header = new byte[DemoHeader.SizeBytes];

        using (FileStream stream = File.OpenRead(path))
        {
            int read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);

            if (read < header.Length)
            {
                throw new InvalidDataException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{path}' is {read} bytes, too short to hold a {DemoHeader.SizeBytes}-byte " +
                    $"demo header."));
            }
        }

        return new LoadedDemo(path, DemoHeader.Parse(header));
    }

    /// <summary>A one-line description for the status bar.</summary>
    /// <returns>Map, duration and protocol.</returns>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"{System.IO.Path.GetFileName(Path)} — {MapName}, {Duration:mm\\:ss}, protocol {NetworkProtocol}");
}
