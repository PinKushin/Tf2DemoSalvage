using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Container;

/// <summary>
/// The trace and the assembly written off a streamed demo are the bytes written off a held one (B449).
/// </summary>
/// <remarks>
/// **The control is the old path, run beside the new one on the same file.** The held path reads the
/// file into one array and walks it with <see cref="DemoCommandReader.ReadWhole"/>; the streamed one
/// opens a <see cref="DemoCommandCollection"/> and never holds more than a command. Both texts are
/// hashed as written, so a demo whose trace runs to hundreds of megabytes is compared without being held.
/// </remarks>
public sealed class CorpusStreamedOutputTests
{
    [Test]
    public void Write_StreamedDemo_IsByteIdenticalToTheHeldDemo()
    {
        int compared = 0;

        foreach (string path in Corpus.Files())
        {
            string name = Path.GetFileName(path);
            byte[] bytes = File.ReadAllBytes(path);
            DemoHeader header = DemoHeader.Parse(bytes);
            (IReadOnlyList<DemoCommand> held, ReadOnlyMemory<byte> tail) =
                DemoCommandReader.ReadWhole(bytes.AsMemory(DemoHeader.SizeBytes));

            DemoCommandCollection streamed = DemoCommandCollection.Open(path);

            streamed.Count.ShouldBe(held.Count, name);
            streamed.Tail.ToArray().ShouldBe(tail.ToArray(), name);

            Hash(writer => DemoTraceWriter.Write(writer, name, streamed.Header, streamed))
                .ShouldBe(Hash(writer => DemoTraceWriter.Write(writer, name, header, held)), $"{name}: trace");

            Hash(writer => DemoAssembly.Write(writer, streamed.Header, streamed, streamed.Tail))
                .ShouldBe(Hash(writer => DemoAssembly.Write(writer, header, held, tail)), $"{name}: assembly");

            compared++;
        }

        compared.ShouldBeGreaterThan(0);
    }

    /// <summary>The SHA-256 of what <paramref name="write"/> produces, as UTF-8, never held as a string.</summary>
    private static string Hash(Action<TextWriter> write)
    {
        using IncrementalHashStream sink = new();
        using (StreamWriter writer = new(sink, new UTF8Encoding(false), 1 << 16, leaveOpen: true))
        {
            write(writer);
        }

        return sink.Finish();
    }

    /// <summary>A write-only stream that hashes what goes through it.</summary>
    private sealed class IncrementalHashStream : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public string Finish() => Convert.ToHexString(_hash.GetHashAndReset());

        public override void Write(byte[] buffer, int offset, int count) => _hash.AppendData(buffer, offset, count);

        public override void Write(ReadOnlySpan<byte> buffer) => _hash.AppendData(buffer);

        public override void Flush()
        {
            // Nothing is buffered: every write is already in the hash.
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hash.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
