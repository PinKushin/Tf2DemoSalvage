using System;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// That <see cref="StudioJiggleBones.Read"/> is total on input it cannot describe.
/// </summary>
/// <remarks>
/// **Written because something else was silently depending on it** (B333).
/// `SkeletonPose.Jiggle` guards with `if (JiggleSource is not { } model) return;`, and that guard
/// was dead: `EntityModels` assigned it from a `byte[]` conditional whose null arm converts to
/// `ReadOnlyMemory.Empty`, which is present. A model with no bytes reached the reader on every
/// prop that had no models at all.
///
/// **Nothing was visibly wrong, and this is why** — the reader answers null for a span too short to
/// hold even its own bone index, so the next guard down caught what the first one should have. That
/// is an accident, and an accident is worth pinning before it is relied on again: the assignment is
/// fixed, and this says what would happen if a third route ever hands the reader nothing.
/// </remarks>
public sealed class StudioJiggleBoneReaderTests
{
    [Test]
    public void Read_FromAModelWithNoBytesAtAll_IsNull()
    {
        StudioJiggleBones.Read(ReadOnlyMemory<byte>.Empty, 0).ShouldBeNull();
    }

    /// <remarks>
    /// **The boundary, one byte either side of the header field the reader needs.** A length test
    /// written as `&lt;=` rather than `&lt;` is invisible to an empty input and to a real model
    /// alike, which is the class of off-by-one that only a boundary can see.
    /// </remarks>
    [Test]
    public void Read_FromAModelOneByteShortOfItsBoneIndex_IsNull()
    {
        StudioJiggleBones.Read(new byte[StudioLayout.HeaderBoneIndexOffset + 3], 0).ShouldBeNull();
    }

    /// <remarks>
    /// **The control, and without it the two above are satisfied by a reader that returns null for
    /// everything.** A header long enough to be read, declaring no bones, is a legitimate answer of
    /// none rather than a refusal — and it proves the length guard is what rejected the two above.
    /// </remarks>
    [Test]
    public void Read_FromAHeaderDeclaringNoBones_IsNullWithoutRefusingToRead()
    {
        byte[] model = new byte[StudioLayout.HeaderBoneIndexOffset + sizeof(int)];

        StudioJiggleBones.Read(model, 0).ShouldBeNull("bone 0 is past a count of zero");
    }

    /// <remarks>
    /// A negative bone is a caller's mistake rather than a malformed file, and the reader says so
    /// rather than answering null — which would make an out-of-range index look like a model that
    /// simply has no jiggle on that bone.
    /// </remarks>
    [Test]
    public void Read_WithANegativeBone_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => StudioJiggleBones.Read(ReadOnlyMemory<byte>.Empty, -1));
    }
}
