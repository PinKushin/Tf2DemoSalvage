using System;
using System.IO;

using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// The viewer's Export and Compile buttons, through the same method they call, on a real demo.
/// </summary>
/// <remarks>
/// <c>z1800.dem</c> is gcor and one byte short of complete, so it also carries a real tail (B448).
/// </remarks>
public sealed class CorpusAssemblyFileTests
{
    [Test]
    public void ExportThenCompile_Z1800_ReproducesBytes()
    {
        string demo = Corpus.Demo("z1800");
        string text = Path.Combine(Path.GetTempPath(), $"tf2ds-z1800-{Guid.NewGuid():N}.txt");
        string rebuilt = Path.ChangeExtension(text, ".dem");

        try
        {
            DemoAssembly.Export(demo, text);
            DemoAssembly.Compile(text, rebuilt);

            File.ReadAllBytes(rebuilt).AsSpan().SequenceEqual(File.ReadAllBytes(demo))
                .ShouldBeTrue("export then compile did not give back z1800.dem byte for byte");
        }
        finally
        {
            File.Delete(text);
            File.Delete(rebuilt);
        }
    }
}
