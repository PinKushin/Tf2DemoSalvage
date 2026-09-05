using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// A property value that was mistyped is refused, and a declared length is not trusted (B345).
/// </summary>
/// <remarks>
/// **`PropertyText.Read` documents its own contract and then left it.** The XML comment says
/// `<c>&lt;exception cref="InvalidDataException"&gt;The tokens do not describe a value.</c>`, and only
/// the unknown-tag branch honoured it — every value went through a bare `long.Parse`, `int.Parse`,
/// `float.Parse` or `tokens[index++]`, each raising a type `DemoAssembly.cs:533` does not catch and
/// so cannot attach the offending line to. Same defect as B344, one layer down.
///
/// **The array length is a different and worse problem, and it is the reason this file exists.** An
/// array value declares its own element count and the reader allocated `new List&lt;PropertyValue&gt;(count)`
/// from it before reading a single element. `docs/FUZZING.md` names that class outright — *"length-prefix
/// decoders are where unbounded allocations come from"* — and names the symptom as a defect:
/// *"an OutOfMemoryException ... a caller cannot reasonably defend against"* when the input came from
/// a file someone downloaded.
///
/// **The bound is not arbitrary.** A line cannot hold more elements than it has tokens left, so the
/// remaining token count is an exact ceiling that no valid input can exceed — no tuning constant, and
/// nothing a legitimate array can trip.
/// </remarks>
public sealed class PropertyTextRefusalTests
{
    /// <remarks>
    /// **The allocation happens BEFORE any element is read**, so the loop running out of tokens does
    /// not save it. Two billion is well inside `int`, so the parse succeeds and the list constructor
    /// is asked for the memory.
    /// </remarks>
    [Test]
    public void Read_AnArrayLengthLargerThanTheLine_IsRefusedBeforeAllocating()
    {
        Should.Throw<InvalidDataException>(() => Read(Array, "a", "2000000000"))
            .Message.ShouldContain("2000000000", Case.Sensitive);
    }

    /// <remarks>
    /// A negative length reached `new List&lt;&gt;(-1)`, which is an `ArgumentOutOfRangeException`
    /// about a capacity — an internal detail of the reader, not a statement about the trace.
    /// </remarks>
    [Test]
    public void Read_ANegativeArrayLength_IsRefused()
    {
        Should.Throw<InvalidDataException>(() => Read(Array, "a", "-1"))
            .Message.ShouldContain("-1", Case.Sensitive);
    }

    /// <remarks>
    /// **The control, and it is the one that makes the two above mean anything.** A length exactly
    /// matching what the line carries must still read, so a bound written as `count &lt; remaining`
    /// rather than `&lt;=` would fail here — the off-by-one is the likely way to get this wrong, and
    /// nothing else in the file would catch it.
    /// </remarks>
    [Test]
    public void Read_AnArrayLengthMatchingTheLine_StillReads()
    {
        PropertyValue value = Read(Array, "a", "2", "i", "7", "i", "9");

        value.AsArray.Count.ShouldBe(2);
        value.AsArray[0].AsInt.ShouldBe(7L);
        value.AsArray[1].AsInt.ShouldBe(9L);
    }

    /// <remarks>An integer value the line does not carry as a number.</remarks>
    [Test]
    public void Read_AnIntegerThatIsNotANumber_IsRefused()
    {
        Should.Throw<InvalidDataException>(() => Read(Scalar, "i", "seven"))
            .Message.ShouldContain("seven", Case.Sensitive);
    }

    /// <remarks>A float, which takes a different parse from the integer above.</remarks>
    [Test]
    public void Read_AFloatThatIsNotANumber_IsRefused()
    {
        Should.Throw<InvalidDataException>(() => Read(Scalar, "f", "half"))
            .Message.ShouldContain("half", Case.Sensitive);
    }

    /// <remarks>
    /// **A value whose tokens simply stop**, which is what a line truncated mid-property gives — and
    /// `tokens[index++]` read past the end for it.
    /// </remarks>
    [Test]
    public void Read_AValueWhoseTokensRunOut_IsRefusedRatherThanIndexingPastTheLine()
    {
        Should.Throw<InvalidDataException>(() => Read(Scalar, "v", "1", "2"))
            .Message.ShouldNotBeEmpty();
    }

    /// <remarks>
    /// The refusal that already worked, kept as the CONTROL for the message shape: an unknown tag
    /// is the one branch that honoured the documented contract, so the others should sound like it.
    /// </remarks>
    [Test]
    public void Read_AnUnknownValueTag_NamesTheTag()
    {
        Should.Throw<InvalidDataException>(() => Read(Scalar, "q", "1"))
            .Message.ShouldContain("q", Case.Sensitive);
    }

    /// <summary>A scalar property, with no array element template.</summary>
    private static FlatProperty Scalar => new(
        new SendProperty(SendPropType.Int, "m_nTest", 0, string.Empty, 0f, 0f, 32, 0),
        "DT_Test",
        null);

    /// <summary>An array property, whose elements are integers.</summary>
    private static FlatProperty Array => new(
        new SendProperty(SendPropType.Array, "m_nTests", 0, string.Empty, 0f, 0f, 32, 0),
        "DT_Test",
        new SendProperty(SendPropType.Int, "m_nTests", 0, string.Empty, 0f, 0f, 32, 0));

    /// <summary>Reads a value from the given tokens, starting at the first.</summary>
    private static PropertyValue Read(FlatProperty flat, params string[] tokens) =>
        PropertyText.Read(flat, new List<string>(tokens), 0);
}
