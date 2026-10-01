using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace Tf2DemoSalvage.Core.Container;

/// <summary>
/// A demo's commands, read off its file each time they are walked rather than held (B449).
/// </summary>
/// <remarks>
/// **Memory is one command, not the file.** Opening walks the file once for the count and the tail —
/// the two things a writer needs before it starts — and every enumeration opens the file again and
/// streams it through <see cref="DemoCommandReader.Read(Stream, Action{ReadOnlyMemory{byte}, string})"/>.
/// A writer that makes a second pass (the trace's schema scan, the assembly's type width) reads the
/// file twice instead of holding it once: a 2 GB idle-server recording is the case this exists for.
/// </remarks>
public sealed class DemoCommandCollection : IReadOnlyCollection<DemoCommand>
{
    /// <summary>Read-ahead for the file stream; the reader asks for a few bytes at a time.</summary>
    private const int BufferBytes = 1 << 16;

    private readonly string _path;

    private DemoCommandCollection(string path, DemoHeader header)
    {
        _path = path;
        Header = header;
    }

    /// <summary>The demo's header.</summary>
    public DemoHeader Header { get; }

    /// <summary>How many whole commands the file holds.</summary>
    public int Count { get; private set; }

    /// <summary>The bytes after the last whole command — empty unless the file was cut (B448).</summary>
    public ReadOnlyMemory<byte> Tail { get; private set; } = ReadOnlyMemory<byte>.Empty;

    /// <summary>Why the walk stopped short, or <c>null</c> for a file that ends on a whole command.</summary>
    public string? Truncated { get; private set; }

    /// <summary>Reads the header and walks the commands once to count them.</summary>
    /// <param name="path">The demo.</param>
    /// <returns>The file, ready to enumerate as often as a writer needs.</returns>
    /// <exception cref="EndOfStreamException">The file is shorter than a header.</exception>
    /// <exception cref="InvalidDataException">The file is not a demo, or a command is corrupt.</exception>
    public static DemoCommandCollection Open(string path)
    {
        using FileStream stream = OpenRead(path);
        byte[] header = new byte[DemoHeader.SizeBytes];
        int read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);

        DemoCommandCollection file = new(path, DemoHeader.Parse(header.AsSpan(0, read)));

        foreach (DemoCommand _ in DemoCommandReader.Read(stream, (tail, reason) => (file.Tail, file.Truncated) = (tail, reason)))
        {
            file.Count++;
        }

        return file;
    }

    /// <inheritdoc/>
    public IEnumerator<DemoCommand> GetEnumerator()
    {
        using FileStream stream = OpenRead(_path);
        stream.Seek(DemoHeader.SizeBytes, SeekOrigin.Begin);

        foreach (DemoCommand command in DemoCommandReader.Read(stream))
        {
            yield return command;
        }
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, FileOptions.SequentialScan);
}
